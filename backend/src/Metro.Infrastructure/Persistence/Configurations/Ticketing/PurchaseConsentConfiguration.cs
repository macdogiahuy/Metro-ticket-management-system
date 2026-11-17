using Metro.Domain.Entities.Ticketing;
using Metro.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations.Ticketing;

public sealed class PurchaseConsentConfiguration : IEntityTypeConfiguration<PurchaseConsent>
{
    public void Configure(EntityTypeBuilder<PurchaseConsent> builder)
    {
        // The existing SQL deployment owns DDL, checks, triggers and retention rules.
        builder.ToTable("purchase_consents", "metro", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.OperatorId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.OrderId).HasColumnName("order_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.OrderVersion).HasColumnName("order_version").HasColumnType("integer").IsRequired(true).HasDefaultValueSql("1");
        builder.Property(x => x.BuyerUserId).HasColumnName("buyer_user_id").HasColumnType("text").IsRequired(true);
        builder.Property(x => x.BeneficiaryUserId).HasColumnName("beneficiary_user_id").HasColumnType("text").IsRequired(true);
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<PurchaseConsentStatus>()).HasDefaultValueSql("'REQUESTED'");
        builder.Property(x => x.OrderContentHash).HasColumnName("order_content_hash").HasColumnType("char(64)").IsRequired(false).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.RequestedAt).HasColumnName("requested_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamptz").IsRequired(true);
        builder.Property(x => x.RespondedAt).HasColumnName("responded_at").HasColumnType("timestamptz").IsRequired(false);
        builder.Property(x => x.ResponseNote).HasColumnName("response_note").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");

        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.BuyerUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.BeneficiaryUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Orders.Order>().WithMany()
            .HasForeignKey("OperatorId", "OrderId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Orders.OrderVersion>().WithMany()
            .HasForeignKey("OperatorId", "OrderId", "OrderVersion")
            .HasPrincipalKey("OperatorId", "OrderId", "Version").OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.OperatorId, x.CorrelationId }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.OrderId }).HasDatabaseName("uq_purchase_consents_pending").IsUnique().HasFilter("status = 'REQUESTED'");
    }
}
