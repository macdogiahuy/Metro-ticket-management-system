using Metro.Application.Abstractions.MultiTenancy;

namespace Metro.Infrastructure.Persistence.MultiTenancy;

public class OperatorContext : IOperatorContext
{
    public Guid? CurrentOperatorId { get; set; }
}
