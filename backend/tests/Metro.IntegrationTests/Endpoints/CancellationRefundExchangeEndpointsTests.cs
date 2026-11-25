using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metro.Application.Abstractions.Booking;
using Metro.Application.Abstractions.Payment;
using Metro.Application.Abstractions.Ticketing;
using Metro.Domain.Entities.Finance;
using Metro.Domain.Entities.Orders;
using Metro.Domain.Entities.Ticketing;
using Metro.Infrastructure.Persistence.Context;
using Metro.IntegrationTests.Infrastructure;
using Metro.IntegrationTests.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Metro.IntegrationTests.Payments.PaymentScenarioFixture;

namespace Metro.IntegrationTests.Endpoints;

[Collection(PaymentCollection.Name)]
public sealed class CancellationRefundExchangeEndpointsTests(PaymentTestFactory factory)
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Cancel_unpaid_order_enforces_auth_idempotency_and_cancels_correctly()
    {
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, true);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var json = JsonSerializer.Serialize(new { BookingCheckout = true, Subtotal = 15000L, Discount = 0L, Amount = 15000L, AppliedGroup = "ORDINARY" });
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE metro.orders SET fare_snapshot = {json}::jsonb WHERE id = {seed.Order}");
        }

        // 1. 401 Unauthorized when unauthenticated
        var cancelUrl = $"/api/v1/orders/{seed.Order}/cancel";
        var unauthReq = new HttpRequestMessage(HttpMethod.Post, cancelUrl)
        {
            Content = JsonContent.Create(new CancelOrderRequest(1, "Buyer cancelled"))
        };
        unauthReq.Headers.Add("Idempotency-Key", "idem-cancel-unauth");
        var unauthRes = await client.SendAsync(unauthReq);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);

        // 2. 403 Forbidden when stranger attempts to cancel
        var strangerId = await UserAsync(factory.Services);
        using var strangerClient = factory.CreateClient();
        strangerClient.DefaultRequestHeaders.Authorization = await BearerAsync(factory, strangerId);

        var strangerReq = new HttpRequestMessage(HttpMethod.Post, cancelUrl)
        {
            Content = JsonContent.Create(new CancelOrderRequest(1, "Stranger cancelled"))
        };
        strangerReq.Headers.Add("Idempotency-Key", "idem-cancel-stranger");
        var strangerRes = await strangerClient.SendAsync(strangerReq);
        Assert.Equal(HttpStatusCode.Forbidden, strangerRes.StatusCode);

        // 3. 200 OK when buyer cancels unpaid order
        using var buyerClient = factory.CreateClient();
        buyerClient.DefaultRequestHeaders.Authorization = await BearerAsync(factory, seed.User);

        var buyerReq = new HttpRequestMessage(HttpMethod.Post, cancelUrl)
        {
            Content = JsonContent.Create(new CancelOrderRequest(1, "BUYER_CANCELLED"))
        };
        buyerReq.Headers.Add("Idempotency-Key", "idem-cancel-buyer-1");
        var buyerRes = await buyerClient.SendAsync(buyerReq);
        Assert.Equal(HttpStatusCode.OK, buyerRes.StatusCode);

        var response = await buyerRes.Content.ReadFromJsonAsync<BookingResponse>();
        Assert.NotNull(response);
        Assert.Equal("CANCELLED", response.Status);

        // Check DB state: order cancelled via metro.cancel_unpaid_order
        await using (var checkScope = factory.Services.CreateAsyncScope())
        {
            var db = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbOrder = await db.Orders.IgnoreQueryFilters().SingleAsync(o => o.Id == seed.Order);
            Assert.Equal("CANCELLED", dbOrder.Status);
        }

        // 4. Idempotent replay: calling cancel again with same or new key on already cancelled order returns 200
        var replayReq = new HttpRequestMessage(HttpMethod.Post, cancelUrl)
        {
            Content = JsonContent.Create(new CancelOrderRequest(1, "BUYER_CANCELLED"))
        };
        replayReq.Headers.Add("Idempotency-Key", "idem-cancel-buyer-replay");
        var replayRes = await buyerClient.SendAsync(replayReq);
        Assert.Equal(HttpStatusCode.OK, replayRes.StatusCode);
        var replayResponse = await replayRes.Content.ReadFromJsonAsync<BookingResponse>();
        Assert.NotNull(replayResponse);
        Assert.Equal("CANCELLED", replayResponse.Status);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Refund_flow_enforces_auth_role_permissions_and_preserves_payment_record()
    {
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, true);

        // 1. Setup payment and ticket for seed order
        client.DefaultRequestHeaders.Authorization = await BearerAsync(factory, seed.User);
        var payCreated = await client.PostAsJsonAsync("/api/v1/payments", new { orderId = seed.Order, idempotencyKey = "pay-refund-test-1" });
        Assert.Equal(HttpStatusCode.Created, payCreated.StatusCode);
        using var paymentDoc = JsonDocument.Parse(await payCreated.Content.ReadAsStringAsync());
        var paymentId = paymentDoc.RootElement.GetProperty("id").GetGuid();

        // Simulate successful MoMo webhook callback
        var webhookPayload = JsonSerializer.Serialize(new
        {
            eventId = Guid.NewGuid().ToString(),
            idempotencyKey = "hook-refund-1",
            paymentId = paymentId.ToString(),
            amount = 15000,
            currency = "VND"
        });
        using var webhookMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/momo")
        {
            Content = new StringContent(webhookPayload, Encoding.UTF8, "application/json")
        };
        webhookMsg.Headers.Add("X-MoMo-Signature", Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(StubPaymentGateway.Secret), Encoding.UTF8.GetBytes(webhookPayload))));
        var webhookRes = await client.SendAsync(webhookMsg);
        Assert.Equal(HttpStatusCode.OK, webhookRes.StatusCode);

        // Process issuance job to get active ticket
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<Metro.Application.Abstractions.MultiTenancy.IOperatorContext>().CurrentOperatorId = seed.Op;
            await scope.ServiceProvider.GetRequiredService<Metro.Infrastructure.Ticketing.TicketIssuanceProcessor>().ProcessDueAsync(500, operatorId: seed.Op);
        }

        Guid ticketId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ticketId = await db.Tickets.IgnoreQueryFilters().Where(t => t.OrderId == seed.Order).Select(t => t.Id).FirstAsync();
        }

        // 2. 401 Unauthorized for unauthenticated refund request
        using var unauthClient = factory.CreateClient();
        var unauthReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/refunds")
        {
            Content = JsonContent.Create(new CreateRefundRequest(paymentId, ticketId, "SERVICE_DISRUPTION", 15000))
        };
        unauthReq.Headers.Add("Idempotency-Key", "idem-refund-unauth");
        Assert.Equal(HttpStatusCode.Unauthorized, (await unauthClient.SendAsync(unauthReq)).StatusCode);

        // 3. Passenger submits refund request: 201 Created
        var createRefundReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/refunds")
        {
            Content = JsonContent.Create(new CreateRefundRequest(paymentId, ticketId, "SERVICE_DISRUPTION", 15000, "Train disrupted"))
        };
        createRefundReq.Headers.Add("Idempotency-Key", "idem-refund-create-1");
        var createRes = await client.SendAsync(createRefundReq);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

        var refundResponse = await createRes.Content.ReadFromJsonAsync<RefundResponse>();
        Assert.NotNull(refundResponse);
        Assert.Equal("SUBMITTED", refundResponse.Status);
        Assert.Equal(15000, refundResponse.RequestedAmountMinor);
        var refundId = refundResponse.Id;

        // 4. 403 Forbidden when normal passenger attempts to approve refund
        var passengerApproveReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/refunds/{refundId}/approve")
        {
            Content = JsonContent.Create(new ApproveRefundRequest(15000, "Passenger tries self approval"))
        };
        passengerApproveReq.Headers.Add("Idempotency-Key", "idem-approve-forbidden");
        var passengerApproveRes = await client.SendAsync(passengerApproveReq);
        Assert.Equal(HttpStatusCode.Forbidden, passengerApproveRes.StatusCode);

        // 5. Admin approves refund: 200 OK
        var adminId = await UserAsync(factory.Services);
        using var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = await BearerAsync(factory, adminId, "Admin");

        var adminApproveReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/refunds/{refundId}/approve")
        {
            Content = JsonContent.Create(new ApproveRefundRequest(15000, "Approved by administrator"))
        };
        adminApproveReq.Headers.Add("Idempotency-Key", "idem-approve-admin-1");
        var adminApproveRes = await adminClient.SendAsync(adminApproveReq);
        Assert.Equal(HttpStatusCode.OK, adminApproveRes.StatusCode);

        var approvedResponse = await adminApproveRes.Content.ReadFromJsonAsync<RefundResponse>();
        Assert.NotNull(approvedResponse);
        Assert.Equal("COMPLETED", approvedResponse.Status);
        Assert.Equal(15000, approvedResponse.ApprovedAmountMinor);
        Assert.NotNull(approvedResponse.TransactionId);

        // Verify Payment is NOT deleted ("không xóa khoản thu") and updated to REFUNDED
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var payment = await db.Payments.IgnoreQueryFilters().SingleAsync(p => p.Id == paymentId);
            Assert.Equal(PaymentStatus.Refunded, payment.Status);

            var refundTx = await db.RefundTransactions.IgnoreQueryFilters().SingleAsync(t => t.RefundRequestId == refundId);
            Assert.Equal(RefundTransactionStatus.Succeeded, refundTx.Status);
            Assert.Equal(15000, refundTx.AmountMinor);

            // Ticket is revoked
            var ticket = await db.Tickets.IgnoreQueryFilters().SingleAsync(t => t.Id == ticketId);
            Assert.Equal("REVOKED", ticket.Status);
        }

        // 6. Idempotency test: Replaying approve returns 200 OK
        var replayApproveReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/refunds/{refundId}/approve")
        {
            Content = JsonContent.Create(new ApproveRefundRequest(15000, "Approved by administrator"))
        };
        replayApproveReq.Headers.Add("Idempotency-Key", "idem-approve-admin-1");
        var replayApproveRes = await adminClient.SendAsync(replayApproveReq);
        Assert.Equal(HttpStatusCode.OK, replayApproveRes.StatusCode);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Ticket_exchange_flow_enforces_one_change_limit_and_revokes_old_qr()
    {
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, true);

        // 1. Setup payment and ticket for seed order
        client.DefaultRequestHeaders.Authorization = await BearerAsync(factory, seed.User);
        var payCreated = await client.PostAsJsonAsync("/api/v1/payments", new { orderId = seed.Order, idempotencyKey = "pay-change-test-1" });
        Assert.Equal(HttpStatusCode.Created, payCreated.StatusCode);
        using var paymentDoc = JsonDocument.Parse(await payCreated.Content.ReadAsStringAsync());
        var paymentId = paymentDoc.RootElement.GetProperty("id").GetGuid();

        var webhookPayload = JsonSerializer.Serialize(new
        {
            eventId = Guid.NewGuid().ToString(),
            idempotencyKey = "hook-change-1",
            paymentId = paymentId.ToString(),
            amount = 15000,
            currency = "VND"
        });
        using var webhookMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/momo")
        {
            Content = new StringContent(webhookPayload, Encoding.UTF8, "application/json")
        };
        webhookMsg.Headers.Add("X-MoMo-Signature", Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(StubPaymentGateway.Secret), Encoding.UTF8.GetBytes(webhookPayload))));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(webhookMsg)).StatusCode);

        // Process issuance job
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<Metro.Application.Abstractions.MultiTenancy.IOperatorContext>().CurrentOperatorId = seed.Op;
            await scope.ServiceProvider.GetRequiredService<Metro.Infrastructure.Ticketing.TicketIssuanceProcessor>().ProcessDueAsync(500, operatorId: seed.Op);
        }

        Guid ticketId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ticketId = await db.Tickets.IgnoreQueryFilters().Where(t => t.OrderId == seed.Order).Select(t => t.Id).FirstAsync();
        }

        // 2. 401 Unauthorized when unauthenticated
        using var unauthClient = factory.CreateClient();
        var newDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(1);
        var unauthReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{ticketId}/change-requests")
        {
            Content = JsonContent.Create(new CreateTicketChangeRequest(newDate, ReasonType: "SERVICE_DISRUPTION"))
        };
        unauthReq.Headers.Add("Idempotency-Key", "idem-change-unauth");
        Assert.Equal(HttpStatusCode.Unauthorized, (await unauthClient.SendAsync(unauthReq)).StatusCode);

        // 3. Passenger creates change request: 201 Created
        var changeReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{ticketId}/change-requests")
        {
            Content = JsonContent.Create(new CreateTicketChangeRequest(newDate, ReasonType: "SERVICE_DISRUPTION", DecisionReason: "Disruption on line"))
        };
        changeReq.Headers.Add("Idempotency-Key", "idem-change-req-1");
        var changeRes = await client.SendAsync(changeReq);
        Assert.Equal(HttpStatusCode.Created, changeRes.StatusCode);

        var changeResponse = await changeRes.Content.ReadFromJsonAsync<TicketChangeResponse>();
        Assert.NotNull(changeResponse);
        Assert.Equal("REQUESTED", changeResponse.Status);
        var changeRequestId = changeResponse.Id;

        // 4. 403 Forbidden when normal passenger attempts to approve change request
        var passengerApproveReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/ticket-change-requests/{changeRequestId}/approve")
        {
            Content = JsonContent.Create(new ApproveTicketChangeRequest("Passenger approval"))
        };
        passengerApproveReq.Headers.Add("Idempotency-Key", "idem-change-appr-forbidden");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(passengerApproveReq)).StatusCode);

        // 5. Staff or Admin approves change request: 200 OK
        var staffId = await UserAsync(factory.Services);
        using var staffClient = factory.CreateClient();
        staffClient.DefaultRequestHeaders.Authorization = await BearerAsync(factory, staffId, "StationStaff");

        var staffApproveReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/ticket-change-requests/{changeRequestId}/approve")
        {
            Content = JsonContent.Create(new ApproveTicketChangeRequest("Approved disruption change"))
        };
        staffApproveReq.Headers.Add("Idempotency-Key", "idem-change-appr-1");
        var staffApproveRes = await staffClient.SendAsync(staffApproveReq);
        Assert.Equal(HttpStatusCode.OK, staffApproveRes.StatusCode);

        var approvedChange = await staffApproveRes.Content.ReadFromJsonAsync<TicketChangeResponse>();
        Assert.NotNull(approvedChange);
        Assert.Equal("APPLIED", approvedChange.Status);

        // Verify DB: ticket change count is 1, old QR credentials revoked
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ticket = await db.Tickets.IgnoreQueryFilters().SingleAsync(t => t.Id == ticketId);
            Assert.Equal(1, ticket.ChangeCount);
            Assert.Equal(newDate, ticket.ServiceDate);

            // Verify old credentials revoked
            var revokedCredentials = await db.TicketCredentials.IgnoreQueryFilters()
                .Where(c => c.TicketId == ticketId && c.Status == "REVOKED")
                .ToListAsync();
            Assert.NotEmpty(revokedCredentials);
        }

        // 6. Second change attempt on the same ticket is REJECTED (Rule: 1 change max per ticket!)
        var secondDate = newDate.AddDays(1);
        var secondChangeReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tickets/{ticketId}/change-requests")
        {
            Content = JsonContent.Create(new CreateTicketChangeRequest(secondDate, ReasonType: "SERVICE_DISRUPTION"))
        };
        secondChangeReq.Headers.Add("Idempotency-Key", "idem-change-req-2");
        var secondChangeRes = await client.SendAsync(secondChangeReq);
        Assert.Equal(HttpStatusCode.Conflict, secondChangeRes.StatusCode);
        using var conflictProblem = JsonDocument.Parse(await secondChangeRes.Content.ReadAsStringAsync());
        Assert.Equal("TICKET_CHANGE_LIMIT_REACHED", conflictProblem.RootElement.GetProperty("code").GetString());
    }
}
