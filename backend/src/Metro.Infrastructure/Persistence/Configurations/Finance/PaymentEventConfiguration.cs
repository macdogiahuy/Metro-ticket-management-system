using Metro.Domain.Entities.Finance;
using Metro.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations.Finance;

public sealed class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> builder)
    {
        // The existing SQL deployment owns DDL, checks, triggers and retention rules.
        builder.ToTable("payment_events", "metro", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.PaymentId).HasColumnName("payment_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.ProviderEventId).HasColumnName("provider_event_id").HasColumnType("text").IsRequired(true);
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasColumnType("varchar(120)").IsRequired(true).HasMaxLength(120);
        builder.Property(x => x.EventType).HasColumnName("event_type").HasColumnType("varchar(60)").IsRequired(true).HasMaxLength(60);
        builder.Property(x => x.SignatureVerified).HasColumnName("signature_verified").HasColumnType("boolean").IsRequired(true);
        builder.Property(x => x.ReceivedAmountMinor).HasColumnName("received_amount_minor").HasColumnType("bigint").IsRequired(false);
        builder.Property(x => x.ReceivedCurrency).HasColumnName("received_currency").HasColumnType("char(3)").IsRequired(false).HasMaxLength(3).IsFixedLength();
        builder.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasColumnType("char(64)").IsRequired(true).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasColumnType("text").IsRequired(true);
        builder.Property(x => x.ReceivedAt).HasColumnName("received_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.ProcessedAt).HasColumnName("processed_at").HasColumnType("timestamptz").IsRequired(false);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");

        builder.HasOne(x => x.Payment).WithMany().HasForeignKey(x => new { x.OperatorId, x.PaymentId })
            .HasPrincipalKey(x => new { x.OperatorId, x.Id }).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.OperatorId, x.ProviderEventId }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.CorrelationId }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.PaymentId, x.ReceivedAt }).HasDatabaseName("ix_payment_events_payment").IsDescending(false, false, true);
    }
}
