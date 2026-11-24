using Metro.Domain.Entities.Finance;
using Metro.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations.Finance;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        // The existing SQL deployment owns DDL, checks, triggers and retention rules.
        builder.ToTable("payments", "metro", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.OperatorId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.OrderId).HasColumnName("order_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.PaymentCode).HasColumnName("payment_code").HasColumnType("varchar(60)").IsRequired(true).HasMaxLength(60);
        builder.Property(x => x.Provider).HasColumnName("provider").HasColumnType("varchar(60)").IsRequired(true).HasMaxLength(60);
        builder.Property(x => x.ProviderPaymentReference).HasColumnName("provider_payment_reference").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.Method).HasColumnName("method").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<PaymentMethod>());
        builder.Property(x => x.AmountMinor).HasColumnName("amount_minor").HasColumnType("bigint").IsRequired(true);
        builder.Property(x => x.Currency).HasColumnName("currency").HasColumnType("char(3)").IsRequired(true).HasMaxLength(3).IsFixedLength().HasDefaultValueSql("'VND'");
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<PaymentStatus>()).HasDefaultValueSql("'PENDING'");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasColumnType("varchar(120)").IsRequired(true).HasMaxLength(120);
        builder.Property(x => x.InitiatedAt).HasColumnName("initiated_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.VerifiedAt).HasColumnName("verified_at").HasColumnType("timestamptz").IsRequired(false);
        builder.Property(x => x.SucceededAt).HasColumnName("succeeded_at").HasColumnType("timestamptz").IsRequired(false);
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.AttemptNumber).HasColumnName("attempt_number").HasColumnType("integer").IsRequired(false);
        builder.Property(x => x.CheckoutUrl).HasColumnName("checkout_url").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.ReconciliationStatus).HasColumnName("reconciliation_status").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<PaymentReconciliationStatus>()).HasDefaultValueSql("'UNRECONCILED'");
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");

        builder.Property(x => x.OrderVersion).HasColumnName("order_version").HasColumnType("integer").ValueGeneratedOnAdd();
        builder.HasOne<Metro.Domain.Entities.Orders.Order>().WithMany()
            .HasForeignKey("OperatorId", "OrderId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<Metro.Domain.Entities.Orders.OrderVersion>().WithMany().HasForeignKey(x => new { x.OperatorId, x.OrderId, x.OrderVersion }).HasPrincipalKey(x => new { x.OperatorId, x.OrderId, x.Version }).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => new { x.OperatorId, x.PaymentCode }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.Provider, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.Provider, x.ProviderPaymentReference }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.CorrelationId }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.OrderId, x.CreatedAt }).HasDatabaseName("ix_payments_order").IsDescending(false, false, true);
        builder.HasIndex(x => new { x.OperatorId, x.OrderId }).HasDatabaseName("uq_succeeded_payment_per_order").IsUnique().HasFilter("status IN ('SUCCEEDED', 'PARTIALLY_REFUNDED', 'REFUNDED')");
    }
}
