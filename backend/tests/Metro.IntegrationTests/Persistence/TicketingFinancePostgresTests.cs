using Metro.Domain.Entities.Tickets;
using System.Text.Json;
using Metro.Domain.Entities.Finance;
using Metro.Domain.Entities.LostProperty;
using Metro.Domain.Entities.Passengers;
using Metro.Domain.Entities.Ticketing;
using Metro.Infrastructure.Persistence.Context;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Metro.IntegrationTests.Persistence;

public class TicketingFinancePostgresTests
{
    private static readonly Guid OperatorId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherOperatorId = Guid.Parse("a0000000-0000-0000-0000-000000000002");
    private static readonly Guid StationId = Guid.Parse("a2000000-0000-0000-0000-000000000001");
    private static readonly Guid DestinationId = Guid.Parse("a2000000-0000-0000-0000-000000000002");
    private static readonly Guid ScheduleId = Guid.Parse("a4000000-0000-0000-0000-000000000001");
    private static readonly Guid TripId = Guid.Parse("a5000000-0000-0000-0000-000000000001");
    private static readonly Guid OrderId = Guid.Parse("a8000000-0000-0000-0000-000000000001");
    private static readonly Guid TicketId = Guid.Parse("aa000000-0000-0000-0000-000000000001");
    private static readonly Guid ChildTicketId = Guid.Parse("aa000000-0000-0000-0000-000000000002");
    private static readonly DateOnly ServiceDate = new(2026, 11, 16);
    private static readonly DateTimeOffset ScanAt = new(2026, 11, 16, 1, 0, 0, TimeSpan.Zero);
    private static readonly string Hash = new('a', 64);

    private static ApplicationDbContext CreateContext(Guid? operatorId = null) => TicketingFinanceModelTests.CreateContext(
        PostgreSqlTestConfiguration.LoadRequired().ConnectionString, operatorId);

