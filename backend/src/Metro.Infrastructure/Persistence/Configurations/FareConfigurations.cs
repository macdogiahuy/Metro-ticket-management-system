using Metro.Domain.Entities.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations;

public class FareMatrixEntryConfiguration : IEntityTypeConfiguration<FareMatrixEntry>
{
    public void Configure(EntityTypeBuilder<FareMatrixEntry> builder)
    {
        builder.ToTable("fare_matrix_entries", "metro", t => t.ExcludeFromMigrations());
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.FarePolicyVersionId).HasColumnName("fare_policy_version_id").IsRequired();
        builder.Property(e => e.TicketProductId).HasColumnName("ticket_product_id").IsRequired();
        builder.Property(e => e.LineId).HasColumnName("line_id");
        builder.Property(e => e.OriginStationId).HasColumnName("origin_station_id");
        builder.Property(e => e.DestinationStationId).HasColumnName("destination_station_id");
        builder.Property(e => e.PaymentChannel).HasColumnName("payment_channel").IsRequired();
        builder.Property(e => e.EligibilityGroup).HasColumnName("eligibility_group").HasMaxLength(50).HasDefaultValue("ORDINARY").IsRequired();
        builder.Property(e => e.AmountMinor).HasColumnName("amount_minor").IsRequired();
        builder.Property(e => e.Sellable).HasColumnName("sellable").HasDefaultValue(true).IsRequired();

        // NULL-safe natural key matching metro.fare_matrix_entries UNIQUE NULLS NOT DISTINCT.
        builder.HasIndex(e => new { e.OperatorId, e.FarePolicyVersionId, e.TicketProductId, e.LineId, e.OriginStationId, e.DestinationStationId, e.PaymentChannel, e.EligibilityGroup })
            .IsUnique().AreNullsDistinct(false);

        builder.HasOne(e => e.FarePolicyVersion).WithMany(f => f.MatrixEntries)
            .HasForeignKey(e => new { e.OperatorId, e.FarePolicyVersionId }).HasPrincipalKey(f => new { f.OperatorId, f.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.TicketProduct).WithMany()
            .HasForeignKey(e => new { e.OperatorId, e.TicketProductId }).HasPrincipalKey(p => new { p.OperatorId, p.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Line).WithMany()
            .HasForeignKey(e => new { e.OperatorId, e.LineId }).HasPrincipalKey(l => new { l.OperatorId, l.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.OriginStation).WithMany()
            .HasForeignKey(e => new { e.OperatorId, e.OriginStationId }).HasPrincipalKey(s => new { s.OperatorId, s.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.DestinationStation).WithMany()
            .HasForeignKey(e => new { e.OperatorId, e.DestinationStationId }).HasPrincipalKey(s => new { s.OperatorId, s.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public class EligibilityPolicyConfiguration : IEntityTypeConfiguration<EligibilityPolicy>
{
    public void Configure(EntityTypeBuilder<EligibilityPolicy> builder)
    {
        builder.ToTable("eligibility_policies", "metro", t => t.ExcludeFromMigrations());
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();
        builder.Property(e => e.BeneficiaryGroup).HasColumnName("beneficiary_group").HasMaxLength(50).IsRequired();
        builder.Property(e => e.DiscountType).HasColumnName("discount_type").IsRequired();
        builder.Property(e => e.DiscountValue).HasColumnName("discount_value").HasPrecision(12, 4).IsRequired();
        builder.Property(e => e.EligibleProductCodes).HasColumnName("eligible_product_codes").HasDefaultValueSql("'{}'").IsRequired();
        builder.Property(e => e.RequiredEvidenceTypes).HasColumnName("required_evidence_types").HasDefaultValueSql("'{}'").IsRequired();
        builder.Property(e => e.EffectiveFrom).HasColumnName("effective_from").IsRequired();
        builder.Property(e => e.EffectiveTo).HasColumnName("effective_to");
        builder.Property(e => e.Status).HasColumnName("status").HasDefaultValue("DRAFT").IsRequired();
        builder.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(e => e.ApprovedByUserId).HasColumnName("approved_by_user_id");
        builder.Property(e => e.ApprovedAt).HasColumnName("approved_at");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("clock_timestamp()").IsRequired();

        builder.HasAlternateKey(e => new { e.OperatorId, e.Id });
        builder.HasIndex(e => new { e.OperatorId, e.Code, e.Version }).IsUnique();
        builder.HasOne(e => e.Operator).WithMany().HasForeignKey(e => e.OperatorId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class EligibilityApplicationConfiguration : IEntityTypeConfiguration<EligibilityApplication>
{
    public void Configure(EntityTypeBuilder<EligibilityApplication> builder)
    {
        builder.ToTable("eligibility_applications", "metro", t => t.ExcludeFromMigrations());
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.ApplicationCode).HasColumnName("application_code").HasMaxLength(60).IsRequired();
        builder.Property(e => e.ApplicantUserId).HasColumnName("applicant_user_id").IsRequired();
        builder.Property(e => e.EligibilityPolicyId).HasColumnName("eligibility_policy_id").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasDefaultValue("DRAFT").IsRequired();
        builder.Property(e => e.SubmittedAt).HasColumnName("submitted_at");
        builder.Property(e => e.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
        builder.Property(e => e.DecidedAt).HasColumnName("decided_at");
        builder.Property(e => e.ValidFrom).HasColumnName("valid_from");
        builder.Property(e => e.ValidTo).HasColumnName("valid_to");
        builder.Property(e => e.DecisionReason).HasColumnName("decision_reason");
        builder.Property(e => e.EvidencePurgedAt).HasColumnName("evidence_purged_at");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("clock_timestamp()").IsRequired();

        builder.HasAlternateKey(e => new { e.OperatorId, e.Id });
        builder.HasIndex(e => new { e.OperatorId, e.ApplicationCode }).IsUnique();
        builder.HasOne(e => e.EligibilityPolicy).WithMany(p => p.Applications)
            .HasForeignKey(e => new { e.OperatorId, e.EligibilityPolicyId }).HasPrincipalKey(p => new { p.OperatorId, p.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
