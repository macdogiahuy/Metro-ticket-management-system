using Metro.IntegrationTests.Infrastructure;

namespace Metro.IntegrationTests.Payments;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PaymentCollection : ICollectionFixture<PaymentTestFactory>
{
    public const string Name = "Payment API";
}
