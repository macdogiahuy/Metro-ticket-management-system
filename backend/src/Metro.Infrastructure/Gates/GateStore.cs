using System.Text.Json;
using Metro.Application.Abstractions.Gates;
using Metro.Infrastructure.Ticketing;
using Npgsql;

namespace Metro.Infrastructure.Gates;

internal sealed class GateStore(NpgsqlConnection connection, NpgsqlTransaction tx, Guid op, string actor)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal Guid Operator => op;
    internal string Actor => actor;
    internal DateTimeOffset Now { get; set; }
    internal NpgsqlCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        var cmd = TicketSql.Command(connection, tx, sql, ("op", op), ("actor", actor), ("now", Now));
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value is DateTimeOffset instant ? instant.ToUniversalTime() : value ?? DBNull.Value);
        return cmd;
    }
    internal async Task Execute(string sql, CancellationToken ct, params (string Name, object? Value)[] p)
    {
        await using var cmd = Command(sql, p); await cmd.ExecuteNonQueryAsync(ct);
    }
    internal async Task<object?> Scalar(string sql, CancellationToken ct, params (string Name, object? Value)[] p)
    {
        await using var cmd = Command(sql, p); var result = await cmd.ExecuteScalarAsync(ct);
        return result is DBNull ? null : result;
    }
    internal async Task<List<T>> Rows<T>(string sql, CancellationToken ct, params (string Name, object? Value)[] p)
    {
        await using var cmd = Command("SELECT row_to_json(q)::text FROM (" + sql + ") q", p);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<T>();
        while (await reader.ReadAsync(ct)) rows.Add(JsonSerializer.Deserialize<T>(reader.GetString(0), Json)!);
        return rows;
    }
    internal async Task<Receipt?> ReadReceipt(Guid id, CancellationToken ct) =>
        (await Rows<Receipt>("""
            SELECT actor_user_id AS "Actor",payload_hash AS "Hash",response::text AS "Response",kind AS "Kind"
            FROM metro.gate_requests WHERE operator_id=@op AND request_id=@id
            """, ct, ("id", id))).SingleOrDefault();

    internal async Task<bool> Staff(CancellationToken ct) => await Scalar("""
        SELECT EXISTS(SELECT 1 FROM public."AspNetUsers" u JOIN metro.operator_accounts m ON m.user_id=u."Id"
          JOIN metro.operators o ON o.id=m.operator_id
          WHERE u."Id"=@actor AND u."EmailConfirmed" AND m.operator_id=@op AND m.status='ACTIVE'
            AND m.account_type IN ('STAFF','ADMIN') AND o.status='ACTIVE'
            AND NOT EXISTS(SELECT 1 FROM metro.user_profiles p WHERE p.user_id=u."Id" AND p.status IN ('LOCKED','DISABLED','CLOSED'))
            AND EXISTS(SELECT 1 FROM public."AspNetUserRoles" ur JOIN public."AspNetRoles" r ON r."Id"=ur."RoleId"
                       WHERE ur."UserId"=u."Id" AND r."Name" IN ('StationStaff','Admin')))
        """, ct) is true;

    internal Task<List<TicketRow>> Tickets(Guid[] credentialIds, CancellationToken ct) => Rows<TicketRow>("""
        SELECT t.id AS "Id",t.correlation_id AS "Reference",t.holder_user_id AS "Holder",t.status AS "Status",
          p.product_type AS "Product",t.effective_from AS "Start",t.expires_at AS "End",t.first_used_at AS "FirstUsed",
          t.service_date AS "Day",t.origin_station_id AS "Origin",t.destination_station_id AS "Destination",
          t.related_ticket_id AS "Related",t.guardian_link_id AS "GuardianLink",t.guardian_user_id AS "Guardian",
          c.id AS "Credential",c.status AS "CredentialStatus",c.token_hash AS "TokenHash",c.key_id AS "KeyId",c.version AS "Version"
        FROM metro.ticket_credentials c JOIN metro.tickets t ON t.operator_id=c.operator_id AND t.id=c.ticket_id
          JOIN metro.ticket_products p ON p.operator_id=t.operator_id AND p.id=t.ticket_product_id
        WHERE t.operator_id=@op AND c.id=ANY(@ids) AND c.credential_type='QR'
        """, ct, ("ids", credentialIds));

    internal async Task LockHolders(IEnumerable<string> holders, CancellationToken ct)
    {
        foreach (var holder in holders.Distinct().Order(StringComparer.Ordinal))
        {
            // Profile/account writers use the exclusive form of key 4. Independent passengers may scan concurrently.
            await Execute("SELECT pg_advisory_xact_lock_shared(hashtextextended(@holder,4))", ct, ("holder", holder));
            await Execute("SELECT pg_advisory_xact_lock(hashtextextended(@op::text||':'||@holder,0))", ct, ("holder", holder));
        }
    }
    internal Task<List<JourneyRow>> Journeys(Guid[] tickets, CancellationToken ct) => Rows<JourneyRow>("""
        SELECT id AS "Id",ticket_id AS "Ticket",status AS "Status",actual_entry_station_id AS "EntryStation",
          actual_exit_station_id AS "ExitStation",entered_at AS "EnteredAt",exited_at AS "ExitedAt",
          entry_planned_trip_id AS "EntryTrip",entry_direction AS "Direction",paired_journey_id AS "Pair"
        FROM metro.journeys WHERE operator_id=@op AND ticket_id=ANY(@ids) AND status IN ('OPEN','MANUAL_REVIEW')
        """, ct, ("ids", tickets));

    internal async Task Save(Guid id, object request, GateResult result, string kind, CancellationToken ct)
    {
        await Execute("""
            INSERT INTO metro.gate_requests(operator_id,request_id,actor_user_id,payload_hash,kind,response,created_at)
            VALUES(@op,@id,@actor,@hash,@kind,@response::jsonb,@now)
            """, ct, ("id", id), ("hash", Hash(request)), ("kind", kind), ("response", JsonSerializer.Serialize(result, Json)));
        await Audit(id, kind, result.ReasonCode, ct);
    }
    internal Task Audit(Guid id, string action, string reason, CancellationToken ct) => Execute("""
        INSERT INTO metro.audit_logs(operator_id,actor_user_id,action,entity_type,entity_id,reason,correlation_id)
        VALUES(@op,@actor,@action,'AFC_EVENT',@id::text,@reason,@id)
        """, ct, ("id", id), ("action", "GATE_" + action), ("reason", reason));
    internal static string Hash(object value) => TicketQrCodec.Hash(JsonSerializer.Serialize(value, Json));
    internal sealed record Receipt(string Actor, string Hash, string Response, string Kind);
}

internal sealed record TicketRow(Guid Id, Guid Reference, string? Holder, string Status, string Product,
    DateTimeOffset? Start, DateTimeOffset? End, DateTimeOffset? FirstUsed, DateOnly? Day,
    Guid? Origin, Guid? Destination, Guid? Related, Guid? GuardianLink, string? Guardian,
    Guid Credential, string CredentialStatus, string TokenHash, string? KeyId, int Version);
internal sealed record JourneyRow(Guid Id, Guid Ticket, string Status, Guid? EntryStation, Guid? ExitStation,
    DateTimeOffset? EnteredAt, DateTimeOffset? ExitedAt, Guid? EntryTrip, short? Direction, Guid? Pair);
