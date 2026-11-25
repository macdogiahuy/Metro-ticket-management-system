using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Metro.Application.Abstractions.Gates;
using Metro.Application.Ticketing;
using Metro.Domain.Entities.Identity;
using Metro.Infrastructure.Persistence.Context;
using Metro.Infrastructure.Ticketing;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Metro.IntegrationTests.Endpoints;

[Collection(DatabaseIntegrationCollection.Name)]
public sealed partial class GateEndpointsTests(MetroWebApplicationFactory factory,Xunit.Abstractions.ITestOutputHelper output)
{
    private static DateOnly Today => TicketRules.LocalDate(DateTimeOffset.UtcNow);
    private static async Task<object?> Sql(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var c = new NpgsqlConnection(PostgreSqlTestConfiguration.LoadRequired().ConnectionString); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value is DateTimeOffset instant ? instant.ToUniversalTime() : value ?? DBNull.Value);
        var v = await cmd.ExecuteScalarAsync(); return v is DBNull ? null : v;
    }
    private async Task<Account> AccountAsync(string role = "Passenger", DateOnly? birth = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var u = new IdentityUser { UserName = $"be10-{Guid.NewGuid():N}@example.invalid", EmailConfirmed = true };
        u.Email = u.UserName; var password = $"Metro!{Guid.NewGuid():N}Aa1";
        Assert.True((await users.CreateAsync(u, password)).Succeeded);
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(role)) Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
        Assert.True((await users.AddToRoleAsync(u, role)).Succeeded);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.UserProfiles.Add(new UserProfile { UserId = u.Id, FullName = "Gate Passenger", NormalizedFullName = "GATE PASSENGER", BirthDate = birth ?? new DateOnly(1990,1,1), Status = "ACTIVE" });
        await db.SaveChangesAsync();
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = u.Email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        return new(u.Id, client);
    }
    private async Task<Network> NetworkAsync(Account staff, string product = "SINGLE_TRIP")
    {
        var n = new Network(Guid.NewGuid(), "B10"+Guid.NewGuid().ToString("N")[..12], Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await Sql("""
            INSERT INTO metro.operators(id,code,name) VALUES(@op,@code,'BE10 fixture');
            INSERT INTO metro.operator_accounts(operator_id,user_id,account_type,mfa_required) VALUES(@op,@staff,'STAFF',true);
            INSERT INTO metro.stations(id,operator_id,code,name) VALUES(@a,@op,'A','Origin'),(@b,@op,'B','Destination'),(@c,@op,'C','Other');
            INSERT INTO metro.lines(id,operator_id,code,name) VALUES(@line,@op,'L','Line');
            INSERT INTO metro.line_stations(operator_id,line_id,station_id,direction,sequence_no,distance_from_origin_m)
              VALUES(@op,@line,@a,0,1,0),(@op,@line,@b,0,2,1000),(@op,@line,@c,0,3,2000);
            INSERT INTO metro.service_calendars(id,operator_id,code,name,day_type,effective_from,status)
              VALUES(@cal,@op,'CAL','Calendar','WEEKDAY',@day,'PUBLISHED');
            INSERT INTO metro.schedule_versions(id,operator_id,line_id,service_calendar_id,version,code,status,effective_from,created_by_user_id)
              VALUES(@schedule,@op,@line,@cal,1,'S','PUBLISHED',@start,@staff);
            INSERT INTO metro.capacity_profiles(id,operator_id,code,version,line_id,seated_capacity,standing_capacity,effective_from,created_by_user_id,max_load_kg)
              VALUES(@profile,@op,'KG',1,@line,100,100,@start,@staff,18000);
            INSERT INTO metro.planned_trips(id,operator_id,schedule_version_id,line_id,trip_code,service_date,direction,planned_start_at,planned_end_at,status,load_profile_id,boarding_station_id)
              VALUES(@trip,@op,@schedule,@line,'T',@day,0,@start,@end,'IN_SERVICE',@profile,@a);
            INSERT INTO metro.planned_stop_times(operator_id,planned_trip_id,station_id,sequence_no,planned_arrival_at,planned_departure_at)
              VALUES(@op,@trip,@a,1,@start,@start),(@op,@trip,@b,2,@end,@end),(@op,@trip,@c,3,@end,@end);
            INSERT INTO metro.actual_trips(id,operator_id,planned_trip_id,status,source) VALUES(@actual,@op,@trip,'IN_SERVICE','SIMULATION');
            INSERT INTO metro.actual_stop_times(operator_id,actual_trip_id,station_id,sequence_no,actual_arrival_at,source)
              VALUES(@op,@actual,@a,1,clock_timestamp()-interval '1 minute','SIMULATION');
            INSERT INTO metro.load_observations(operator_id,planned_trip_id,capacity_profile_id,observed_load_kg,recorded_by_user_id,observed_at)
              VALUES(@op,@trip,@profile,17999.9,@staff,clock_timestamp());
            INSERT INTO metro.ticket_products(id,operator_id,code,name,product_type,expiry_rule,duration_value,duration_unit)
              VALUES(@product,@op,'P','Gate product',@type,CASE WHEN @type='SINGLE_TRIP' THEN 'SINGLE_JOURNEY' ELSE 'NEXT_LOCAL_MIDNIGHT' END,
                CASE @type WHEN 'DAY' THEN 1 WHEN 'WEEK' THEN 7 WHEN 'MONTH' THEN 30 END,CASE WHEN @type='SINGLE_TRIP' THEN NULL ELSE 'DAY' END);
            INSERT INTO metro.fare_policy_versions(id,operator_id,code,version,scope_key,effective_from,created_by_user_id)
              VALUES(@fare,@op,'F',1,'F',@start,@staff);
            """, ("op",n.Op),("code",n.Code),("staff",staff.Id),("a",n.A),("b",n.B),("c",n.C),("line",n.Line),
            ("cal",Guid.NewGuid()),("schedule",n.Schedule),("profile",n.Profile),("trip",n.Trip),("actual",n.Actual),
            ("day",Today),("start",TicketRules.Midnight(Today)),("end",TicketRules.Midnight(Today.AddDays(1))),
            ("product",n.Product),("type",product),("fare",n.Fare));
        return n;
    }
    private async Task<(Guid Id, string Qr)> Issue(Network n, Account holder, Account? guardian = null, Guid? destination = null, object? snapshot = null)
    {
        var order = Guid.NewGuid(); var payment = Guid.NewGuid(); var free = guardian is not null;
        await Sql("""
            INSERT INTO metro.orders(id,operator_id,order_code,channel,payer_user_id,beneficiary_user_id,subtotal_minor,discount_minor,total_minor,fare_policy_version_id,service_date,fare_snapshot)
             VALUES(@id,@op,@code,'WEB',@buyer,@holder,10000,@discount,@total,@fare,@day,@snapshot::jsonb);
            INSERT INTO metro.order_items(operator_id,order_id,ticket_product_id,origin_station_id,destination_station_id,unit_price_minor,discount_minor,line_total_minor,fare_snapshot)
             VALUES(@op,@id,@product,@a,@b,10000,@discount,@total,@snapshot::jsonb);
            INSERT INTO metro.order_versions(operator_id,order_id,version,status,ticket_product_id,beneficiary_user_id,service_date,origin_station_id,destination_station_id,subtotal_minor,discount_minor,total_minor,fare_policy_version_id,confirmed_at,fare_snapshot)
             VALUES(@op,@id,1,'CONFIRMED',@product,@holder,@day,@a,@b,10000,@discount,@total,@fare,clock_timestamp(),@snapshot::jsonb);
            UPDATE metro.orders SET status='HELD' WHERE id=@id;
            """, ("id",order),("op",n.Op),("code",order.ToString()),("buyer",guardian?.Id ?? holder.Id),("holder",holder.Id),
            ("discount",free ? 10000L : 0L),("total",free ? 0L : 10000L),("fare",n.Fare),("day",Today),("product",n.Product),("a",n.A),("b",destination ?? n.B),
            ("snapshot",snapshot is null ? "{}" : JsonSerializer.Serialize(snapshot)));
        if (guardian is null)
        {
            await Sql("""
                INSERT INTO metro.payments(id,operator_id,order_id,payment_code,provider,method,amount_minor,idempotency_key)
                 VALUES(@pay,@op,@order,@code,'BE10_TEST','SANDBOX_GATEWAY',10000,@code);
                SELECT metro.confirm_payment_and_issue_tickets(@op,@pay,@code,@code,@hash,10000::bigint,'VND'::char(3),true)
                """, ("pay",payment),("op",n.Op),("order",order),("code",payment.ToString()),("hash",new string('a',64)));
        }
        else
            await Sql("SELECT metro.issue_free_order(@op,@order,@actor)",("op",n.Op),("order",order),("actor",guardian.Id));
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TicketIssuanceProcessor>().ProcessDueAsync(500,operatorId:n.Op);
        var id = (Guid)(await Sql("SELECT id FROM metro.tickets WHERE order_id=@id",("id",order)))!;
        var reference = (Guid)(await Sql("SELECT correlation_id FROM metro.tickets WHERE id=@id",("id",id)))!;
        var credential = (Guid)(await Sql("SELECT id FROM metro.ticket_credentials WHERE ticket_id=@id AND status='ACTIVE'",("id",id)))!;
        var codec = scope.ServiceProvider.GetRequiredService<TicketQrCodec>();
        return (id,codec.Token(n.Op,reference,credential,1,codec.ActiveKeyId));
    }
    private static string Url(Network n,string path) => $"/api/v1/gate/{path}?operatorCode={n.Code}";
    private static GateScanRequest Entry(Network n, params string[] tokens) => new(Guid.NewGuid(),"ENTRY",n.A,n.Trip,0,tokens,"BE10_BROWSER",tokens.Length==2);
    private static GateScanRequest Exit(Network n, params string[] tokens) => new(Guid.NewGuid(),"EXIT",n.B,null,null,tokens,"BE10_BROWSER",tokens.Length==2);
    private static async Task<GateResult> Scan(Account staff,Network n,GateScanRequest r)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,Url(n,"scans")){Content=JsonContent.Create(r)};
        request.Headers.Add("Idempotency-Key",r.RequestId.ToString());
        var response = await staff.Client.SendAsync(request);
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted,await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<GateResult>())!;
    }
    private static async Task<HttpResponseMessage> Resolve(Account staff,Network n,GateResolutionRequest r)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,Url(n,"resolutions")){Content=JsonContent.Create(r)};
        request.Headers.Add("Idempotency-Key",r.RequestId.ToString()); return await staff.Client.SendAsync(request);
    }
    private sealed record Network(Guid Op,string Code,Guid Product,Guid Fare,Guid A,Guid B,Guid C,Guid Line,Guid Schedule,Guid Profile,Guid Trip)
    { public Guid Actual { get; } = Guid.NewGuid(); }
    private sealed record Account(string Id,HttpClient Client):IDisposable { public void Dispose()=>Client.Dispose(); }
}
