namespace Metro.Application.Abstractions.Network;

/// <summary>BE-13 UC29 admin service calendar and versioned schedule contracts.</summary>
public sealed record CreateServiceCalendarRequest(
    string Code, string Name, string DayType, short[] DaysOfWeek,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, short Priority, string? Status, string? Reason);

public sealed record AdminServiceCalendarResponse(
    Guid Id, string Code, string Name, string DayType, short[] DaysOfWeek,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, short Priority, string Status, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record CreateScheduleVersionRequest(
    Guid LineId, Guid ServiceCalendarId, string Code, DateTime EffectiveFrom, DateTime? EffectiveTo, string? Reason);

public sealed record ScheduleDecisionRequest(string? Reason);

public sealed record AdminScheduleVersionResponse(
    Guid Id, Guid LineId, Guid ServiceCalendarId, int Version, string Code, string Status,
    DateTime EffectiveFrom, DateTime? EffectiveTo, string CreatedByUserId, string? ApprovedByUserId,
    string? PublishedByUserId, DateTime? ApprovedAt, DateTime? PublishedAt, string? DecisionReason,
    DateTime CreatedAt, DateTime UpdatedAt);

public interface IAdminScheduleService
{
    Task<IReadOnlyList<AdminServiceCalendarResponse>> ListCalendarsAsync(string adminUserId, CancellationToken ct);
    Task<AdminServiceCalendarResponse> CreateCalendarAsync(CreateServiceCalendarRequest request, string adminUserId, CancellationToken ct);
    Task<IReadOnlyList<AdminScheduleVersionResponse>> ListSchedulesAsync(Guid? lineId, string? status, string adminUserId, CancellationToken ct);
    Task<AdminScheduleVersionResponse> GetScheduleAsync(Guid scheduleId, string adminUserId, CancellationToken ct);
    Task<AdminScheduleVersionResponse> CreateScheduleAsync(CreateScheduleVersionRequest request, string adminUserId, CancellationToken ct);
    Task<AdminScheduleVersionResponse> ApproveScheduleAsync(Guid scheduleId, ScheduleDecisionRequest request, string adminUserId, CancellationToken ct);
    Task<AdminScheduleVersionResponse> PublishScheduleAsync(Guid scheduleId, ScheduleDecisionRequest request, string adminUserId, CancellationToken ct);
}
