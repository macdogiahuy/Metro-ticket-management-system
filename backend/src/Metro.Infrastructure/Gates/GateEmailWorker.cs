using System.Text.Json;
using Metro.Application.Abstractions.Gates;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Metro.Infrastructure.Gates;

public sealed class GateEmailProcessor(ApplicationDbContext db,GateEmailProtection protection,IGateEmailSender sender)
{
    public async Task<int> ProcessDueAsync(Guid? operatorId=null,CancellationToken ct=default)
    {
        await using var c=new NpgsqlConnection(db.Database.GetConnectionString());await c.OpenAsync(ct);
        await using(var cleanup=new NpgsqlCommand("""
            UPDATE metro.gate_email_outbox o SET protected_payload=NULL,status=CASE WHEN c.expires_at<=clock_timestamp() THEN 'EXPIRED' ELSE 'CANCELLED' END
            FROM metro.email_verification_challenges c WHERE o.challenge_id=c.id AND o.protected_payload IS NOT NULL
              AND (c.expires_at<=clock_timestamp() OR c.consumed_at IS NOT NULL OR c.superseded_at IS NOT NULL OR c.failed_attempts>=5)
            """,c))await cleanup.ExecuteNonQueryAsync(ct);
        var count=0;
        for(var i=0;i<20;i++)
        {
            await using var tx=await c.BeginTransactionAsync(ct);
            var s=new GateStore(c,tx,Guid.Empty,"email-worker");
            var job=(await s.Rows<Job>("""
                SELECT id AS "Id",operator_id AS "Operator",kind AS "Kind",protected_payload AS "Payload",attempts AS "Attempts",notification_id AS "Notification",
                  expires_at AS "Expires",clock_timestamp() AS "ReadAt"
                FROM metro.gate_email_outbox WHERE status='QUEUED' AND protected_payload IS NOT NULL AND next_attempt_at<=clock_timestamp()
                 AND (expires_at IS NULL OR expires_at>clock_timestamp()) AND (@filter::uuid IS NULL OR operator_id=@filter::uuid)
                ORDER BY next_attempt_at,id LIMIT 1 FOR UPDATE SKIP LOCKED
                """,ct,("filter",operatorId))).SingleOrDefault();
            if(job is null){await tx.CommitAsync(ct);break;}
            await s.Execute("UPDATE metro.gate_email_outbox SET status='SENDING' WHERE id=@id",ct,("id",job.Id));
            string? error=null;
            try
            {
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
                if(job.Expires is { } expiry)
                {
                    var remaining=expiry-job.ReadAt;
                    if(remaining<=TimeSpan.Zero)deadline.Cancel();else deadline.CancelAfter(remaining);
                }
                deadline.Token.ThrowIfCancellationRequested();
                await sender.SendAsync(protection.Unprotect(job.Payload),deadline.Token);
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
            catch(Exception e){error=e is GateException ge?ge.Code:"EMAIL_DELIVERY_FAILED";}
            var attempt=job.Attempts+1;
            await s.Execute("""
                INSERT INTO metro.gate_email_attempts(delivery_id,attempt_no,status,error_code) VALUES(@id,@attempt,@result,@error::text);
                UPDATE metro.gate_email_outbox SET attempts=@attempt,status=CASE WHEN @error::text IS NULL THEN 'ACCEPTED'
                    WHEN @attempt<CASE WHEN kind='OTP' THEN 2 ELSE 3 END AND (expires_at IS NULL OR expires_at>clock_timestamp()) THEN 'QUEUED' ELSE 'FAILED' END,
                  protected_payload=CASE WHEN @error::text IS NULL OR @attempt>=CASE WHEN kind='OTP' THEN 2 ELSE 3 END OR expires_at<=clock_timestamp() THEN NULL ELSE protected_payload END,
                  next_attempt_at=created_at+CASE WHEN @attempt=1 THEN interval '1 minute' ELSE interval '5 minutes' END,
                  last_error_code=@error::text,accepted_at=CASE WHEN @error::text IS NULL THEN clock_timestamp() ELSE NULL END WHERE id=@id;
                UPDATE metro.notifications SET status=CASE WHEN @error::text IS NULL THEN 'SENT' ELSE 'FAILED' END,
                  attempts=@attempt,last_error=@error::text,sent_at=CASE WHEN @error::text IS NULL THEN clock_timestamp() ELSE NULL END
                  WHERE operator_id=@operator AND id=@notification::uuid
                """,ct,("id",job.Id),("attempt",attempt),("result",error is null?"ACCEPTED":"FAILED"),("error",error),("operator",job.Operator),("notification",job.Notification));
            await tx.CommitAsync(ct);count++;
        }
        return count;
    }
    private sealed record Job(Guid Id,Guid Operator,string Kind,string Payload,short Attempts,Guid? Notification,DateTimeOffset? Expires,DateTimeOffset ReadAt);
}
public sealed class GateEmailWorker(IServiceScopeFactory scopes,IOptions<GateEmailOptions> options,ILogger<GateEmailWorker> log):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!options.Value.WorkerEnabled)return;
        while(!stoppingToken.IsCancellationRequested)
        {
            try{await using var scope=scopes.CreateAsyncScope();await scope.ServiceProvider.GetRequiredService<GateEmailProcessor>().ProcessDueAsync(ct:stoppingToken);}
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception){log.LogWarning("Gate email queue unavailable; encrypted messages will be retried.");}
            try{await Task.Delay(TimeSpan.FromSeconds(1),stoppingToken);}catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
        }
    }
}
