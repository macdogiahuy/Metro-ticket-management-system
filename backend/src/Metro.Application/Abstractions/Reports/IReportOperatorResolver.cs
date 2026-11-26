namespace Metro.Application.Abstractions.Reports;

/// <summary>Resolves the report tenant from the authenticated administrator's own membership.</summary>
public interface IReportOperatorResolver
{
    Task<Guid> ResolveAsync(string userId, CancellationToken cancellationToken);
}
