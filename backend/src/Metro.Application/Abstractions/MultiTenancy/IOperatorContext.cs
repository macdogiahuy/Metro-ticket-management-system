namespace Metro.Application.Abstractions.MultiTenancy;

/// <summary>
/// Scoped context set only by a trusted caller after operator authorization.
/// Never populate directly from an unvalidated request header or parameter.
/// Missing/empty operator denies scoped reads and writes; no admin bypass is implied.
/// When CurrentOperatorId is set, EF Core automatically applies the global query filter
/// to restrict queries to that operator's data.
/// </summary>
public interface IOperatorContext
{
    Guid? CurrentOperatorId { get; set; }
}

