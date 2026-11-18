namespace Metro.Domain.Common;

/// <summary>
/// Marks an entity that may belong to an operator tenant or be global/system-level.
/// Multi-tenancy global query filter applies when OperatorId is set.
/// </summary>
public interface IOptionalOperatorScoped
{
    Guid? OperatorId { get; set; }
}
