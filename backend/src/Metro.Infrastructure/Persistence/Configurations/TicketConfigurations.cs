using Metro.Domain.Entities.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations;

public class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.TicketCode).HasColumnName("ticket_code").HasMaxLength(80).IsRequired();
        builder.Property(e => e.OrderItemId).HasColumnName("order_item_id").IsRequired();
        builder.Property(e => e.TicketProductId).HasColumnName("ticket_product_id").IsRequired();
        builder.Property(e => e.HolderUserId).HasColumnName("holder_user_id");
        builder.Property(e => e.SequenceNo).HasColumnName("sequence_no").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasDefaultValue("ISSUED").IsRequired();
        // Migration 006 fills missing bounds/context BEFORE INSERT; read them back via RETURNING.
        builder.Property(e => e.EffectiveFrom).HasColumnName("effective_from").ValueGeneratedOnAdd();
        builder.Property(e => e.ExpiresAt).HasColumnName("expires_at").ValueGeneratedOnAdd();
        builder.Property(e => e.LatestActivationAt).HasColumnName("latest_activation_at");
        builder.Property(e => e.FirstUsedAt).HasColumnName("first_used_at");
        builder.Property(e => e.RevokedAt).HasColumnName("revoked_at");
        builder.Property(e => e.RevocationReason).HasColumnName("revocation_reason");
        builder.Property(e => e.FarePolicyVersionId).HasColumnName("fare_policy_version_id").IsRequired();
        builder.Property(e => e.CorrelationId).HasColumnName("correlation_id").HasDefaultValueSql("gen_random_uuid()").IsRequired();
        builder.Property(e => e.IssuedAt).HasColumnName("issued_at").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("clock_timestamp()").IsRequired();

        // SRS v1.2 / Migration 006 fields
        builder.Property(e => e.OrderId).HasColumnName("order_id").ValueGeneratedOnAdd();
        builder.Property(e => e.OrderVersion).HasColumnName("order_version").ValueGeneratedOnAdd();
        builder.Property(e => e.ServiceDate).HasColumnName("service_date").ValueGeneratedOnAdd();
        builder.Property(e => e.OriginStationId).HasColumnName("origin_station_id").ValueGeneratedOnAdd();
        builder.Property(e => e.DestinationStationId).HasColumnName("destination_station_id").ValueGeneratedOnAdd();
        builder.Property(e => e.RelatedTicketId).HasColumnName("related_ticket_id");
        builder.Property(e => e.RelationType).HasColumnName("relation_type");
        builder.Property(e => e.GuardianLinkId).HasColumnName("guardian_link_id");
        builder.Property(e => e.GuardianUserId).HasColumnName("guardian_user_id");
        builder.Property(e => e.ChangeCount).HasColumnName("change_count").HasDefaultValue((short)0).IsRequired();

        builder.HasAlternateKey(e => new { e.OperatorId, e.Id });
        builder.HasIndex(e => new { e.OperatorId, e.TicketCode }).IsUnique();
        builder.HasIndex(e => new { e.OperatorId, e.OrderItemId, e.SequenceNo }).IsUnique();
        builder.HasIndex(e => new { e.OperatorId, e.CorrelationId }).IsUnique();
        builder.HasIndex(e => new { e.OperatorId, e.ServiceDate, e.Status });

        builder.HasOne(e => e.OrderItem)
            .WithMany(oi => oi.Tickets)
            .HasForeignKey(e => new { e.OperatorId, e.OrderItemId })
            .HasPrincipalKey(oi => new { oi.OperatorId, oi.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.TicketProduct)
            .WithMany()
            .HasForeignKey(e => new { e.OperatorId, e.TicketProductId })
            .HasPrincipalKey(p => new { p.OperatorId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.FarePolicyVersion)
            .WithMany()
            .HasForeignKey(e => new { e.OperatorId, e.FarePolicyVersionId })
            .HasPrincipalKey(f => new { f.OperatorId, f.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Order)
            .WithMany()
            .HasForeignKey(e => new { e.OperatorId, e.OrderId })
            .HasPrincipalKey(o => new { o.OperatorId, o.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.OriginStation)
            .WithMany()
            .HasForeignKey(e => new { e.OperatorId, e.OriginStationId })
            .HasPrincipalKey(s => new { s.OperatorId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.DestinationStation)
            .WithMany()
            .HasForeignKey(e => new { e.OperatorId, e.DestinationStationId })
            .HasPrincipalKey(s => new { s.OperatorId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.RelatedTicket)
            .WithMany()
            .HasForeignKey(e => new { e.OperatorId, e.RelatedTicketId })
            .HasPrincipalKey(t => new { t.OperatorId, t.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class TicketCredentialConfiguration : IEntityTypeConfiguration<TicketCredential>
{
    public void Configure(EntityTypeBuilder<TicketCredential> builder)
    {
        builder.ToTable("ticket_credentials", "metro", table => table.ExcludeFromMigrations());
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.TicketId).HasColumnName("ticket_id").IsRequired();
        builder.Property(e => e.CredentialType).HasColumnName("credential_type").IsRequired();
        builder.Property(e => e.TokenHash).HasColumnName("token_hash").HasColumnType("char(64)").HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(e => e.TokenLast4).HasColumnName("token_last4").HasMaxLength(4);
        builder.Property(e => e.Status).HasColumnName("status").HasDefaultValue("ACTIVE").IsRequired();
        builder.Property(e => e.IssuedAt).HasColumnName("issued_at").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.RevokedAt).HasColumnName("revoked_at");
        builder.Property(e => e.Version).HasColumnName("version").HasDefaultValue(1).IsRequired();
        builder.Property(e => e.KeyId).HasColumnName("key_id");
        builder.Property(e => e.ReplacedById).HasColumnName("replaced_by_id");

        builder.HasAlternateKey(e => new { e.OperatorId, e.Id });
        builder.HasIndex(e => new { e.OperatorId, e.TokenHash }).IsUnique();
        builder.HasIndex(e => new { e.OperatorId, e.TicketId, e.Version }).HasDatabaseName("uq_signed_qr_version")
            .IsUnique().HasFilter("credential_type='QR' AND key_id IS NOT NULL");
        builder.HasIndex(e => new { e.OperatorId, e.TicketId }).HasDatabaseName("uq_current_signed_qr")
            .IsUnique().HasFilter("credential_type='QR' AND status='ACTIVE' AND key_id IS NOT NULL");

        builder.HasOne(e => e.Ticket)
            .WithMany(t => t.Credentials)
            .HasForeignKey(e => new { e.OperatorId, e.TicketId })
            .HasPrincipalKey(t => new { t.OperatorId, t.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(e => e.ReplacedBy)
            .WithMany()
            .HasForeignKey(e => new { e.OperatorId, e.ReplacedById })
            .HasPrincipalKey(c => new { c.OperatorId, c.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
