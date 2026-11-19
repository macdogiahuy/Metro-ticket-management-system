using Metro.Domain.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations;

public sealed class EligibilityEvidenceConfiguration : IEntityTypeConfiguration<EligibilityEvidence>
{
    public void Configure(EntityTypeBuilder<EligibilityEvidence> builder)
    {
        builder.ToTable("eligibility_evidence", "metro");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.ValidFrom).HasColumnName("valid_from");
        builder.Property(x => x.ValidTo).HasColumnName("valid_to");
        builder.Property(x => x.EvidenceObjectKey).HasColumnName("evidence_object_key");
        builder.Property(x => x.EvidenceFileName).HasColumnName("evidence_file_name");
        builder.Property(x => x.EvidenceContentType).HasColumnName("evidence_content_type");
        builder.Property(x => x.EvidenceSize).HasColumnName("evidence_size");
        builder.Property(x => x.EvidenceSha256).HasColumnName("evidence_sha256").HasMaxLength(64);
        builder.Property(x => x.DecisionReason).HasColumnName("decision_reason");
        builder.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id");
        builder.Property(x => x.DecidedAt).HasColumnName("decided_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("clock_timestamp()");
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });
    }
}
