namespace Metro.Application.Abstractions.Ticketing;

public sealed record TicketPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
public sealed record TicketView(Guid Id, string Code, string ProductType, string Status,
    string IssuanceStatus, DateTime? EffectiveFrom, DateTime? EndExclusive, DateOnly? ServiceDate,
    Guid? OriginStationId, Guid? DestinationStationId, bool QrAvailable, string? EligibilityGroup);
public sealed record TripHint(Guid? PlannedTripId, DateTimeOffset? DepartureAt, string Status, DateTimeOffset UpdatedAt, bool IsReservation = false);
public sealed record TicketDetail(TicketView Ticket, IReadOnlyList<TripHint> TripSuggestions);
public sealed record OrderHistoryItem(Guid Id, string Code, int Version, string Status,
    long TotalMinor, string Currency, DateTime CreatedAt, DateTime? PaidAt,
    DateTime? FreeConfirmedAt, DateTime? IssuedAt, string? BeneficiaryName, string ProductType,
    IReadOnlyList<OrderVersionView> Versions);
public sealed record OrderVersionView(int Version, string Status, string ProductType, DateOnly? ServiceDate,
    long TotalMinor, string? EligibilityGroup);
public sealed record RefundHistoryItem(Guid Id, long AmountMinor, string Status);
public sealed record PaymentHistoryItem(Guid Id, Guid OrderId, int? OrderVersion, string Code,
    long AmountMinor, string Currency, string Status, string ReconciliationStatus,
    DateTimeOffset InitiatedAt, DateTimeOffset? SucceededAt, IReadOnlyList<RefundHistoryItem> Refunds);
public sealed record FreeIssueRequest(int OrderVersion);
public sealed record FreeIssueResult(Guid OrderId, int OrderVersion, string Status, IReadOnlyList<Guid> TicketIds);

public sealed class TicketException(string code, int status) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public interface ITicketService
{
    Task<TicketPage<TicketView>> GetTicketsAsync(string user, string? operatorCode, int page, int pageSize, CancellationToken ct);
    Task<TicketDetail> GetTicketAsync(string user, Guid id, string? operatorCode, CancellationToken ct);
    Task<byte[]> GetQrAsync(string user, Guid id, string? operatorCode, CancellationToken ct);
    Task<TicketPage<OrderHistoryItem>> GetOrdersAsync(string user, string? operatorCode, int page, int pageSize, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct);
    Task<TicketPage<PaymentHistoryItem>> GetPaymentsAsync(string user, string? operatorCode, int page, int pageSize, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct);
    Task<FreeIssueResult> IssueFreeAsync(string user, Guid orderId, FreeIssueRequest request, string key, string? operatorCode, CancellationToken ct);
}