    private static async Task<ApplicationDbContext> SeedAsync(Guid? operatorId = null)
    {
        var db = CreateContext(operatorId);
        try
        {
            await db.Database.BeginTransactionAsync();
            var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
                "Persistence", "Fixtures", "TicketingFinanceSeed.sql"));
            await using var seed = new NpgsqlCommand(sql, (NpgsqlConnection)db.Database.GetDbConnection(),
                (NpgsqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction());
            await seed.ExecuteNonQueryAsync();
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    private static Payment NewPayment(string code = "EF-PAYMENT") => new()
    {
        OperatorId = OperatorId, OrderId = OrderId, PaymentCode = code, Provider = "MOMO_TEST",
        Method = PaymentMethod.SandboxGateway, AmountMinor = 10000, IdempotencyKey = code
    };

    private static async Task AssertDatabaseRejectsAsync(ApplicationDbContext db, string sqlState)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(sqlState, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [PostgreSqlFact]
    public async Task Every_mapped_column_and_foreign_key_matches_the_deployed_SQL_schema()
    {
        await using var db = CreateContext();
        await db.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        foreach (var entity in db.Model.GetEntityTypes().Where(e => TicketingFinanceModelTests.MappedTypes.Contains(e.ClrType)))
        {
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, "metro");
            await using var command = new NpgsqlCommand("""
                SELECT a.attname, format_type(a.atttypid, a.atttypmod), NOT a.attnotnull
                FROM pg_attribute a
                WHERE a.attrelid = to_regclass('metro.' || @table) AND a.attnum > 0 AND NOT a.attisdropped
                """, connection);
            command.Parameters.AddWithValue("table", table.Name);
            var actual = new Dictionary<string, (string Type, bool Nullable)>();
            await using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                    actual.Add(reader.GetString(0), (reader.GetString(1), reader.GetBoolean(2)));

            Assert.Equal(entity.GetProperties().Count(), actual.Count);
            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName(table)!;
                Assert.True(actual.TryGetValue(column, out var stored), $"Missing {table.Name}.{column}");
                Assert.Equal(property.IsNullable, stored.Nullable);
                Assert.Equal(NormalizeType(property.GetColumnType()!), NormalizeType(stored.Type));
            }

            // Compare ordered local/principal column lists, including composite operator keys.
            await using var foreignKeys = new NpgsqlCommand("""
                SELECT pn.nspname, pc.relname,
                       array_agg(la.attname::text ORDER BY keys.position),
                       array_agg(pa.attname::text ORDER BY keys.position), c.confdeltype::text
                FROM pg_constraint c
                JOIN pg_class pc ON pc.oid = c.confrelid
                JOIN pg_namespace pn ON pn.oid = pc.relnamespace
                CROSS JOIN LATERAL unnest(c.conkey, c.confkey) WITH ORDINALITY keys(local_id, principal_id, position)
                JOIN pg_attribute la ON la.attrelid = c.conrelid AND la.attnum = keys.local_id
                JOIN pg_attribute pa ON pa.attrelid = c.confrelid AND pa.attnum = keys.principal_id
                WHERE c.contype = 'f' AND c.conrelid = to_regclass('metro.' || @table)
                GROUP BY c.oid, pn.nspname, pc.relname, c.confdeltype
                """, connection);
            foreignKeys.Parameters.AddWithValue("table", table.Name);
            var actualKeys = new HashSet<string>();
            await using (var reader = await foreignKeys.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                {
                    Assert.Equal("a", reader.GetString(4)); // SQL default ON DELETE NO ACTION
                    actualKeys.Add($"{reader.GetString(0)}.{reader.GetString(1)}:{string.Join(',', reader.GetFieldValue<string[]>(2))}:{string.Join(',', reader.GetFieldValue<string[]>(3))}");
                }
            var mappedKeys = entity.GetForeignKeys().Select(fk =>
            {
                var principal = StoreObjectIdentifier.Table(fk.PrincipalEntityType.GetTableName()!, fk.PrincipalEntityType.GetSchema());
                return $"{principal.Schema ?? "public"}.{principal.Name}:{string.Join(',', fk.Properties.Select(p => p.GetColumnName(table)))}:{string.Join(',', fk.PrincipalKey.Properties.Select(p => p.GetColumnName(principal)))}";
            }).ToHashSet();
            Assert.True(actualKeys.SetEquals(mappedKeys), $"Foreign keys differ for {table.Name}");
        }
    }

    private static string NormalizeType(string type) => type
        .Replace("character varying", "varchar").Replace("character(", "char(")
        .Replace("timestamp with time zone", "timestamptz");

    [PostgreSqlFact]
    public async Task Payments_and_verified_events_roundtrip_defaults_and_reject_duplicate_callbacks()
    {
        await using var db = await SeedAsync();
        var payment = NewPayment();
        var second = NewPayment("EF-PAYMENT-2");
        db.Payments.AddRange(payment, second);
        await db.SaveChangesAsync();
        Assert.NotEqual(Guid.Empty, payment.Id);
        Assert.NotEqual(Guid.Empty, payment.CorrelationId);
        Assert.True(payment.CreatedAt > DateTimeOffset.MinValue);
        Assert.Null(payment.ProviderPaymentReference);
        Assert.Null(second.ProviderPaymentReference);

        var callback = new PaymentEvent
        {
            OperatorId = OperatorId, Payment = payment, ProviderEventId = "EF-EVENT", IdempotencyKey = "EF-EVENT",
            EventType = "PAYMENT_RESULT", SignatureVerified = true, ReceivedAmountMinor = 10000,
            ReceivedCurrency = "VND", PayloadHash = Hash, Outcome = "VERIFIED", ProcessedAt = ScanAt
        };
        db.PaymentEvents.Add(callback);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var stored = await db.PaymentEvents.Include(e => e.Payment).SingleAsync();
        Assert.Equal(PaymentStatus.Pending, stored.Payment.Status);
        Assert.Equal(PaymentMethod.SandboxGateway, stored.Payment.Method);
        Assert.Equal("VND", stored.Payment.Currency);
        Assert.Equal(Hash, stored.PayloadHash);
        Assert.True(stored.SignatureVerified);
        Assert.Equal(ScanAt, stored.ProcessedAt);

        db.PaymentEvents.Add(new PaymentEvent
        {
            OperatorId = OperatorId, PaymentId = payment.Id, ProviderEventId = "EF-EVENT",
            IdempotencyKey = "EF-EVENT-REPLAY", EventType = "PAYMENT_RESULT", SignatureVerified = true,
            PayloadHash = Hash, Outcome = "REPLAY"
        });
        await AssertDatabaseRejectsAsync(db, PostgresErrorCodes.UniqueViolation);
    }

    [PostgreSqlFact]
    public async Task Composite_foreign_key_rejects_a_payment_for_another_operators_order()
    {
        // The write is valid for this context's operator, but the order belongs to another operator.
        // This exercises the database FK after the BE-02A write guard has passed.
        await using var db = await SeedAsync(OtherOperatorId);
        var payment = NewPayment();
        payment.OperatorId = OtherOperatorId;
        db.Payments.Add(payment);
        await AssertDatabaseRejectsAsync(db, PostgresErrorCodes.ForeignKeyViolation);
    }

    [PostgreSqlFact]
    public async Task Refund_history_roundtrips_json_ciphertext_and_unknown_outcomes()
    {
        await using var db = await SeedAsync();
        var payment = NewPayment();
        var request = new RefundRequest
        {
            OperatorId = OperatorId, RequestCode = "EF-REFUND", TicketId = TicketId, Payment = payment,
            ReasonType = "TECHNICAL_ERROR", RequestedAmountMinor = 10000, SubmittedByUserId = "ef-owner"
        };
        var refund = new RefundTransaction
        {
            OperatorId = OperatorId, RefundRequest = request, Payment = payment, RefundCode = "EF-REFUND-TX",
            Provider = "MOMO_TEST", Method = "SANDBOX_GATEWAY", AmountMinor = 10000, IdempotencyKey = "EF-REFUND-TX",
            Status = RefundTransactionStatus.Unknown, PayoutRecipientSnapshot = "{\"originalPayment\":true}"
        };
        var attempt = new RefundAttempt
        {
            OperatorId = OperatorId, RefundTransaction = refund, AttemptNo = 1, RequestHash = Hash,
            Status = RefundAttemptStatus.Timeout, ErrorCode = "TIMEOUT"
        };
        var revision = new RefundPayoutRevision
        {
            OperatorId = OperatorId, RefundRequest = request, RevisionNo = 1, BankCode = "EF-BANK",
            AccountCiphertext = [1, 2, 3], AccountLast4 = "1234", HolderName = "Demo",
            ChangedByUserId = "ef-admin", ChangeReason = "Test history"
        };
        db.AddRange(attempt, revision);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var stored = await db.RefundAttempts.Include(a => a.RefundTransaction).ThenInclude(r => r.RefundRequest).SingleAsync();
        Assert.Equal(RefundAttemptStatus.Timeout, stored.Status);
        Assert.Equal(RefundTransactionStatus.Unknown, stored.RefundTransaction.Status);
        Assert.Null(stored.RefundTransaction.ProviderRefundReference);
        using var snapshot = JsonDocument.Parse(stored.RefundTransaction.PayoutRecipientSnapshot);
        Assert.True(snapshot.RootElement.GetProperty("originalPayment").GetBoolean());
        Assert.Equal(10000, stored.RefundTransaction.RefundRequest.RequestedAmountMinor);
        Assert.Equal(new byte[] { 1, 2, 3 }, (await db.RefundPayoutRevisions.SingleAsync()).AccountCiphertext);

        db.RefundTransactions.Add(new RefundTransaction
        {
            OperatorId = OperatorId, RefundRequestId = request.Id, PaymentId = payment.Id,
            RefundCode = "EF-REFUND-DUPLICATE", Provider = "MOMO_TEST", Method = "SANDBOX_GATEWAY",
            AmountMinor = 10000, IdempotencyKey = "EF-REFUND-DUPLICATE"
        });
        await AssertDatabaseRejectsAsync(db, PostgresErrorCodes.UniqueViolation);
    }

    [PostgreSqlFact]
    public async Task Inspection_roundtrips_credential_replacement_paired_journey_and_entry_trip_context()
    {
        await using var db = await SeedAsync();
        var replacement = new TicketCredential
        {
            OperatorId = OperatorId, TicketId = TicketId, CredentialType = "QR",
            TokenHash = new string('b', 64), TokenLast4 = "5678"
        };
        var credential = new TicketCredential
        {
            OperatorId = OperatorId, TicketId = TicketId, CredentialType = "QR",
            TokenHash = Hash, Status = "REPLACED", ReplacedBy = replacement, RevokedAt = ScanAt.UtcDateTime
        };
        var childJourney = new Journey
        {
            OperatorId = OperatorId, JourneyCode = "EF-CHILD-JOURNEY", TicketId = ChildTicketId,
            ServiceDate = ServiceDate, EntryPlannedTripId = TripId, EntryDirection = 0, EnteredAt = ScanAt
        };
        var journey = new Journey
        {
            OperatorId = OperatorId, JourneyCode = "EF-JOURNEY", TicketId = TicketId, PairedJourney = childJourney,
            ActualEntryStationId = StationId, ServiceDate = ServiceDate, EntryPlannedTripId = TripId,
            EntryDirection = 0, EnteredAt = ScanAt
        };
        var scan = new AfcEvent
        {
            OperatorId = OperatorId, EventCode = "EF-SCAN", IdempotencyKey = "EF-SCAN", TicketId = TicketId,
            Journey = journey, Credential = replacement, EventType = AfcEventType.TapIn,
            StationId = StationId, DeviceCode = "EF-DEVICE", EventAt = ScanAt, Decision = AfcEventDecision.Allow,
            ReasonCode = "VALID", ActorUserId = "ef-inspector", PlannedTripId = TripId, Direction = 0
        };
        db.AddRange(credential, scan);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var stored = await db.AfcEvents.Include(e => e.Journey)!.ThenInclude(j => j!.PairedJourney).Include(e => e.Credential).SingleAsync();
        Assert.Equal(TripId, stored.PlannedTripId);
        Assert.Equal(ScanAt, stored.EventAt);
        Assert.Equal(ServiceDate, stored.Journey!.ServiceDate);
        Assert.Equal(ChildTicketId, stored.Journey.PairedJourney!.TicketId);
        Assert.Equal(replacement.Id, stored.Credential!.Id);
        var old = await db.TicketCredentials.Include(c => c.ReplacedBy).SingleAsync(c => c.TokenHash == Hash);
        Assert.Equal(replacement.Id, old.ReplacedBy!.Id);

        db.Journeys.Add(new Journey { OperatorId = OperatorId, TicketId = TicketId, JourneyCode = "EF-DUPLICATE-ENTRY" });
        await AssertDatabaseRejectsAsync(db, PostgresErrorCodes.UniqueViolation);
    }

    [PostgreSqlFact]
    public async Task Guardian_consent_and_suggestions_preserve_dates_version_and_current_suggestion_uniqueness()
    {
        await using var db = await SeedAsync();
        var guardian = new GuardianLink
        {
            OperatorId = OperatorId, GuardianUserId = "ef-owner", ChildUserId = "ef-child",
            Relationship = GuardianRelationship.Parent, Status = GuardianLinkStatus.Active,
            VerifiedByUserId = "ef-admin", VerifiedAt = ScanAt, ValidFrom = ServiceDate, ValidTo = ServiceDate.AddYears(1)
        };
        var consent = new PurchaseConsent
        {
            OperatorId = OperatorId, OrderId = OrderId, OrderVersion = 2, BuyerUserId = "ef-buyer",
            BeneficiaryUserId = "ef-owner", OrderContentHash = Hash, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        var suggestion = new TripSuggestion
        {
            OperatorId = OperatorId, OrderId = OrderId, OrderVersion = 2, SuggestionCode = "EF-SUGGESTION",
            BeneficiaryUserId = "ef-owner", ServiceDate = ServiceDate, OriginStationId = StationId,
            DestinationStationId = DestinationId, SuggestedPlannedTripId = TripId, SuggestedDepartureAt = ScanAt,
            ScheduleVersionId = ScheduleId, SourceVersionLabel = "EF-SCHEDULE", Direction = 0, DataFreshnessSeconds = 10
        };
        db.AddRange(guardian, consent, suggestion);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(ServiceDate.AddYears(1), (await db.GuardianLinks.SingleAsync()).ValidTo);
        Assert.Equal(2, (await db.PurchaseConsents.SingleAsync()).OrderVersion);
        var current = await db.TripSuggestions.SingleAsync();
        Assert.Equal(2, current.OrderVersion);
        Assert.Equal(TripId, current.SuggestedPlannedTripId);
        Assert.Null(current.SupersededAt);

        // A different order version can retain its own historical/current suggestion.
        db.TripSuggestions.Add(new TripSuggestion
        {
            OperatorId = OperatorId, OrderId = OrderId, OrderVersion = 1, SuggestionCode = "EF-OLD-VERSION",
            BeneficiaryUserId = "ef-owner", ServiceDate = ServiceDate, OriginStationId = StationId,
            DestinationStationId = DestinationId, Status = TripSuggestionStatus.NoTrip,
            Source = TripSuggestionSource.Manual, NoTripReason = "No service"
        });
        await db.SaveChangesAsync();
        var replacementSuggestion = new TripSuggestion
        {
            OperatorId = OperatorId, OrderId = OrderId, OrderVersion = 2, SuggestionCode = "EF-NEW-SUGGESTION",
            BeneficiaryUserId = "ef-owner", ServiceDate = ServiceDate, OriginStationId = StationId,
            DestinationStationId = DestinationId, Status = TripSuggestionStatus.NoTrip,
            Source = TripSuggestionSource.Manual, NoTripReason = "No service"
        };
        db.TripSuggestions.Add(replacementSuggestion);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var previous = await db.TripSuggestions.SingleAsync(s => s.Id == suggestion.Id);
        Assert.Equal(TripSuggestionStatus.Superseded, previous.Status);
        Assert.Equal(replacementSuggestion.ComputedAt, previous.SupersededAt);
        Assert.Equal(2, await db.TripSuggestions.CountAsync(s => s.SupersededAt == null));
        Assert.Equal(replacementSuggestion.Id,
            (await db.TripSuggestions.SingleAsync(s => s.OrderVersion == 2 && s.SupersededAt == null)).Id);

        // Reinstating the old row must not create two current suggestions for version 2.
        previous.SupersededAt = null;
        await AssertDatabaseRejectsAsync(db, PostgresErrorCodes.UniqueViolation);
    }

    [PostgreSqlFact]
    public async Task Ticket_change_history_keeps_related_child_and_allows_only_one_applied_change()
    {
        await using var db = await SeedAsync();
        TicketChangeRequest Change(string code) => new()
        {
            OperatorId = OperatorId, RequestCode = code, TicketId = TicketId, OrderId = OrderId,
            RequestedByUserId = "ef-owner", ReasonType = TicketChangeReasonType.ServiceDisruption,
            OriginalServiceDate = ServiceDate, RequestedServiceDate = ServiceDate.AddDays(1),
            NewOriginStationId = StationId, NewDestinationStationId = DestinationId, NewPlannedTripId = TripId,
            Status = TicketChangeRequestStatus.Applied, DecidedByUserId = "ef-inspector", DecidedAt = ScanAt,
            DecisionReason = "Published service disruption", AppliedAt = ScanAt, RelatedChildTicketId = ChildTicketId
        };
        db.TicketChangeRequests.Add(Change("EF-CHANGE"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var stored = await db.TicketChangeRequests.SingleAsync();
        Assert.Equal(ChildTicketId, stored.RelatedChildTicketId);
        Assert.Equal(ServiceDate.AddDays(1), stored.RequestedServiceDate);
        Assert.Equal((short)1, stored.ChangeSequence);
        db.TicketChangeRequests.Add(Change("EF-CHANGE-AGAIN"));
        await AssertDatabaseRejectsAsync(db, PostgresErrorCodes.UniqueViolation);
    }

    [PostgreSqlFact]
    public async Task Lost_property_roundtrips_private_details_claim_and_single_handover()
    {
        await using var db = await SeedAsync();
        var report = new LostReport
        {
            OperatorId = OperatorId, ReportCode = "EF-LOST", SubmittedByUserId = "ef-owner", OwnerUserId = "ef-child",
            ItemCategory = "BAG", Color = "Blue", PublicDescription = "A blue bag", SecretCharacteristics = "Demo secret",
            LostFrom = ScanAt, StationId = StationId, PlannedTripId = TripId, AttachmentReference = "test/lost.jpg"
        };
        var item = new FoundItem
        {
            OperatorId = OperatorId, ItemCode = "EF-FOUND", ReceivedByUserId = "ef-admin", HoldingStationId = StationId,
            PublicDescription = "A blue bag", SecretCharacteristics = "Demo secret", FoundAt = ScanAt
        };
        var claim = new LostItemClaim
        {
            OperatorId = OperatorId, ClaimCode = "EF-CLAIM", LostReport = report, FoundItem = item,
            ClaimantUserId = "ef-owner", Status = LostItemClaimStatus.Approved, DecidedByUserId = "ef-admin",
            DecidedAt = ScanAt, DecisionReason = "Verified demo evidence"
        };
        var handover = new LostItemHandover
        {
            OperatorId = OperatorId, FoundItem = item, Claim = claim, HandedToUserId = "ef-owner",
            HandedByUserId = "ef-admin", EvidenceReference = "test/handover", ConfirmationNote = "Received in person"
        };
        db.LostItemHandovers.Add(handover);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var stored = await db.LostItemHandovers.Include(h => h.Claim).ThenInclude(c => c.LostReport).Include(h => h.FoundItem).SingleAsync();
        Assert.Equal("Demo secret", stored.FoundItem.SecretCharacteristics);
        Assert.Equal("ef-child", stored.Claim.LostReport!.OwnerUserId);
        Assert.Equal(LostItemClaimStatus.Approved, stored.Claim.Status);
        Assert.NotEqual(default, stored.HandedAt);

        db.LostItemHandovers.Add(new LostItemHandover
        {
            OperatorId = OperatorId, FoundItemId = item.Id, ClaimId = claim.Id,
            HandedToUserId = "ef-owner", HandedByUserId = "ef-admin"
        });
        await AssertDatabaseRejectsAsync(db, PostgresErrorCodes.UniqueViolation);
    }
}
