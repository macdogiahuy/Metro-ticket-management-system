namespace Metro.Domain.Common;

/// <summary>
/// Marks an entity that belongs to an operator tenant.
/// Multi-tenancy global query filter applies to this entity.
/// </summary>
public interface IOperatorScoped
{
    Guid OperatorId { get; set; }
}
