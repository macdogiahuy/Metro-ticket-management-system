using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Metro.Application.Abstractions.Gates;
using Metro.IntegrationTests.Infrastructure;

namespace Metro.IntegrationTests.Endpoints;

public sealed partial class GateEndpointsTests
{
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Phone_lookup_returns_minimal_ticket_and_audits_without_exposing_the_number()
    {
        using var staff = await AccountAsync("StationStaff");
        using var holder = await AccountAsync();
        var n = await NetworkAsync(staff);
        var ticket = await Issue(n, holder);
        const string phone = "0912345678";
        await Sql("UPDATE public.\"AspNetUsers\" SET \"PhoneNumber\"=@phone WHERE \"Id\"=@id",
            ("phone", phone), ("id", holder.Id));

        var response = await staff.Client.PostAsJsonAsync(Url(n, "lookup"),
            new GateLookupRequest("", "Counter support", "+84 912 345 678"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(ticket.Id.ToString(), json.RootElement.GetProperty("ticketId").GetString());
        Assert.DoesNotContain(phone, body);
        Assert.DoesNotContain("912345678", body);
        Assert.DoesNotContain(phone, string.Join('\n', factory.TestLogs.Messages));

        var audits = await Sql("""
            SELECT count(*) FROM metro.audit_logs
            WHERE operator_id=@op AND action='GATE_LOOKUP' AND entity_id=@ticket AND actor_user_id=@actor
            """, ("op", n.Op), ("ticket", ticket.Id.ToString()), ("actor", staff.Id));
        Assert.Equal(1L, audits);
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Lookup_rejects_missing_ambiguous_or_malformed_identifiers()
    {
        using var staff = await AccountAsync("StationStaff");
        var n = await NetworkAsync(staff);
        var invalid = new[]
        {
            new GateLookupRequest("", "Counter support"),
            new GateLookupRequest("T-1", "Counter support", "0912345678"),
            new GateLookupRequest("", "Counter support", "12345"),
            new GateLookupRequest(new string('x', 61), "Counter support"),
            new GateLookupRequest("T-1", ""),
        };
        foreach (var request in invalid)
        {
            var response = await staff.Client.PostAsJsonAsync(Url(n, "lookup"), request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("INVALID_GATE_LOOKUP", problem.RootElement.GetProperty("code").GetString());
        }
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Verified_QR_replacement_writes_an_audit_record()
    {
        using var staff = await AccountAsync("StationStaff");
        using var holder = await AccountAsync();
        var n = await NetworkAsync(staff);
        var ticket = await Issue(n, holder);
        async Task<long> Count() => (long)(await Sql("""
            SELECT count(*) FROM metro.audit_logs
            WHERE operator_id=@op AND action='REPLACE_TICKET_QR' AND entity_id=@ticket
            """, ("op", n.Op), ("ticket", ticket.Id.ToString())))!;

        var before = await Count();
        var result = await QrReceipt(await QrWrite(staff, n, ticket.Id, DocumentProof("REPLACE"), false));
        Assert.Equal("QR_REPLACED", result.Code);
        Assert.Equal(before + 1, await Count());
    }
}
