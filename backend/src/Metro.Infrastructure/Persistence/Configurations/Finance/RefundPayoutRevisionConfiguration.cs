using Metro.Domain.Entities.Finance;
using Metro.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations.Finance;

public sealed class RefundPayoutRevisionConfiguration : IEntityTypeConfiguration<RefundPayoutRevision>
{
    public void Configure(EntityTypeBuilder<RefundPayoutRevision> builder)
    {
        // The existing SQL deployment owns DDL, checks, triggers and retention rules.
        builder.ToTable("refund_payout_revisions", "metro", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.RefundRequestId).HasColumnName("refund_request_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.RevisionNo).HasColumnName("revision_no").HasColumnType("integer").IsRequired(true);
        builder.Property(x => x.BankCode).HasColumnName("bank_code").HasColumnType("varchar(40)").IsRequired(false).HasMaxLength(40);
        builder.Property(x => x.AccountCiphertext).HasColumnName("account_ciphertext").HasColumnType("bytea").IsRequired(false);
        builder.Property(x => x.AccountLast4).HasColumnName("account_last4").HasColumnType("varchar(4)").IsRequired(false).HasMaxLength(4);
        builder.Property(x => x.HolderName).HasColumnName("holder_name").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.ChangedByUserId).HasColumnName("changed_by_user_id").HasColumnType("text").IsRequired(true);
        builder.Property(x => x.ChangeReason).HasColumnName("change_reason").HasColumnType("text").IsRequired(true);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");

        builder.HasOne<Metro.Domain.Entities.Identity.Bank>().WithMany()
            .HasForeignKey("BankCode")
            .HasPrincipalKey("Code").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.ChangedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.RefundRequest).WithMany().HasForeignKey(x => new { x.OperatorId, x.RefundRequestId })
            .HasPrincipalKey(x => new { x.OperatorId, x.Id }).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.OperatorId, x.RefundRequestId, x.RevisionNo }).IsUnique();
    }
}
