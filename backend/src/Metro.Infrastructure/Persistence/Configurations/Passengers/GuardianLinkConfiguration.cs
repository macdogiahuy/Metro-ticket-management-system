using Metro.Domain.Entities.Passengers;
using Metro.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations.Passengers;

public sealed class GuardianLinkConfiguration : IEntityTypeConfiguration<GuardianLink>
{
    public void Configure(EntityTypeBuilder<GuardianLink> builder)
    {
        // The existing SQL deployment owns DDL, checks, triggers and retention rules.
        builder.ToTable("guardian_links", "metro", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.OperatorId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.GuardianUserId).HasColumnName("guardian_user_id").HasColumnType("text").IsRequired(true);
        builder.Property(x => x.ChildUserId).HasColumnName("child_user_id").HasColumnType("text").IsRequired(true);
        builder.Property(x => x.Relationship).HasColumnName("relationship").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<GuardianRelationship>());
        builder.Property(x => x.VerificationType).HasColumnName("verification_type").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<GuardianVerificationType>()).HasDefaultValueSql("'GUARDIANSHIP'");
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<GuardianLinkStatus>()).HasDefaultValueSql("'PENDING'");
        builder.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.EvidencePurgedAt).HasColumnName("evidence_purged_at").HasColumnType("timestamptz").IsRequired(false);
        builder.Property(x => x.SubmittedAt).HasColumnName("submitted_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.VerifiedByUserId).HasColumnName("verified_by_user_id").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.VerifiedAt).HasColumnName("verified_at").HasColumnType("timestamptz").IsRequired(false);
        builder.Property(x => x.ValidFrom).HasColumnName("valid_from").HasColumnType("date").IsRequired(false);
        builder.Property(x => x.ValidTo).HasColumnName("valid_to").HasColumnType("date").IsRequired(false);
        builder.Property(x => x.DecisionReason).HasColumnName("decision_reason").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.RevocationReason).HasColumnName("revocation_reason").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");

        builder.HasOne<Metro.Domain.Entities.Identity.Operator>().WithMany()
            .HasForeignKey("OperatorId")
            .HasPrincipalKey("Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.GuardianUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.ChildUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.VerifiedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.OperatorId, x.GuardianUserId, x.ChildUserId }).HasDatabaseName("uq_guardian_links_active_pair").IsUnique().HasFilter("status = 'ACTIVE'");
        builder.HasIndex(x => new { x.OperatorId, x.GuardianUserId, x.Status }).HasDatabaseName("ix_guardian_links_guardian");
    }
}
