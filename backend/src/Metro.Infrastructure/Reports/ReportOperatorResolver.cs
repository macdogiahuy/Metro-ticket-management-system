using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Abstractions.Reports;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Reports;

/// <summary>
/// Reports are staff data. Unlike passenger ticket/payment services, their tenant cannot
/// be inferred from an owned order and must never be chosen from a client parameter.
/// </summary>
public sealed class ReportOperatorResolver(ApplicationDbContext db, IOperatorContext operatorContext) : IReportOperatorResolver
{
    public async Task<Guid> ResolveAsync(string userId, CancellationToken cancellationToken)
    {
        var operatorId = await db.OperatorAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.UserId == userId && a.Status == "ACTIVE" && a.Operator.Status == "ACTIVE")
            .OrderBy(a => a.Id)
            .Select(a => (Guid?)a.OperatorId)
            .FirstOrDefaultAsync(cancellationToken);

        if (operatorId is null)
            throw new ReportException("REPORT_OPERATOR_FORBIDDEN", StatusCodes.Status403Forbidden,
                "The administrator has no active operator account for reports.");

        operatorContext.CurrentOperatorId = operatorId.Value;
        return operatorId.Value;
    }
}
