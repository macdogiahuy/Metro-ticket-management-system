using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Metro.Infrastructure.Ticketing;

public sealed class TicketIssuanceWorker(IServiceScopeFactory scopes, IOptions<TicketQrOptions> options,
    ILogger<TicketIssuanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TicketIssuanceProcessor>().ProcessDueAsync(ct: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception)
            {
                // DB outages must not stop the host; no exception payload is logged.
                logger.LogWarning("Ticket issuance polling unavailable; the durable queue will be retried.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
