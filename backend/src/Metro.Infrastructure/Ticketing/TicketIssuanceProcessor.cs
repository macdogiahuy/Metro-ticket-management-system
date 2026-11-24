using Metro.Application.Abstractions.Ticketing;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Metro.Infrastructure.Ticketing;

/// <summary>Completes SQL-created tickets. Never creates another paid ticket.</summary>
public sealed class TicketIssuanceProcessor(ApplicationDbContext db, TicketQrCodec codec)
{
    public async Task<int> ProcessDueAsync(int limit = 50, CancellationToken ct = default, Guid? operatorId = null)
    {
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString()); await connection.OpenAsync(ct);
        var jobs = new List<(Guid Id, Guid Operator, string Holder)>();
        var selectSql = operatorId.HasValue
            ? """
              SELECT j.ticket_id,j.operator_id,t.holder_user_id FROM metro.ticket_issuance_jobs j
              JOIN metro.tickets t ON t.operator_id=j.operator_id AND t.id=j.ticket_id
              WHERE j.status='PENDING' AND j.next_attempt_at<=clock_timestamp() AND j.operator_id=@op
              ORDER BY j.next_attempt_at,j.ticket_id LIMIT @limit
              """
            : """
              SELECT j.ticket_id,j.operator_id,t.holder_user_id FROM metro.ticket_issuance_jobs j
              JOIN metro.tickets t ON t.operator_id=j.operator_id AND t.id=j.ticket_id
              WHERE j.status='PENDING' AND j.next_attempt_at<=clock_timestamp()
              ORDER BY j.next_attempt_at,j.ticket_id LIMIT @limit
              """;
        var selectParams = operatorId.HasValue
            ? new (string Name, object Value)[] { ("limit", limit), ("op", operatorId.Value) }
            : new (string Name, object Value)[] { ("limit", limit) };
        await using (var cmd = TicketSql.Command(connection, null, selectSql, selectParams))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) jobs.Add((reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2)));
        var completed = 0;
        foreach (var job in jobs)
        {
            try { if (await CompleteAsync(connection, job.Id, job.Operator, job.Holder, ct)) completed++; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Store only a stable code, never a provider/SQL payload or QR material.
                var code = ex is TicketException e ? e.Code : "ISSUANCE_RETRY_REQUIRED";
                await using var fail = TicketSql.Command(connection, null, """
                    WITH escalated AS (UPDATE metro.ticket_issuance_jobs SET
                     first_failed_at=COALESCE(first_failed_at,clock_timestamp()),last_error_code=@code,
                     status=CASE WHEN clock_timestamp()>=COALESCE(first_failed_at,clock_timestamp())+interval '15 minutes'
                       THEN 'NEEDS_SUPPORT' ELSE 'PENDING' END,
                     next_attempt_at=clock_timestamp()+interval '1 minute'
                    WHERE operator_id=@op AND ticket_id=@id AND status='PENDING' RETURNING *)
                    INSERT INTO metro.notifications(operator_id,recipient_user_id,notification_type,channel,template_code,content_reference,mandatory,correlation_id)
                     SELECT j.operator_id,recipient.user_id,'TICKET_NEEDS_SUPPORT',channel.name,'BE09_NEEDS_SUPPORT',
                      jsonb_build_object('orderId',j.order_id,'ticketId',j.ticket_id),true,
                      md5(j.ticket_id::text||recipient.user_id||channel.name||'NEEDS_SUPPORT')::uuid
                     FROM escalated j JOIN metro.orders o ON o.operator_id=j.operator_id AND o.id=j.order_id
                     CROSS JOIN LATERAL (SELECT DISTINCT user_id FROM unnest(ARRAY[o.payer_user_id,o.beneficiary_user_id]) user_id WHERE user_id IS NOT NULL) recipient
                     CROSS JOIN (VALUES('IN_APP'),('EMAIL_MOCK')) channel(name)
                     WHERE j.status='NEEDS_SUPPORT' ON CONFLICT(operator_id,correlation_id) DO NOTHING
                    """, ("code", code), ("op", job.Operator), ("id", job.Id));
                await fail.ExecuteNonQueryAsync(ct);
            }
        }
        return completed;
    }
    private async Task<bool> CompleteAsync(NpgsqlConnection connection, Guid id, Guid op, string holder, CancellationToken ct)
    {
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using (var cmd = TicketSql.Command(connection, tx, "SELECT pg_advisory_xact_lock(hashtextextended(@op::text||':'||@holder,0))", ("op", op), ("holder", holder)))
            await cmd.ExecuteNonQueryAsync(ct);
        Guid order; Guid reference; bool free;
        await using (var cmd = TicketSql.Command(connection, tx, """
            SELECT t.order_id,t.correlation_id,o.free_confirmed_at IS NOT NULL FROM metro.tickets t
            JOIN metro.orders o ON o.operator_id=t.operator_id AND o.id=t.order_id
            JOIN metro.ticket_issuance_jobs j ON j.operator_id=t.operator_id AND j.ticket_id=t.id
            WHERE t.operator_id=@op AND t.id=@id AND j.status='PENDING' AND j.next_attempt_at<=clock_timestamp()
            FOR UPDATE OF o,j,t SKIP LOCKED
            """, ("op", op), ("id", id)))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return false;
            order = r.GetGuid(0); reference = r.GetGuid(1); free = r.GetBoolean(2);
        }
        await using (var cmd = TicketSql.Command(connection, tx, """
            UPDATE metro.ticket_issuance_jobs j SET status='CANCELLED',completed_at=clock_timestamp()
            FROM metro.tickets t,metro.orders o WHERE j.operator_id=@op AND j.ticket_id=@id
             AND t.operator_id=j.operator_id AND t.id=j.ticket_id AND o.operator_id=t.operator_id AND o.id=t.order_id
             AND (t.status NOT IN ('ISSUED','ACTIVE') OR o.status NOT IN ('PAID','PENDING_ISSUE','ISSUED'))
            """, ("op", op), ("id", id)))
            if (await cmd.ExecuteNonQueryAsync(ct) != 0) { await tx.CommitAsync(ct); return false; }
        await using (var cmd = TicketSql.Command(connection, tx, """
            SELECT expires_at>clock_timestamp() AND effective_from<=clock_timestamp() FROM metro.tickets WHERE operator_id=@op AND id=@id
            """, ("op", op), ("id", id)))
            if (await cmd.ExecuteScalarAsync(ct) is not true) throw new TicketException("TICKET_OUTSIDE_VALIDITY", 409);
        await using (var cmd = TicketSql.Command(connection, tx, "SELECT metro.check_issuance_order(@op,@order)", ("op", op), ("order", order)))
            await cmd.ExecuteScalarAsync(ct);
        if (free)
        {
            // Recheck approval and the *same* guardian ticket at the time QR is prepared.
            await using var cmd = TicketSql.Command(connection, tx, """
                SELECT EXISTS(SELECT 1 FROM metro.eligible_guardian_ticket(@op,@order) g
                 JOIN metro.tickets child ON child.operator_id=@op AND child.id=@id AND child.related_ticket_id=g.ticket_id AND child.guardian_link_id=g.link_id
                 JOIN metro.ticket_credentials c ON c.operator_id=@op AND c.ticket_id=g.ticket_id
                 JOIN metro.tickets parent ON parent.operator_id=@op AND parent.id=g.ticket_id
                 WHERE parent.status IN ('ACTIVE','IN_USE') AND parent.expires_at>clock_timestamp()
                  AND c.credential_type='QR' AND c.status='ACTIVE' AND c.key_id IS NOT NULL
                  AND EXISTS(SELECT 1 FROM metro.user_profiles profile WHERE profile.user_id=child.holder_user_id
                    AND profile.birth_date<=child.service_date AND (profile.birth_date+interval '6 years')::date>child.service_date))
                """, ("op", op), ("order", order), ("id", id));
            if (await cmd.ExecuteScalarAsync(ct) is not true) throw new TicketException("FREE_PAIR_NOT_READY", 409);
        }
        Guid credentialId; int version; string keyId; string? expected = null;
        await using (var cmd = TicketSql.Command(connection, tx, """
            SELECT id,version,key_id,token_hash FROM metro.ticket_credentials
            WHERE operator_id=@op AND ticket_id=@id AND credential_type='QR' AND status='ACTIVE' FOR UPDATE
            """, ("op", op), ("id", id)))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            if (await r.ReadAsync(ct))
            {
                if (r.IsDBNull(2)) throw new TicketException("LEGACY_QR_REQUIRES_SUPPORT", 409);
                credentialId = r.GetGuid(0); version = r.GetInt32(1); keyId = r.GetString(2); expected = r.GetString(3);
                if (await r.ReadAsync(ct)) throw new TicketException("MULTIPLE_CURRENT_QR", 409);
            }
            else { credentialId = Guid.NewGuid(); version = 0; keyId = codec.ActiveKeyId; }
        }
        if (version == 0)
        {
            await using var cmd = TicketSql.Command(connection, tx, "SELECT COALESCE(max(version),0)+1 FROM metro.ticket_credentials WHERE operator_id=@op AND ticket_id=@id AND credential_type='QR'", ("op", op), ("id", id));
            version = (int)(await cmd.ExecuteScalarAsync(ct))!;
        }
        var token = codec.Token(op, reference, credentialId, version, keyId);
        // Validate the renderer before committing READY. No I/O or public storage is involved.
        _ = TicketQrCodec.Png(token);
        if (expected is not null && !TicketQrCodec.Matches(token, expected)) throw new TicketException("QR_INTEGRITY_ERROR", 503);
        if (expected is null)
        {
            await using var cmd = TicketSql.Command(connection, tx, """
                INSERT INTO metro.ticket_credentials(id,operator_id,ticket_id,credential_type,token_hash,status,version,key_id)
                VALUES(@credential,@op,@id,'QR',@hash,'ACTIVE',@version,@key)
                """, ("credential", credentialId), ("op", op), ("id", id), ("hash", TicketQrCodec.Hash(token)), ("version", version), ("key", keyId));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await using (var cmd = TicketSql.Command(connection, tx, """
            UPDATE metro.tickets SET status='ACTIVE',updated_at=clock_timestamp() WHERE operator_id=@op AND id=@id AND status='ISSUED';
            UPDATE metro.ticket_issuance_jobs SET status='READY',completed_at=clock_timestamp(),last_error_code=NULL WHERE operator_id=@op AND ticket_id=@id;
            UPDATE metro.orders o SET status='ISSUED',issued_at=COALESCE(issued_at,clock_timestamp()),updated_at=clock_timestamp()
             WHERE o.operator_id=@op AND o.id=@order AND o.status IN ('PAID','PENDING_ISSUE')
              AND NOT EXISTS(SELECT 1 FROM metro.ticket_issuance_jobs j WHERE j.operator_id=@op AND j.order_id=o.id AND j.status<>'READY');
            INSERT INTO metro.audit_logs(operator_id,action,entity_type,entity_id,changed_fields,outcome)
             VALUES(@op,'PREPARE_TICKET_QR','TICKET',@id::text,ARRAY['credential','status'],'SUCCEEDED');
            INSERT INTO metro.notifications(operator_id,recipient_user_id,notification_type,channel,template_code,content_reference,mandatory,correlation_id)
             SELECT @op,recipient.user_id,'TICKET_ISSUED',channel.name,'BE09_TICKET_READY',
              jsonb_build_object('orderId',o.id,'ticketId',@id::uuid),true,
              md5(@id::text||recipient.user_id||channel.name||'READY')::uuid
             FROM metro.orders o
             CROSS JOIN LATERAL (SELECT DISTINCT user_id FROM unnest(ARRAY[o.payer_user_id,o.beneficiary_user_id]) user_id WHERE user_id IS NOT NULL) recipient
             CROSS JOIN (VALUES('IN_APP'),('EMAIL_MOCK')) channel(name)
             WHERE o.operator_id=@op AND o.id=@order ON CONFLICT(operator_id,correlation_id) DO NOTHING;
            """, ("op", op), ("id", id), ("order", order)))
            await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct); return true;
    }
}
