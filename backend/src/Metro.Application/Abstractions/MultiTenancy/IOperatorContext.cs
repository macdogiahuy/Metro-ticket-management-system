namespace Metro.Application.Abstractions.MultiTenancy;

/// <summary>
/// Provides the current operator context for multi-tenancy.
/// When CurrentOperatorId is set, EF Core automatically applies the global query filter
/// to restrict queries to that operator's data.
/// </summary>
public interface IOperatorContext
{
    Guid? CurrentOperatorId { get; set; }
}
