using Metro.Domain.Entities.Finance;
using Metro.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations.Finance;

public sealed class RefundRequestConfiguration : IEntityTypeConfiguration<RefundRequest>
{
    public void Configure(EntityTypeBuilder<RefundRequest> builder)
    {
        // The existing SQL deployment owns DDL, checks, triggers and retention rules.
        builder.ToTable("refund_requests", "metro", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.OperatorId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.RequestCode).HasColumnName("request_code").HasColumnType("varchar(60)").IsRequired(true).HasMaxLength(60);
        builder.Property(x => x.TicketId).HasColumnName("ticket_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.PaymentId).HasColumnName("payment_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.IncidentId).HasColumnName("incident_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.ReasonType).HasColumnName("reason_type").HasColumnType("varchar(60)").IsRequired(true).HasMaxLength(60);
        builder.Property(x => x.RequestedAmountMinor).HasColumnName("requested_amount_minor").HasColumnType("bigint").IsRequired(true);
        builder.Property(x => x.ApprovedAmountMinor).HasColumnName("approved_amount_minor").HasColumnType("bigint").IsRequired(false);
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<RefundRequestStatus>()).HasDefaultValueSql("'SUBMITTED'");
        builder.Property(x => x.SubmittedByUserId).HasColumnName("submitted_by_user_id").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.ApprovedByUserId).HasColumnName("approved_by_user_id").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.ReviewedAt).HasColumnName("reviewed_at").HasColumnType("timestamptz").IsRequired(false);
        builder.Property(x => x.ApprovedAt).HasColumnName("approved_at").HasColumnType("timestamptz").IsRequired(false);
        builder.Property(x => x.DecisionReason).HasColumnName("decision_reason").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");

        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.SubmittedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.ReviewedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.ApprovedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Tickets.Ticket>().WithMany()
            .HasForeignKey("OperatorId", "TicketId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Payment).WithMany().HasForeignKey(x => new { x.OperatorId, x.PaymentId })
            .HasPrincipalKey(x => new { x.OperatorId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(ExternalReferences.Incidents, null).WithMany()
            .HasForeignKey("OperatorId", "IncidentId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.OperatorId, x.RequestCode }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.CorrelationId }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.TicketId }).HasDatabaseName("uq_active_refund_per_ticket").IsUnique().HasFilter("status IN ('SUBMITTED','UNDER_REVIEW','NEEDS_INFO','PENDING_APPROVAL','APPROVED','PROCESSING')");
        builder.HasIndex(x => new { x.OperatorId, x.PaymentId, x.Status }).HasDatabaseName("ix_refunds_payment");
    }
}
