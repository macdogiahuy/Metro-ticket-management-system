using Metro.Domain.Entities.Finance;
using Metro.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations.Finance;

public sealed class RefundTransactionConfiguration : IEntityTypeConfiguration<RefundTransaction>
{
    public void Configure(EntityTypeBuilder<RefundTransaction> builder)
    {
        // The existing SQL deployment owns DDL, checks, triggers and retention rules.
        builder.ToTable("refund_transactions", "metro", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.OperatorId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.RefundRequestId).HasColumnName("refund_request_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.PaymentId).HasColumnName("payment_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.RefundCode).HasColumnName("refund_code").HasColumnType("varchar(60)").IsRequired(true).HasMaxLength(60);
        builder.Property(x => x.Provider).HasColumnName("provider").HasColumnType("varchar(60)").IsRequired(true).HasMaxLength(60);
        builder.Property(x => x.ProviderRefundReference).HasColumnName("provider_refund_reference").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.Method).HasColumnName("method").HasColumnType("text").IsRequired(true);
        builder.Property(x => x.AmountMinor).HasColumnName("amount_minor").HasColumnType("bigint").IsRequired(true);
        builder.Property(x => x.Currency).HasColumnName("currency").HasColumnType("char(3)").IsRequired(true).HasMaxLength(3).IsFixedLength().HasDefaultValueSql("'VND'");
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<RefundTransactionStatus>()).HasDefaultValueSql("'PENDING'");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasColumnType("varchar(120)").IsRequired(true).HasMaxLength(120);
        builder.Property(x => x.PayoutRecipientSnapshot).HasColumnName("payout_recipient_snapshot").HasColumnType("jsonb").IsRequired(true).HasDefaultValueSql("'{}'");
        builder.Property(x => x.ProcessedByUserId).HasColumnName("processed_by_user_id").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.SucceededAt).HasColumnName("succeeded_at").HasColumnType("timestamptz").IsRequired(false);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");

        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.ProcessedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.RefundRequest).WithMany().HasForeignKey(x => new { x.OperatorId, x.RefundRequestId })
            .HasPrincipalKey(x => new { x.OperatorId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Payment).WithMany().HasForeignKey(x => new { x.OperatorId, x.PaymentId })
            .HasPrincipalKey(x => new { x.OperatorId, x.Id }).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.OperatorId, x.RefundCode }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.Provider, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.Provider, x.ProviderRefundReference }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.CorrelationId }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.RefundRequestId }).HasDatabaseName("uq_unresolved_refund_per_request").IsUnique().HasFilter("status IN ('PENDING','PROCESSING','UNKNOWN')");
    }
}
