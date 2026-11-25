using System.Collections.Concurrent;
using System.Diagnostics;
using Metro.IntegrationTests.Infrastructure;

namespace Metro.IntegrationTests.Endpoints;

public sealed partial class GateEndpointsTests
{
    [PostgreSqlFact,Trait("Category","PostgreSQL"),Trait("Category","Performance")]
    public async Task QR_decision_local_smoke_has_P95_below_one_second_for_100_requests_at_20_concurrent_clients()
    {
        using var staff=await AccountAsync("StationStaff");var n=await NetworkAsync(staff,"DAY");
        var holders=new List<Account>();var tickets=new List<string>();
        try
        {
            for(var i=0;i<20;i++){var holder=await AccountAsync();holders.Add(holder);tickets.Add((await Issue(n,holder)).Qr);}
            Assert.Equal("Allow",(await Scan(staff,n,Entry(n,tickets[0]))).Decision);
            Assert.Equal("Allow",(await Scan(staff,n,Exit(n,tickets[0]))).Decision);
            var samples=new ConcurrentBag<double>();
            for(var round=0;round<5;round++)
                await Task.WhenAll(tickets.Select(async token=>
                {
                    var request=round%2==0?Entry(n,token):Exit(n,token);var watch=Stopwatch.StartNew();
                    var result=await Scan(staff,n,request);watch.Stop();Assert.Equal("Allow",result.Decision);samples.Add(watch.Elapsed.TotalMilliseconds);
                }));
            var sorted=samples.Order().ToArray();var p95=sorted[(int)Math.Ceiling(sorted.Length*.95)-1];
            output.WriteLine($"Local QR smoke: samples={sorted.Length}, concurrency=20, P95={p95:F2} ms, max={sorted[^1]:F2} ms. Includes TestServer HTTP/auth overhead; excludes camera and external network. This is not the full SRS reference-dataset benchmark.");
            Assert.Equal(100,sorted.Length);Assert.True(p95<=1000,$"NFR-002 local smoke P95 {p95:F2} ms exceeds 1000 ms.");
            await Task.WhenAll(tickets.Select(token=>Scan(staff,n,Exit(n,token))));
        }
        finally{foreach(var holder in holders)holder.Dispose();}
    }
}
