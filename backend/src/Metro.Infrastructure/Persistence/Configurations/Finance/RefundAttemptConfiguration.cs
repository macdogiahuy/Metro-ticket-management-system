using Metro.Domain.Entities.Finance;
using Metro.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations.Finance;

public sealed class RefundAttemptConfiguration : IEntityTypeConfiguration<RefundAttempt>
{
    public void Configure(EntityTypeBuilder<RefundAttempt> builder)
    {
        // The existing SQL deployment owns DDL, checks, triggers and retention rules.
        builder.ToTable("refund_attempts", "metro", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.RefundTransactionId).HasColumnName("refund_transaction_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.AttemptNo).HasColumnName("attempt_no").HasColumnType("integer").IsRequired(true);
        builder.Property(x => x.ProviderEventId).HasColumnName("provider_event_id").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.RequestHash).HasColumnName("request_hash").HasColumnType("char(64)").IsRequired(true).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.ResponseHash).HasColumnName("response_hash").HasColumnType("char(64)").IsRequired(false).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<RefundAttemptStatus>());
        builder.Property(x => x.ErrorCode).HasColumnName("error_code").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.AttemptedAt).HasColumnName("attempted_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamptz").IsRequired(false);

        builder.HasOne(x => x.RefundTransaction).WithMany().HasForeignKey(x => new { x.OperatorId, x.RefundTransactionId })
            .HasPrincipalKey(x => new { x.OperatorId, x.Id }).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.OperatorId, x.RefundTransactionId, x.AttemptNo }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.ProviderEventId }).IsUnique();
    }
}
