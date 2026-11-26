using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metro.Application.Abstractions.Booking;
using Metro.Application.Abstractions.Catalog;
using Metro.Application.Abstractions.Operations;
using Metro.Application.Ticketing;
using Metro.Infrastructure.Booking;
using Metro.Infrastructure.Persistence.Context;
using Metro.IntegrationTests.Infrastructure;
using Metro.IntegrationTests.Payments;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;

namespace Metro.IntegrationTests.Endpoints;

[Collection(PaymentCollection.Name)]
public sealed class OperationsEndpointsTests(PaymentTestFactory factory)
{
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Every_admin_route_requires_auth_and_operator_membership_while_load_is_public()
    {
        var seed = await SeedAsync();
        using var guest = factory.CreateClient();
        using var passenger = await ClientAsync(seed.Buyer);
        using var staff = await ActorAsync(seed, "StationStaff", false);
        using var outsider = await ActorAsync(seed, "Admin", false);
        var trip = $"/api/v1/admin/trips/{seed.First}";
        var targets = new (HttpMethod Method, string Path, object? Body)[] {
            (HttpMethod.Post, trip+"/eta", new UpdateEtaRequest(seed.Origin, null, seed.Start, "ETA")),
            (HttpMethod.Post, trip+"/load", new RecordLoadRequest(0, seed.Start, "Load")),
            (HttpMethod.Put, trip+"/status", new UpdateTripStateRequest("DELAYED", "Delay")),
            (HttpMethod.Get, trip+"/operations", null),
            (HttpMethod.Get, "/api/v1/admin/disruptions", null),
            (HttpMethod.Post, "/api/v1/admin/disruptions", new CreateDisruptionRequest("Delay", "Description", "DELAYED", DateTimeOffset.UtcNow, LineId:seed.Line)),
            (HttpMethod.Post, $"/api/v1/admin/disruptions/{Guid.NewGuid()}/publish", new DisruptionDecisionRequest(true,"Verified")),
            (HttpMethod.Post, $"/api/v1/admin/disruptions/{Guid.NewGuid()}/resolve", new DisruptionDecisionRequest(true,"Recovered")) };
        foreach (var target in targets)
        {
            var url = target.Path + "?operatorCode=" + seed.Code;
            await Expect(await Send(guest, target.Method, url, target.Body), HttpStatusCode.Unauthorized);
            await Expect(await Send(passenger, target.Method, url, target.Body), HttpStatusCode.Forbidden);
            await Expect(await Send(staff, target.Method, url, target.Body), HttpStatusCode.Forbidden);
            await Expect(await Send(outsider, target.Method, url, target.Body), HttpStatusCode.Forbidden);
        }
        var load = await guest.GetFromJsonAsync<TripLoadResponse>(LoadUrl(seed));
        Assert.Equal("NO_DATA", load!.LoadStatus);
        Assert.False(load.EntryLoadAllowed);
        await Expect(await guest.GetAsync($"/api/v1/trips/{Guid.NewGuid()}/load?operatorCode={seed.Code}"), HttpStatusCode.NotFound);
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        Assert.Contains("/api/v1/trips/{id}/load", swagger.Paths.Keys);
        Assert.All(swagger.Paths["/api/v1/admin/trips/{id}/eta"].Operations.Values, operation => Assert.NotEmpty(operation.Security));
        var etaOperation=swagger.Paths["/api/v1/admin/trips/{id}/eta"].Operations[OperationType.Post];
        Assert.Contains(etaOperation.Parameters,p=>p.Name=="Idempotency-Key" && p.Required && p.Schema.MinLength==8 && p.Schema.MaxLength==120);
        Assert.All(etaOperation.RequestBody.Content.Values,media=>Assert.NotNull(media.Example));
        Assert.Equal("UpdateTripOperationalState", swagger.Paths["/api/v1/admin/trips/{id}/status"].Operations[OperationType.Put].OperationId);
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task ETA_and_load_are_atomic_idempotent_histories_with_strict_validation()
    {
        var seed = await SeedAsync();
        using var admin = await ActorAsync(seed, "Admin");
        var etaUrl = AdminTrip(seed, "eta");
        var eta = new UpdateEtaRequest(seed.Origin, seed.Start.AddMinutes(-1), seed.Start, "Manual verified ETA");
        var key = Guid.NewGuid().ToString();
        var concurrent = await Task.WhenAll(Send(admin,HttpMethod.Post,etaUrl,eta,key), Send(admin,HttpMethod.Post,etaUrl,eta,key));
        var receipt = await Receipt(concurrent[0]);
        Assert.Equal(receipt, await Receipt(concurrent[1]));
        await Expect(await Send(admin,HttpMethod.Post,etaUrl,eta with { ExpectedDepartureAt=seed.Start.AddMinutes(1) },key),HttpStatusCode.Conflict);
        await Expect(await admin.PostAsJsonAsync(etaUrl,eta),HttpStatusCode.BadRequest);
        await Expect(await Send(admin,HttpMethod.Post,etaUrl,eta with { StationId=Guid.NewGuid() }),HttpStatusCode.NotFound);
        await Expect(await Send(admin,HttpMethod.Post,etaUrl,eta with { ExpectedArrivalAt=null,ExpectedDepartureAt=seed.Start.AddDays(1) }),HttpStatusCode.UnprocessableEntity);
        await Expect(await Send(admin,HttpMethod.Post,etaUrl,eta with { Reason=" " }),HttpStatusCode.BadRequest);
        var observed = DateTimeOffset.UtcNow;
        await Sql($"UPDATE metro.planned_trips SET service_date={TicketRules.LocalDate(observed)} WHERE id={seed.First}");
        var input = new RecordLoadRequest(18_000.1m, observed, "Overload is recorded, not rejected");
        var loadUrl = AdminTrip(seed,"load");
        var loadKey = Guid.NewGuid().ToString();
        await Receipt(await Send(admin,HttpMethod.Post,loadUrl,input,loadKey));
        await Receipt(await Send(admin,HttpMethod.Post,loadUrl,input,loadKey));
        await Expect(await Send(admin,HttpMethod.Post,loadUrl,input with { ObservedLoadKg=1.01m }),HttpStatusCode.BadRequest);
        await Expect(await Send(admin,HttpMethod.Post,loadUrl,input with { ObservedAt=observed.AddDays(2) }),HttpStatusCode.UnprocessableEntity);
        var load = await admin.GetFromJsonAsync<TripLoadResponse>(LoadUrl(seed));
        Assert.Equal("FULL",load!.LoadStatus);
        Assert.Equal(18_000.1m,load.ObservedLoadKg);
        Assert.Equal(18_000m,load.MaxLoadKg);
        Assert.False(load.EntryLoadAllowed);
        Assert.False(load.HoldsCapacity);
        var history = await admin.GetFromJsonAsync<TripOperationsHistory>(AdminTrip(seed,"operations"));
        Assert.Single(history!.EtaUpdates);
        Assert.Single(history.LoadObservations);
        await Expect(await admin.GetAsync(AdminTrip(seed,"operations")+"&page=0"),HttpStatusCode.BadRequest);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2,await db.AuditLogs.IgnoreQueryFilters().CountAsync(a=>a.OperatorId==seed.Op && a.EntityType=="SERVICE_OPERATIONS"));
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Publishing_and_recovery_update_cached_routes_notices_notifications_and_keep_paid_rights()
    {
        var seed = await SeedAsync();
        using var admin = await ActorAsync(seed,"Admin");
        using var buyer = await ClientAsync(seed.Buyer);
        var order = await PaidOrderAsync(seed,buyer);
        await RefreshAsync();
        var original = await buyer.GetFromJsonAsync<TripSuggestionResponse>($"/api/v1/orders/{order.Id}/suggestion");
        Assert.Equal(seed.First,original!.PlannedTripId);
        var journeyUrl=$"/api/v1/journeys?operatorCode={seed.Code}&originStationId={seed.Origin}&destinationStationId={seed.Destination}";
        var statusUrl=$"/api/v1/service-status?operatorCode={seed.Code}";
        await Expect(await buyer.GetAsync(journeyUrl),HttpStatusCode.OK);
        Assert.Empty((await buyer.GetFromJsonAsync<ServiceStatusResponse>(statusUrl))!.Notices);
        var create=new CreateDisruptionRequest("Intermediate station closed","Verified service interruption","CLOSED",DateTimeOffset.UtcNow,StationId:seed.Middle);
        var draft=await Receipt(await Send(admin,HttpMethod.Post,$"/api/v1/admin/disruptions?operatorCode={seed.Code}",create));
        Assert.Equal("DRAFT",draft.Status);
        Assert.Empty((await buyer.GetFromJsonAsync<ServiceStatusResponse>(statusUrl))!.Notices);
        await Expect(await buyer.GetAsync(journeyUrl),HttpStatusCode.OK);
        var publish=$"/api/v1/admin/disruptions/{draft.Id}/publish?operatorCode={seed.Code}";
        await Expect(await Send(admin,HttpMethod.Post,publish,new DisruptionDecisionRequest(false,"Unchecked")),HttpStatusCode.BadRequest);
        var key=Guid.NewGuid().ToString();
        var decision=new DisruptionDecisionRequest(true,"Verified by administrator");
        var published=await Receipt(await Send(admin,HttpMethod.Post,publish,decision,key));
        Assert.Equal(published,await Receipt(await Send(admin,HttpMethod.Post,publish,decision,key)));
        await Expect(await buyer.GetAsync(journeyUrl),HttpStatusCode.UnprocessableEntity);
        var status=(await buyer.GetFromJsonAsync<ServiceStatusResponse>(statusUrl))!;
        Assert.Single(status.Notices);
        Assert.Equal("CLOSED",status.Stations.Single(s=>s.Id==seed.Middle).Status);
        await RefreshAsync();
        Assert.Equal("NO_TRIP",(await buyer.GetFromJsonAsync<TripSuggestionResponse>($"/api/v1/orders/{order.Id}/suggestion"))!.Status);
        await using(var scope=factory.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(2,await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM metro.notifications WHERE operator_id={seed.Op} AND notification_type='SERVICE_DISRUPTION_PUBLISHED'").SingleAsync());
            var paid=await db.Orders.IgnoreQueryFilters().SingleAsync(o=>o.Id==order.Id);
            Assert.NotNull(paid.PaidAt);
            Assert.Equal(6_000,paid.TotalMinor);
        }
        var resolve=$"/api/v1/admin/disruptions/{draft.Id}/resolve?operatorCode={seed.Code}";
        // Publication/recovery can share the same clock tick; expiring a notice must not violate its time-window check.
        using(var sameTickFactory=factory.WithWebHostBuilder(builder=>builder.ConfigureTestServices(services=>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTime(published.RecordedAt));
        })))
        using(var sameTickClient=sameTickFactory.CreateClient())
        {
            sameTickClient.DefaultRequestHeaders.Authorization=admin.DefaultRequestHeaders.Authorization;
            await Receipt(await Send(sameTickClient,HttpMethod.Post,resolve,new DisruptionDecisionRequest(true,"Service restored")));
        }
        await Expect(await buyer.GetAsync(journeyUrl),HttpStatusCode.OK);
        Assert.Empty((await buyer.GetFromJsonAsync<ServiceStatusResponse>(statusUrl))!.Notices);
        await RefreshAsync();
        Assert.Equal(seed.First,(await buyer.GetFromJsonAsync<TripSuggestionResponse>($"/api/v1/orders/{order.Id}/suggestion"))!.PlannedTripId);
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task ETA_cancellation_and_missing_fallback_recompute_one_current_suggestion_without_touching_money()
    {
        var seed=await SeedAsync();
        using var admin=await ActorAsync(seed,"Admin");
        using var buyer=await ClientAsync(seed.Buyer);
        var order=await PaidOrderAsync(seed,buyer);
        var suggestionUrl=$"/api/v1/orders/{order.Id}/suggestion";
        await RefreshAsync();
        Assert.Equal(seed.First,(await buyer.GetFromJsonAsync<TripSuggestionResponse>(suggestionUrl))!.PlannedTripId);
        await Receipt(await Send(admin,HttpMethod.Post,AdminTrip(seed,"eta"),new UpdateEtaRequest(seed.Origin,null,seed.Start.AddHours(1),"Late train")));
        await RefreshAsync();
        Assert.Equal(seed.Next,(await buyer.GetFromJsonAsync<TripSuggestionResponse>(suggestionUrl))!.PlannedTripId);
        await Receipt(await Send(admin,HttpMethod.Put,AdminTrip(seed,"status",seed.Next),new UpdateTripStateRequest("CANCELLED","Cancelled")));
        await RefreshAsync();
        Assert.Equal(seed.First,(await buyer.GetFromJsonAsync<TripSuggestionResponse>(suggestionUrl))!.PlannedTripId);
        await Receipt(await Send(admin,HttpMethod.Put,AdminTrip(seed,"status"),new UpdateTripStateRequest("CANCELLED","Cancelled")));
        await RefreshAsync(); await RefreshAsync();
        Assert.Equal("NO_TRIP",(await buyer.GetFromJsonAsync<TripSuggestionResponse>(suggestionUrl))!.Status);
        await Expect(await Send(admin,HttpMethod.Put,AdminTrip(seed,"status"),new UpdateTripStateRequest("READY","Resurrect")),HttpStatusCode.Conflict);
        await using var scope=factory.Services.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1,await db.TripSuggestions.IgnoreQueryFilters().CountAsync(s=>s.OrderId==order.Id && s.SupersededAt==null));
        Assert.Equal(4,await db.TripSuggestions.IgnoreQueryFilters().CountAsync(s=>s.OrderId==order.Id));
        Assert.Equal(1,await db.Payments.IgnoreQueryFilters().CountAsync(p=>p.OrderId==order.Id && p.SucceededAt!=null));
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Latest_kg_controls_fallback_but_missing_data_never_grants_gate_permission_and_used_tickets_freeze_suggestions()
    {
        var seed=await SeedAsync();
        using var admin=await ActorAsync(seed,"Admin");
        using var buyer=await ClientAsync(seed.Buyer);
        var order=await PaidOrderAsync(seed,buyer);
        using var currentFactory=factory.WithWebHostBuilder(builder=>builder.ConfigureTestServices(services=>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTime(seed.Start.AddMinutes(-1)));
        }));
        using var clockedAdmin=currentFactory.CreateClient();
        clockedAdmin.DefaultRequestHeaders.Authorization=admin.DefaultRequestHeaders.Authorization;
        async Task Record(decimal kg,DateTimeOffset observed) => await Receipt(await Send(clockedAdmin,HttpMethod.Post,
            AdminTrip(seed,"load"),new RecordLoadRequest(kg,observed,"Manual kg")));
        var when=seed.Start.AddMinutes(-2);
        await Record(17_999.9m,when);
        await RefreshAsync(currentFactory.Services);
        var url=$"/api/v1/orders/{order.Id}/suggestion";
        Assert.Equal(seed.First,(await buyer.GetFromJsonAsync<TripSuggestionResponse>(url))!.PlannedTripId);
        await Record(18_000m,when.AddSeconds(1));
        await RefreshAsync(currentFactory.Services);
        Assert.Equal(seed.Next,(await buyer.GetFromJsonAsync<TripSuggestionResponse>(url))!.PlannedTripId);
        await Record(0,when.AddMinutes(-5));
        Assert.Equal("FULL",(await buyer.GetFromJsonAsync<TripLoadResponse>(LoadUrl(seed)))!.LoadStatus);
        Assert.False((await buyer.GetFromJsonAsync<TripLoadResponse>($"/api/v1/trips/{seed.Next}/load?operatorCode={seed.Code}"))!.EntryLoadAllowed);
        await Sql($"UPDATE metro.tickets SET first_used_at=clock_timestamp() WHERE operator_id={seed.Op} AND order_id={order.Id}");
        await Receipt(await Send(admin,HttpMethod.Put,AdminTrip(seed,"status",seed.Next),new UpdateTripStateRequest("CANCELLED","After entry")));
        await RefreshAsync(currentFactory.Services);
        Assert.Equal(seed.Next,(await buyer.GetFromJsonAsync<TripSuggestionResponse>(url))!.PlannedTripId);
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Published_delay_requires_real_ETA_and_trip_recovery_never_resurrects_later_cancellation()
    {
        var seed=await SeedAsync();
        using var admin=await ActorAsync(seed,"Admin");
        using var buyer=await ClientAsync(seed.Buyer);
        var order=await PaidOrderAsync(seed,buyer);
        var draft=await Receipt(await Send(admin,HttpMethod.Post,$"/api/v1/admin/disruptions?operatorCode={seed.Code}",
            new CreateDisruptionRequest("Delay","Confirmed line delay","DELAYED",DateTimeOffset.UtcNow,LineId:seed.Line)));
        await Receipt(await Send(admin,HttpMethod.Post,$"/api/v1/admin/disruptions/{draft.Id}/publish?operatorCode={seed.Code}",new DisruptionDecisionRequest(true,"Publish")));
        var status=await buyer.GetFromJsonAsync<ServiceStatusResponse>($"/api/v1/service-status?operatorCode={seed.Code}");
        Assert.Equal("DELAYED",status!.Lines.Single().OperationalState);
        await RefreshAsync();
        Assert.Equal("NO_TRIP",(await buyer.GetFromJsonAsync<TripSuggestionResponse>($"/api/v1/orders/{order.Id}/suggestion"))!.Status);
        await Receipt(await Send(admin,HttpMethod.Post,AdminTrip(seed,"eta"),new UpdateEtaRequest(seed.Origin,null,seed.Start.AddMinutes(5),"Verified ETA")));
        await RefreshAsync();
        Assert.Equal(seed.First,(await buyer.GetFromJsonAsync<TripSuggestionResponse>($"/api/v1/orders/{order.Id}/suggestion"))!.PlannedTripId);
        var tripDraft=await Receipt(await Send(admin,HttpMethod.Post,$"/api/v1/admin/disruptions?operatorCode={seed.Code}",
            new CreateDisruptionRequest("Trip blocked","Confirmed trip interruption","SUSPENDED",DateTimeOffset.UtcNow,PlannedTripId:seed.First)));
        await Receipt(await Send(admin,HttpMethod.Post,$"/api/v1/admin/disruptions/{tripDraft.Id}/publish?operatorCode={seed.Code}",new DisruptionDecisionRequest(true,"Publish")));
        await Expect(await Send(admin,HttpMethod.Put,AdminTrip(seed,"status"),new UpdateTripStateRequest("IN_SERVICE","Reopen without recovery")),HttpStatusCode.Conflict);
        await Receipt(await Send(admin,HttpMethod.Put,AdminTrip(seed,"status"),new UpdateTripStateRequest("CANCELLED","Terminal change")));
        await Receipt(await Send(admin,HttpMethod.Post,$"/api/v1/admin/disruptions/{tripDraft.Id}/resolve?operatorCode={seed.Code}",new DisruptionDecisionRequest(true,"Recorded recovery")));
        await using var scope=factory.Services.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal("CANCELLED",(await db.PlannedTrips.IgnoreQueryFilters().SingleAsync(p=>p.Id==seed.First)).Status);
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Audit_failure_rolls_back_ETA_and_replay_record_and_allows_a_clean_retry()
    {
        var seed=await SeedAsync();
        using var admin=await ActorAsync(seed,"Admin");
        using var faultFactory=factory.WithWebHostBuilder(builder=>builder.ConfigureTestServices(services=>
            services.AddDbContext<ApplicationDbContext>(options=>options.AddInterceptors(new RejectOperationsAudit(seed.Op)))));
        using var faultClient=faultFactory.CreateClient();
        faultClient.DefaultRequestHeaders.Authorization=admin.DefaultRequestHeaders.Authorization;
        var input=new UpdateEtaRequest(seed.Origin,null,seed.Start,"Verified ETA");
        var key=Guid.NewGuid().ToString();
        await Expect(await Send(faultClient,HttpMethod.Post,AdminTrip(seed,"eta"),input,key),HttpStatusCode.Conflict);
        var history=(await admin.GetFromJsonAsync<TripOperationsHistory>(AdminTrip(seed,"operations")))!;
        Assert.Empty(history.EtaUpdates);
        await using(var scope=factory.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0,await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM metro.operations_requests WHERE operator_id={seed.Op}").SingleAsync());
        }
        await Receipt(await Send(admin,HttpMethod.Post,AdminTrip(seed,"eta"),input,key));
        Assert.Single((await admin.GetFromJsonAsync<TripOperationsHistory>(AdminTrip(seed,"operations")))!.EtaUpdates);
    }

    private async Task RefreshAsync(IServiceProvider? provider=null)
    {
        await using var scope=(provider??factory.Services).CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BookingSuggestionProcessor>().ProcessAsync(0,default);
    }
    private async Task Sql(FormattableString sql)
    {
        await using var scope=factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlInterpolatedAsync(sql);
    }
    private async Task<HttpClient> ClientAsync(string user)
    {
        var client=factory.CreateClient();
        client.DefaultRequestHeaders.Authorization=await PaymentScenarioFixture.BearerAsync(factory,user);
        return client;
    }
    private async Task<HttpClient> ActorAsync(Seed seed,string role,bool membership=true)
    {
        await using var scope=factory.Services.CreateAsyncScope();
        var user=await PaymentScenarioFixture.UserAsync(scope.ServiceProvider);
        var users=scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if(!await roles.RoleExistsAsync(role)) Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
        Assert.True((await users.AddToRoleAsync((await users.FindByIdAsync(user))!,role)).Succeeded);
        var client=await ClientAsync(user);
        var token=new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(client.DefaultRequestHeaders.Authorization!.Parameter);
        var claims=token.Claims.Append(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role,role));
        var signing=new Microsoft.IdentityModel.Tokens.SigningCredentials(new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.UTF8.GetBytes(PaymentTestFactory.SigningKey)),Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(
            new System.IdentityModel.Tokens.Jwt.JwtSecurityToken("metro-api","metro-web",claims,DateTime.UtcNow.AddMinutes(-1),DateTime.UtcNow.AddHours(1),signing)));
        if(membership) await Sql($"INSERT INTO metro.operator_accounts(operator_id,user_id,account_type,status,mfa_required) VALUES({seed.Op},{user},'ADMIN','ACTIVE',true)");
        return client;
    }
    private static string AdminTrip(Seed s,string action,Guid? trip=null)=>$"/api/v1/admin/trips/{trip??s.First}/{action}?operatorCode={s.Code}";
    private static string LoadUrl(Seed s)=>$"/api/v1/trips/{s.First}/load?operatorCode={s.Code}";
    private static async Task<HttpResponseMessage> Send(HttpClient client,HttpMethod method,string path,object? body,string? key=null)
    {
        using var request=new HttpRequestMessage(method,path);
        request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString());
        if(body is not null) request.Content=JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    private static async Task Expect(HttpResponseMessage response,HttpStatusCode expected)=>
        Assert.True(response.StatusCode==expected,$"Expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    private static async Task<OperationReceipt> Receipt(HttpResponseMessage response)
    {
        await Expect(response,HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<OperationReceipt>())!;
    }
    private async Task<BookingResponse> PaidOrderAsync(Seed s,HttpClient buyer)
    {
        var create=await Send(buyer,HttpMethod.Post,$"/api/v1/orders?operatorCode={s.Code}",new BookingSelection(s.Product,ServiceDate:s.Date,OriginStationId:s.Origin,DestinationStationId:s.Destination));
        await Expect(create,HttpStatusCode.Created);
        var order=(await create.Content.ReadFromJsonAsync<BookingResponse>())!;
        await Expect(await Send(buyer,HttpMethod.Post,$"/api/v1/orders/{order.Id}/confirm",new ConfirmBookingRequest(1,order.TotalMinor)),HttpStatusCode.OK);
        var payment=await buyer.PostAsJsonAsync("/api/v1/payments",new{orderId=order.Id,orderVersion=1,idempotencyKey=Guid.NewGuid().ToString()});
        await Expect(payment,HttpStatusCode.Created);
        var paymentId=(await payment.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var json=JsonSerializer.Serialize(new{eventId=Guid.NewGuid().ToString(),idempotencyKey=Guid.NewGuid().ToString(),paymentId=paymentId.ToString(),amount=order.TotalMinor,currency="VND"});
        using var callback=new HttpRequestMessage(HttpMethod.Post,"/api/v1/webhooks/momo"){Content=new StringContent(json,Encoding.UTF8,"application/json")};
        callback.Headers.Add("X-MoMo-Signature",Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(StubPaymentGateway.Secret),Encoding.UTF8.GetBytes(json))));
        await Expect(await buyer.SendAsync(callback),HttpStatusCode.OK);
        return order;
    }

    private async Task<Seed> SeedAsync()
    {
        await using var scope=factory.Services.CreateAsyncScope();
        var buyer=await PaymentScenarioFixture.UserAsync(scope.ServiceProvider);
        var checker=await PaymentScenarioFixture.UserAsync(scope.ServiceProvider);
        var op=Guid.NewGuid();var code=$"BE14-{op:N}";var line=Guid.NewGuid();var origin=Guid.NewGuid();var middle=Guid.NewGuid();var destination=Guid.NewGuid();
        var calendar=Guid.NewGuid();var schedule=Guid.NewGuid();var profile=Guid.NewGuid();var first=Guid.NewGuid();var next=Guid.NewGuid();var fare=Guid.NewGuid();var product=Guid.NewGuid();
        var date=TicketRules.LocalDate(DateTimeOffset.UtcNow).AddDays(1);var start=new DateTimeOffset(TicketRules.Midnight(date).AddHours(6).AddMinutes(10));var effective=DateTimeOffset.UtcNow.AddDays(-1);
        await Sql($"""
            INSERT INTO metro.operators(id,code,name) VALUES({op},{code},'BE14 test');
            INSERT INTO metro.operator_accounts(operator_id,user_id,account_type,status) VALUES({op},{buyer},'PASSENGER','ACTIVE');
            INSERT INTO metro.lines(id,operator_id,code,name) VALUES({line},{op},'L1','Test line');
            INSERT INTO metro.stations(id,operator_id,code,name) VALUES({origin},{op},'A','Origin'),({middle},{op},'B','Middle'),({destination},{op},'C','Destination');
            INSERT INTO metro.line_stations(operator_id,line_id,station_id,direction,sequence_no,distance_from_origin_m)
            VALUES({op},{line},{origin},0,1,0),({op},{line},{middle},0,2,500),({op},{line},{destination},0,3,1000);
            INSERT INTO metro.fare_policy_versions(id,operator_id,code,version,scope_key,status,effective_from,created_by_user_id,approved_by_user_id,approved_at,published_by_user_id,published_at)
            VALUES({fare},{op},'F1',1,'DEFAULT','PUBLISHED',{effective},{buyer},{checker},{effective},{checker},{effective});
            INSERT INTO metro.ticket_products(id,operator_id,code,name,product_type,expiry_rule) VALUES({product},{op},'SINGLE','Single','SINGLE_TRIP','SINGLE_JOURNEY');
            INSERT INTO metro.fare_matrix_entries(operator_id,fare_policy_version_id,ticket_product_id,payment_channel,amount_minor) VALUES({op},{fare},{product},'ANY',5000);
            INSERT INTO metro.service_calendars(id,operator_id,code,name,day_type,effective_from,status) VALUES({calendar},{op},'CAL','Calendar','WEEKDAY',{date},'PUBLISHED');
            INSERT INTO metro.schedule_versions(id,operator_id,line_id,service_calendar_id,version,code,status,effective_from,created_by_user_id,approved_by_user_id,approved_at,published_by_user_id,published_at)
            VALUES({schedule},{op},{line},{calendar},1,'SCH','PUBLISHED',{effective},{buyer},{checker},{effective},{checker},{effective});
            INSERT INTO metro.capacity_profiles(id,operator_id,code,version,line_id,seated_capacity,standing_capacity,status,effective_from,created_by_user_id,max_load_kg)
            VALUES({profile},{op},'KG',1,{line},0,0,'PUBLISHED',{effective},{buyer},18000);
            INSERT INTO metro.planned_trips(id,operator_id,schedule_version_id,line_id,trip_code,service_date,direction,planned_start_at,planned_end_at,load_profile_id)
            VALUES({first},{op},{schedule},{line},'FIRST',{date},0,{start},{start.AddMinutes(6)},{profile}),({next},{op},{schedule},{line},'NEXT',{date},0,{start.AddMinutes(20)},{start.AddMinutes(26)},{profile});
            INSERT INTO metro.planned_stop_times(operator_id,planned_trip_id,station_id,sequence_no,planned_arrival_at,planned_departure_at)
            VALUES({op},{first},{origin},1,{start},{start}),({op},{first},{middle},2,{start.AddMinutes(3)},{start.AddMinutes(3)}),({op},{first},{destination},3,{start.AddMinutes(6)},{start.AddMinutes(6)}),
                ({op},{next},{origin},1,{start.AddMinutes(20)},{start.AddMinutes(20)}),({op},{next},{middle},2,{start.AddMinutes(23)},{start.AddMinutes(23)}),({op},{next},{destination},3,{start.AddMinutes(26)},{start.AddMinutes(26)});
            """);
        return new(op,code,buyer,line,origin,middle,destination,first,next,product,date,start);
    }
    private sealed record Seed(Guid Op,string Code,string Buyer,Guid Line,Guid Origin,Guid Middle,Guid Destination,Guid First,Guid Next,Guid Product,DateOnly Date,DateTimeOffset Start);
    private sealed class FixedTime(DateTimeOffset now):TimeProvider { public override DateTimeOffset GetUtcNow()=>now; }
    private sealed class RejectOperationsAudit(Guid op):Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,CancellationToken cancellationToken=default)
        {
            if(eventData.Context!.ChangeTracker.Entries<Metro.Domain.Entities.Identity.AuditLog>()
                .Any(e=>e.Entity.OperatorId==op && e.Entity.EntityType=="SERVICE_OPERATIONS")) throw new InvalidOperationException("Audit unavailable");
            return ValueTask.FromResult(result);
        }
    }
}
