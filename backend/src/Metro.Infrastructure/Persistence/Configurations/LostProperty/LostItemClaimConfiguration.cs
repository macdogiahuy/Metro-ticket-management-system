using Metro.Domain.Entities.LostProperty;
using Metro.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations.LostProperty;

public sealed class LostItemClaimConfiguration : IEntityTypeConfiguration<LostItemClaim>
{
    public void Configure(EntityTypeBuilder<LostItemClaim> builder)
    {
        // The existing SQL deployment owns DDL, checks, triggers and retention rules.
        builder.ToTable("lost_item_claims", "metro", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.OperatorId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.ClaimCode).HasColumnName("claim_code").HasColumnType("varchar(60)").IsRequired(true).HasMaxLength(60);
        builder.Property(x => x.LostReportId).HasColumnName("lost_report_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.FoundItemId).HasColumnName("found_item_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.ClaimantUserId).HasColumnName("claimant_user_id").HasColumnType("text").IsRequired(true);
        builder.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<LostItemClaimStatus>()).HasDefaultValueSql("'SUBMITTED'");
        builder.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.DecidedAt).HasColumnName("decided_at").HasColumnType("timestamptz").IsRequired(false);
        builder.Property(x => x.DecisionReason).HasColumnName("decision_reason").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");

        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.ClaimantUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.DecidedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.LostReport).WithMany().HasForeignKey(x => new { x.OperatorId, x.LostReportId })
            .HasPrincipalKey(x => new { x.OperatorId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.FoundItem).WithMany().HasForeignKey(x => new { x.OperatorId, x.FoundItemId })
            .HasPrincipalKey(x => new { x.OperatorId, x.Id }).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.OperatorId, x.ClaimCode }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.FoundItemId }).HasDatabaseName("uq_lost_item_claims_approved").IsUnique().HasFilter("status = 'APPROVED'");
        builder.HasIndex(x => new { x.OperatorId, x.FoundItemId, x.ClaimantUserId }).HasDatabaseName("uq_lost_item_claims_open_per_user").IsUnique().HasFilter("status IN ('SUBMITTED','APPROVED')");
    }
}
