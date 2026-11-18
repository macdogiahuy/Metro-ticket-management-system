using Metro.Domain.Common;
using Metro.Domain.Entities.Identity;
using Metro.Domain.Entities.Network;
using Metro.Domain.Entities.Orders;
using Metro.Domain.Entities.Tickets;
using Metro.Infrastructure.Persistence.Context;
using Metro.Infrastructure.Persistence.MultiTenancy;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Metro.IntegrationTests;

public class PostgresLiveIntegrationTests
{
    private static string ConnectionString()
    {
        return PostgreSqlTestConfiguration.LoadRequired().ConnectionString;
    }

    private static ApplicationDbContext Context(NpgsqlConnection connection, Guid? op) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options,
            new OperatorContext { CurrentOperatorId = op });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Core_Mappings_RoundTrip_Constraints_And_Operator_Isolation()
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(); // Configured connection/schema errors must fail, never silently pass.
        await using var transaction = await connection.BeginTransactionAsync();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await using var ca = Context(connection, a);
        await using var cb = Context(connection, b);
        await ca.Database.UseTransactionAsync(transaction);
        await cb.Database.UseTransactionAsync(transaction);
        // No EnsureCreated, migrations, fixture scripts, commit or destructive cleanup.
        var suffix = Guid.NewGuid().ToString("N");
        var now = new DateTime(2026, 11, 16, 1, 0, 0, DateTimeKind.Utc);
        var day = new DateOnly(2026, 11, 16);
        var user = new IdentityUser { Id = suffix, UserName = suffix, NormalizedUserName = suffix.ToUpperInvariant() };
        var bank = new Bank { Code = suffix, Name = "Synthetic bank", IsDemo = false };
        var role = new BusinessRole { Code = suffix, Name = "Synthetic role" };
        var permission = new Permission { Code = suffix, Description = "Synthetic permission" };
        ca.AddRange(user, bank, role, permission,
            new Operator { Id = a, Code = "A" + suffix, Name = "Synthetic A" },
            new Operator { Id = b, Code = "B" + suffix, Name = "Synthetic B" });
        await ca.SaveChangesAsync();
        var account = new OperatorAccount { OperatorId = a, UserId = user.Id };
        var line = new Line { OperatorId = a, Code = suffix, Name = "Synthetic line" };
        var station = new Station { OperatorId = a, Code = suffix, Name = "Synthetic station", MinTransferSeconds = 42, Latitude = 10.123456m };
        var calendar = new ServiceCalendar { OperatorId = a, Code = suffix, Name = "Synthetic calendar", EffectiveFrom = day, EffectiveTo = day, DaysOfWeek = [1, 2] };
        var product = new TicketProduct { OperatorId = a, Code = suffix, Name = "Synthetic day ticket", ProductType = "DAY", DurationValue = 1, DurationUnit = "DAY", ExpiryRule = "ELAPSED_DURATION" };
        var fare = new FarePolicyVersion { OperatorId = a, Code = suffix, ScopeKey = suffix, CreatedByUserId = user.Id, EffectiveFrom = now };
        ca.AddRange(account, line, station, calendar, product, fare,
            new UserProfile { UserId = user.Id, FullName = "Synthetic passenger", NormalizedFullName = "SYNTHETIC", BirthDate = new DateOnly(2000, 1, 1) },
            new AuthSession { UserId = user.Id, RefreshTokenHash = suffix + suffix, IssuedAt = now, ExpiresAt = now.AddHours(1) },
            new EmailVerificationChallenge { UserId = user.Id, Purpose = "VERIFY_EMAIL", CodeHash = suffix + suffix, CreatedAt = now, ExpiresAt = now.AddMinutes(10) },
            new RolePermission { RoleCode = role.Code, PermissionCode = permission.Code, Version = 2 });
        await ca.SaveChangesAsync();
        cb.Lines.Add(new Line { OperatorId = b, Code = suffix, Name = "Synthetic B line" });
        await cb.SaveChangesAsync();
        var schedule = new ScheduleVersion { OperatorId = a, LineId = line.Id, ServiceCalendarId = calendar.Id, Code = suffix, Version = 1, EffectiveFrom = now, CreatedByUserId = user.Id };
        var order = new Order { OperatorId = a, OrderCode = suffix, Channel = "ADMIN", FarePolicyVersionId = fare.Id, ServiceDate = day, FreeConfirmedAt = now, OrderVersion = 2, EligibilityGroup = "ORDINARY", AgeReferenceDate = day, SubtotalMinor = 100, TotalMinor = 100, FareSnapshot = "{\"amount\":100}" };
        ca.AddRange(schedule, order,
            new LineStation { OperatorId = a, LineId = line.Id, StationId = station.Id, SequenceNo = 1, TravelTimeSeconds = 77 },
            new AccountRoleAssignment { OperatorId = a, AccountId = account.Id, RoleCode = role.Code, Reason = "Synthetic mapping test", ValidFrom = now, ValidTo = now.AddHours(1) },
            new AuditLog { OperatorId = a, Action = "TEST", EntityType = "mapping", EntityId = suffix, ChangedFields = ["status"] });
        await ca.SaveChangesAsync();
        var trip = new PlannedTrip { OperatorId = a, ScheduleVersionId = schedule.Id, LineId = line.Id, TripCode = suffix, ServiceDate = day, PlannedStartAt = now, PlannedEndAt = now.AddHours(1), BoardingStationId = station.Id, BoardingStatus = "PAUSED" };
        var item = new OrderItem { OperatorId = a, OrderId = order.Id, TicketProductId = product.Id, UnitPriceMinor = 100, LineTotalMinor = 100, ProductSnapshot = "{\"type\":\"DAY\"}" };
        ca.AddRange(trip, item, new OrderVersion { OperatorId = a, OrderId = order.Id, Version = 2, TicketProductId = product.Id, ServiceDate = day, SubtotalMinor = 100, TotalMinor = 100, FarePolicyVersionId = fare.Id, FareSnapshot = "{\"version\":2}" });
        await ca.SaveChangesAsync();
        var actual = new ActualTrip { OperatorId = a, PlannedTripId = trip.Id, Status = "READY", Source = "MANUAL" };
        var ticket = new Ticket { OperatorId = a, TicketCode = suffix, OrderItemId = item.Id, TicketProductId = product.Id, FarePolicyVersionId = fare.Id, SequenceNo = 1, HolderUserId = user.Id, Status = "ACTIVE", EffectiveFrom = now, ExpiresAt = now.AddHours(1), OrderId = order.Id, OrderVersion = 2 };
        ca.AddRange(actual, ticket, new PlannedStopTime { OperatorId = a, PlannedTripId = trip.Id, StationId = station.Id, SequenceNo = 1, PlannedArrivalAt = now, PlannedDepartureAt = now.AddMinutes(1) });
        await ca.SaveChangesAsync();
        // Missing values must be available on the same tracked instance immediately after INSERT.
        var destination = new Station { OperatorId = a, Code = suffix + "-dst", Name = "Synthetic destination" };
        var singleProduct = new TicketProduct { OperatorId = a, Code = suffix + "-single", Name = "Synthetic single trip" };
        var singleOrder = new Order { OperatorId = a, OrderCode = suffix + "-single", Channel = "ADMIN", FarePolicyVersionId = fare.Id, ServiceDate = day, OrderVersion = 3, SubtotalMinor = 100, TotalMinor = 100 };
        ca.AddRange(destination, singleProduct, singleOrder);
        await ca.SaveChangesAsync();
        var singleItem = new OrderItem { OperatorId = a, OrderId = singleOrder.Id, TicketProductId = singleProduct.Id, PlannedTripId = trip.Id, OriginStationId = station.Id, DestinationStationId = destination.Id, UnitPriceMinor = 100, LineTotalMinor = 100 };
        ca.Add(singleItem);
        await ca.SaveChangesAsync();
        var generatedTicket = new Ticket { OperatorId = a, TicketCode = suffix + "-generated", OrderItemId = singleItem.Id, TicketProductId = singleProduct.Id, FarePolicyVersionId = fare.Id, SequenceNo = 1 };
        Assert.Null(generatedTicket.EffectiveFrom);
        Assert.Null(generatedTicket.ExpiresAt);
        ca.Add(generatedTicket);
        await ca.SaveChangesAsync();
        // Midnight in Asia/Ho_Chi_Minh is 17:00 UTC on the preceding date.
        var expectedStart = new DateTime(2026, 11, 15, 17, 0, 0, DateTimeKind.Utc);
        var expectedEnd = expectedStart.AddDays(1);
        Assert.Equal(expectedStart, generatedTicket.EffectiveFrom);
        Assert.Equal(expectedEnd, generatedTicket.ExpiresAt);
        Assert.Equal(singleOrder.Id, generatedTicket.OrderId);
        Assert.Equal(3, generatedTicket.OrderVersion);
        Assert.Equal(day, generatedTicket.ServiceDate);
        Assert.Equal(station.Id, generatedTicket.OriginStationId);
        Assert.Equal(destination.Id, generatedTicket.DestinationStationId);
        Assert.Equal(expectedEnd, ca.Entry(generatedTicket).Property(t => t.ExpiresAt).OriginalValue);
        Assert.True(generatedTicket.IsValidAt(expectedStart));
        Assert.False(generatedTicket.IsValidAt(expectedStart.AddTicks(-10)));
        Assert.False(generatedTicket.IsValidAt(expectedEnd));
        Assert.False(generatedTicket.IsValidAt(expectedEnd.AddDays(4)));
        var trackedTicket = await ca.Tickets.SingleAsync(t => t.Id == generatedTicket.Id);
        Assert.Same(generatedTicket, trackedTicket);
        Assert.False(trackedTicket.IsValidAt(expectedEnd.AddDays(4)));
        // Existing explicit bounds on the DAY ticket must still be preserved.
        Assert.Equal(now, ticket.EffectiveFrom);
        Assert.Equal(now.AddHours(1), ticket.ExpiresAt);
        var stop = new ActualStopTime { OperatorId = a, ActualTripId = actual.Id, StationId = station.Id, SequenceNo = 1, Source = "OCC_IMPORT", ActualArrivalAt = now, ActualDepartureAt = now.AddMinutes(2) };
        ca.AddRange(stop, new TicketCredential { OperatorId = a, TicketId = ticket.Id, CredentialType = "QR", TokenHash = suffix + suffix });
        await ca.SaveChangesAsync();
        ca.ChangeTracker.Clear();
        cb.ChangeTracker.Clear();
        Assert.Equal(a, (await ca.Lines.SingleAsync(x => x.Code == suffix)).OperatorId);
        Assert.Equal(b, (await cb.Lines.SingleAsync(x => x.Code == suffix)).OperatorId);
        await using var missing = Context(connection, null);
        await missing.Database.UseTransactionAsync(transaction);
        Assert.Empty(await missing.Lines.ToListAsync());
        Assert.Equal("OCC_IMPORT", (await ca.ActualStopTimes.SingleAsync(x => x.Id == stop.Id)).Source);
        Assert.Equal(42, (await ca.Stations.SingleAsync(x => x.Id == station.Id)).MinTransferSeconds);
        Assert.Equal(77, (await ca.LineStations.SingleAsync(x => x.LineId == line.Id && x.StationId == station.Id)).TravelTimeSeconds);
        var readTrip = await ca.PlannedTrips.SingleAsync(x => x.Id == trip.Id);
        Assert.Equal("PAUSED", readTrip.BoardingStatus);
        Assert.Equal(station.Id, readTrip.BoardingStationId);
        Assert.Null(readTrip.LoadProfileId); // No legacy entity is introduced to populate this optional reference.
        var readOrder = await ca.Orders.SingleAsync(x => x.Id == order.Id);
        Assert.Equal((day, now, 2, "ORDINARY", day), (readOrder.ServiceDate, readOrder.FreeConfirmedAt, readOrder.OrderVersion, readOrder.EligibilityGroup, readOrder.AgeReferenceDate));
        Assert.Contains("100", readOrder.FareSnapshot);
        Assert.Equal(day, (await ca.OrderVersions.SingleAsync(x => x.OrderId == order.Id)).ServiceDate);
        Assert.False((await ca.Tickets.SingleAsync(x => x.Id == ticket.Id)).IsValidAt(now.AddHours(1)));
        Assert.False((await ca.ServiceCalendars.SingleAsync(x => x.Id == calendar.Id)).GetEffectiveWindow().Contains(day));
        // Reload every mapped row inserted above, including arrays, JSON, Identity and dependent tables.
        Assert.Equal("Asia/Ho_Chi_Minh", (await ca.Operators.AsNoTracking().SingleAsync(x => x.Id == a)).Timezone);
        Assert.NotNull(await ca.Users.AsNoTracking().SingleAsync(x => x.Id == user.Id));
        Assert.False((await ca.Banks.AsNoTracking().SingleAsync(x => x.Code == suffix)).IsDemo);
        Assert.NotNull(await ca.UserProfiles.AsNoTracking().SingleAsync(x => x.UserId == user.Id));
        Assert.NotNull(await ca.AuthSessions.AsNoTracking().SingleAsync(x => x.UserId == user.Id));
        Assert.NotNull(await ca.EmailVerificationChallenges.AsNoTracking().SingleAsync(x => x.UserId == user.Id));
        Assert.NotNull(await ca.OperatorAccounts.AsNoTracking().SingleAsync(x => x.Id == account.Id));
        Assert.NotNull(await ca.BusinessRoles.AsNoTracking().SingleAsync(x => x.Code == suffix));
        Assert.NotNull(await ca.Permissions.AsNoTracking().SingleAsync(x => x.Code == suffix));
        Assert.Equal(2, (await ca.RolePermissions.AsNoTracking().SingleAsync(x => x.RoleCode == suffix)).Version);
        Assert.NotNull(await ca.AccountRoleAssignments.AsNoTracking().SingleAsync(x => x.AccountId == account.Id));
        Assert.Equal(new[] { "status" }, (await ca.AuditLogs.AsNoTracking().SingleAsync(x => x.EntityId == suffix)).ChangedFields);
        Assert.NotNull(await ca.ScheduleVersions.AsNoTracking().SingleAsync(x => x.Id == schedule.Id));
        Assert.NotNull(await ca.PlannedStopTimes.AsNoTracking().SingleAsync(x => x.PlannedTripId == trip.Id));
        Assert.NotNull(await ca.ActualTrips.AsNoTracking().SingleAsync(x => x.Id == actual.Id));
        Assert.NotNull(await ca.TicketProducts.AsNoTracking().SingleAsync(x => x.Id == product.Id));
        Assert.NotNull(await ca.FarePolicyVersions.AsNoTracking().SingleAsync(x => x.Id == fare.Id));
        Assert.NotNull(await ca.OrderItems.AsNoTracking().SingleAsync(x => x.Id == item.Id));
        Assert.NotNull(await ca.TicketCredentials.AsNoTracking().SingleAsync(x => x.TicketId == ticket.Id));
        await ExpectSqlState(connection, transaction, "23505", "INSERT INTO metro.lines SELECT * FROM metro.lines WHERE id = @id", line.Id);
        await ExpectSqlState(connection, transaction, "23505", "INSERT INTO metro.lines(operator_id,code,name) SELECT operator_id,code,name FROM metro.lines WHERE id = @id", line.Id);
        await ExpectSqlState(connection, transaction, "23503", "UPDATE metro.line_stations SET operator_id = @id WHERE line_id = @line", b, line.Id);
        await ExpectSqlState(connection, transaction, "23514", "UPDATE metro.actual_stop_times SET source = 'INVALID' WHERE id = @id", stop.Id);
        await ExpectSqlState(connection, transaction, "23P01", "INSERT INTO metro.tickets(operator_id,ticket_code,order_item_id,ticket_product_id,fare_policy_version_id,sequence_no,holder_user_id,status,effective_from,expires_at) SELECT operator_id,ticket_code || '-overlap',order_item_id,ticket_product_id,fare_policy_version_id,2,holder_user_id,status,effective_from,expires_at FROM metro.tickets WHERE id = @id", ticket.Id);
        var zeroInsert = new Station { OperatorId = a, Code = suffix + "-zero", Name = "Synthetic zero", MinTransferSeconds = 0 };
        ca.AddRange(zeroInsert, new LineStation { OperatorId = a, LineId = line.Id, Station = zeroInsert, SequenceNo = 2, TravelTimeSeconds = 0 });
        await ca.SaveChangesAsync();
        ca.ChangeTracker.Clear();
        Assert.Equal(0, (await ca.Stations.SingleAsync(x => x.Id == zeroInsert.Id)).MinTransferSeconds);
        Assert.Equal(0, (await ca.LineStations.SingleAsync(x => x.StationId == zeroInsert.Id)).TravelTimeSeconds);
        // Both integer columns permit zero; EF must not substitute their non-zero DB defaults.
        var zeroStation = await ca.Stations.SingleAsync(x => x.Id == station.Id);
        zeroStation.MinTransferSeconds = 0;
        var zeroTravel = await ca.LineStations.SingleAsync(x => x.LineId == line.Id && x.StationId == station.Id);
        zeroTravel.TravelTimeSeconds = 0;
        await ca.SaveChangesAsync();
        ca.ChangeTracker.Clear();
        Assert.Equal(0, (await ca.Stations.SingleAsync(x => x.Id == station.Id)).MinTransferSeconds);
        Assert.Equal(0, (await ca.LineStations.SingleAsync(x => x.LineId == line.Id && x.StationId == station.Id)).TravelTimeSeconds);
        // Forging the local operator must not update/delete another operator's row by PK.
        var foreignLine = await cb.Lines.SingleAsync(x => x.Code == suffix);
        ca.ChangeTracker.Clear();
        var forged = new Line { Id = foreignLine.Id, OperatorId = a, Code = suffix, Name = "Forged" };
        ca.Attach(forged);
        ca.Entry(forged).Property(x => x.Name).IsModified = true;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ca.SaveChangesAsync());
        ca.ChangeTracker.Clear();
        ca.Attach(forged);
        ca.Remove(forged);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ca.SaveChangesAsync());
        ca.ChangeTracker.Clear();
        Assert.Equal("Synthetic B line", (await cb.Lines.AsNoTracking().SingleAsync(x => x.Id == foreignLine.Id)).Name);
        await transaction.RollbackAsync();
    }

    private static async Task ExpectSqlState(NpgsqlConnection connection, NpgsqlTransaction transaction, string state, string sql, Guid id, Guid? line = null)
    {
        await transaction.SaveAsync("constraint_probe");
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        if (line.HasValue) command.Parameters.AddWithValue("line", line.Value);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        await transaction.RollbackAsync("constraint_probe");
        Assert.Equal(state, error.SqlState);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task EndExclusive_Matches_Postgres_For_Null_Bounds_And_Abutting_Ranges()
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var start = new DateTime(2026, 11, 16, 1, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(1);
        foreach (var lower in new DateTime?[] { null, start })
        foreach (var upper in new DateTime?[] { null, end })
        foreach (var point in new[] { start.AddTicks(-10), start, end.AddTicks(-10), end })
        {
            await using var command = new NpgsqlCommand("SELECT tstzrange(@lower,@upper,'[)') @> @point, tstzrange(@lower,@upper,'[)') && tstzrange(@end,@after,'[)')", connection);
            command.Parameters.Add(new NpgsqlParameter("lower", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = (object?)lower ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("upper", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = (object?)upper ?? DBNull.Value });
            command.Parameters.AddWithValue("point", point);
            command.Parameters.AddWithValue("end", end);
            command.Parameters.AddWithValue("after", end.AddHours(1));
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var window = new TimeWindow(lower, upper);
            Assert.Equal(reader.GetBoolean(0), window.Contains(point));
            Assert.Equal(reader.GetBoolean(1), window.Overlaps(new TimeWindow(end, end.AddHours(1))));
        }
        await using var empty = new NpgsqlCommand("SELECT daterange(DATE '2026-11-16',DATE '2026-11-16','[)') && daterange(NULL,NULL,'[)'), daterange(DATE '2026-11-16',DATE '2026-11-16','[)') @> DATE '2026-11-16'", connection);
        await using var result = await empty.ExecuteReaderAsync();
        Assert.True(await result.ReadAsync());
        var date = new DateOnly(2026, 11, 16);
        Assert.Equal(result.GetBoolean(0), new DateWindow(date, date).Overlaps(new DateWindow(date.AddDays(-1))));
        Assert.Equal(result.GetBoolean(1), new DateWindow(date, date).Contains(date));
    }
}
