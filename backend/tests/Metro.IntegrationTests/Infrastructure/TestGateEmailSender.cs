using System.Collections.Concurrent;
using Metro.Application.Abstractions.Gates;

namespace Metro.IntegrationTests.Infrastructure;

// Test-only mailbox. Production never returns codes or exposes an in-memory mailbox endpoint.
public sealed class TestGateEmailSender:IGateEmailSender
{
    public ConcurrentDictionary<Guid,GateEmailMessage> Accepted { get; }=new();
    public ConcurrentDictionary<Guid,int> Calls { get; }=new();
    public ConcurrentDictionary<Guid,int> FailuresRemaining { get; }=new();
    public Task SendAsync(GateEmailMessage message,CancellationToken ct)
    {
        Calls.AddOrUpdate(message.DeliveryId,1,(_,count)=>count+1);
        if(FailuresRemaining.TryGetValue(message.DeliveryId,out var left) && left>0)
        {
            FailuresRemaining[message.DeliveryId]=left-1;
            throw new GateException("TEST_EMAIL_UNAVAILABLE",503);
        }
        Accepted[message.DeliveryId]=message;return Task.CompletedTask;
    }
}
