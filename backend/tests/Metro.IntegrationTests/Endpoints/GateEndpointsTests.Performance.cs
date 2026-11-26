using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Metro.IntegrationTests.Infrastructure;
using Npgsql;

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
            var settings = new NpgsqlConnectionStringBuilder(PostgreSqlTestConfiguration.LoadRequired().ConnectionString);
            var loopback = string.Equals(settings.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || IPAddress.TryParse(settings.Host, out var address) && IPAddress.IsLoopback(address);
            output.WriteLine($"QR smoke: samples={sorted.Length}, concurrency=20, P95={p95:F2} ms, max={sorted[^1]:F2} ms; configured DB transport={(loopback ? "loopback" : "non-loopback")}. Includes TestServer HTTP/auth, database work, connection-pool waits and all application-to-database transport latency. Excludes camera and browser-to-API network. A loopback address may still be a tunnel/proxy. This is not the full SRS reference-dataset benchmark.");

            // Measure the same DB connection route after the timed scan rounds. This is diagnostic
            // evidence, not a correction factor: never subtract it from P95 or relax the threshold.
            await using (var probeConnection = new NpgsqlConnection(settings.ConnectionString))
            {
                await probeConnection.OpenAsync();
                await using var probe = new NpgsqlCommand("SELECT 1", probeConnection);
                var probeSamples = new List<double>();
                for (var i = 0; i < 5; i++)
                {
                    var watch = Stopwatch.StartNew();
                    Assert.Equal(1, await probe.ExecuteScalarAsync());
                    watch.Stop();
                    probeSamples.Add(watch.Elapsed.TotalMilliseconds);
                }
                probeSamples.Sort();
                output.WriteLine($"Sequential DB SELECT 1 probe: samples=5, min={probeSamples[0]:F2} ms, median={probeSamples[2]:F2} ms, max={probeSamples[^1]:F2} ms. Measured outside scan samples; does not isolate the cause of latency or establish SRS acceptance.");
            }
            Assert.Equal(100,sorted.Length);Assert.True(p95<=1000,$"QR smoke P95 {p95:F2} ms exceeds the unchanged 1000 ms threshold. Includes application-to-database transport latency; inspect the test diagnostics and environment before attributing the failure to network or gate logic. Full NFR-002 acceptance requires the SRS reference environment and dataset.");
            await Task.WhenAll(tickets.Select(token=>Scan(staff,n,Exit(n,token))));
        }
        finally{foreach(var holder in holders)holder.Dispose();}
    }
}
