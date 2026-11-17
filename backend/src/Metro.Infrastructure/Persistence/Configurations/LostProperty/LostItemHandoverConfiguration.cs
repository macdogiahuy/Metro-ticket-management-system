using Metro.Domain.Entities.LostProperty;
using Metro.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations.LostProperty;

public sealed class LostItemHandoverConfiguration : IEntityTypeConfiguration<LostItemHandover>
{
    public void Configure(EntityTypeBuilder<LostItemHandover> builder)
    {
        // The existing SQL deployment owns DDL, checks, triggers and retention rules.
        builder.ToTable("lost_item_handovers", "metro", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.OperatorId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.FoundItemId).HasColumnName("found_item_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.ClaimId).HasColumnName("claim_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.HandedToUserId).HasColumnName("handed_to_user_id").HasColumnType("text").IsRequired(true);
        builder.Property(x => x.HandedByUserId).HasColumnName("handed_by_user_id").HasColumnType("text").IsRequired(true);
        builder.Property(x => x.HandedAt).HasColumnName("handed_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.ConfirmationNote).HasColumnName("confirmation_note").HasColumnType("text").IsRequired(false);

        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.HandedToUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.HandedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.FoundItem).WithMany().HasForeignKey(x => new { x.OperatorId, x.FoundItemId })
            .HasPrincipalKey(x => new { x.OperatorId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Claim).WithMany().HasForeignKey(x => new { x.OperatorId, x.ClaimId })
            .HasPrincipalKey(x => new { x.OperatorId, x.Id }).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.OperatorId, x.FoundItemId }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.ClaimId }).IsUnique();
    }
}
