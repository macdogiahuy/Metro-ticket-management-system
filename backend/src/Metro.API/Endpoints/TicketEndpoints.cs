using System.Security.Claims;
using Metro.Application.Abstractions.Ticketing;

namespace Metro.API.Endpoints;

public static class TicketEndpoints
{
    private static string Subject(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.FindFirstValue("sub") ?? throw new TicketException("SUBJECT_REQUIRED", 401);

    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Tickets & history").RequireAuthorization();
        group.MapGet("/tickets", (ClaimsPrincipal user, string? operatorCode, int? page, int? pageSize, ITicketService service, CancellationToken ct) =>
            service.GetTicketsAsync(Subject(user), operatorCode, page ?? 1, pageSize ?? 20, ct))
            .WithName("GetMyTickets").WithSummary("UC13 — Vé của tôi và vé trẻ được giám hộ")
            .Produces<TicketPage<TicketView>>();
        group.MapGet("/tickets/{id:guid}", (ClaimsPrincipal user, Guid id, string? operatorCode, ITicketService service, CancellationToken ct) =>
            service.GetTicketAsync(Subject(user), id, operatorCode, ct))
            .WithName("GetMyTicket").WithSummary("UC13 — Chi tiết vé và chuyến gợi ý").Produces<TicketDetail>();
        group.MapGet("/tickets/{id:guid}/qr", async (HttpContext http, Guid id, string? operatorCode, ITicketService service, CancellationToken ct) =>
        {
            var png = await service.GetQrAsync(Subject(http.User), id, operatorCode, ct);
            http.Response.Headers.CacheControl = "no-store, private";
            http.Response.Headers.Pragma = "no-cache";
            return Results.File(png, "image/png");
        }).WithName("GetMyTicketQr").WithSummary("UC13 — QR hiện hành; GET không tạo QR mới").Produces(200, contentType: "image/png");
        group.MapGet("/orders", (ClaimsPrincipal user, string? operatorCode, int? page, int? pageSize, DateOnly? fromDate, DateOnly? toDate, ITicketService service, CancellationToken ct) =>
            service.GetOrdersAsync(Subject(user), operatorCode, page ?? 1, pageSize ?? 20, fromDate, toDate, ct))
            .WithName("GetMyOrders").WithSummary("UC14 — Lịch sử đơn mua của tôi").Produces<TicketPage<OrderHistoryItem>>();
        group.MapGet("/payments", (ClaimsPrincipal user, string? operatorCode, int? page, int? pageSize, DateOnly? fromDate, DateOnly? toDate, ITicketService service, CancellationToken ct) =>
            service.GetPaymentsAsync(Subject(user), operatorCode, page ?? 1, pageSize ?? 20, fromDate, toDate, ct))
            .WithName("GetMyPayments").WithSummary("UC15 — Lịch sử thanh toán, đối soát và hoàn tiền").Produces<TicketPage<PaymentHistoryItem>>();
        group.MapPost("/orders/{id:guid}/issue-free", async (HttpContext http, Guid id, FreeIssueRequest request,
            string? operatorCode, ITicketService service, CancellationToken ct) =>
        {
            if (http.Request.Headers["Idempotency-Key"].Count != 1) throw new TicketException("IDEMPOTENCY_KEY_REQUIRED", 400);
            var result = await service.IssueFreeAsync(Subject(http.User), id, request, http.Request.Headers["Idempotency-Key"].ToString(), operatorCode, ct);
            return Results.Json(result, statusCode: result.Status == "ISSUED" ? 200 : 202);
        }).WithName("IssueFreeChildTicket").WithSummary("UC13 — Xác nhận vé lượt miễn phí trẻ dưới 6")
            .Produces<FreeIssueResult>(200).Produces<FreeIssueResult>(202).ProducesProblem(400).ProducesProblem(403).ProducesProblem(409);
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store, private";
            return await next(context);
        });
        return app;
    }
}
