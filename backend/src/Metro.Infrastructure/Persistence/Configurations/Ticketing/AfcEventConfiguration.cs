using Metro.Domain.Entities.Ticketing;
using Metro.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations.Ticketing;

public sealed class AfcEventConfiguration : IEntityTypeConfiguration<AfcEvent>
{
    public void Configure(EntityTypeBuilder<AfcEvent> builder)
    {
        // The existing SQL deployment owns DDL, checks, triggers and retention rules.
        builder.ToTable("afc_events", "metro", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.OperatorId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.EventCode).HasColumnName("event_code").HasColumnType("varchar(100)").IsRequired(true).HasMaxLength(100);
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasColumnType("varchar(120)").IsRequired(true).HasMaxLength(120);
        builder.Property(x => x.JourneyId).HasColumnName("journey_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.TicketId).HasColumnName("ticket_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.CredentialId).HasColumnName("credential_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.EventType).HasColumnName("event_type").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<AfcEventType>());
        builder.Property(x => x.StationId).HasColumnName("station_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.DeviceCode).HasColumnName("device_code").HasColumnType("varchar(80)").IsRequired(true).HasMaxLength(80);
        builder.Property(x => x.EventAt).HasColumnName("event_at").HasColumnType("timestamptz").IsRequired(true);
        builder.Property(x => x.ReceivedAt).HasColumnName("received_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.Offline).HasColumnName("offline").HasColumnType("boolean").IsRequired(true).HasDefaultValueSql("false");
        builder.Property(x => x.Decision).HasColumnName("decision").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<AfcEventDecision>());
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasColumnType("varchar(80)").IsRequired(true).HasMaxLength(80);
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.ManualReason).HasColumnName("manual_reason").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.PlannedTripId).HasColumnName("planned_trip_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.Direction).HasColumnName("direction").HasColumnType("smallint").IsRequired(false);

        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.ActorUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Journey).WithMany().HasForeignKey(x => new { x.OperatorId, x.JourneyId })
            .HasPrincipalKey(x => new { x.OperatorId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Tickets.Ticket>().WithMany()
            .HasForeignKey("OperatorId", "TicketId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Credential).WithMany().HasForeignKey(x => new { x.OperatorId, x.CredentialId })
            .HasPrincipalKey(x => new { x.OperatorId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.Station>().WithMany()
            .HasForeignKey("OperatorId", "StationId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.PlannedTrip>().WithMany()
            .HasForeignKey("OperatorId", "PlannedTripId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.OperatorId, x.EventCode }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.CorrelationId }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.TicketId, x.EventAt }).HasDatabaseName("ix_afc_ticket_time").IsDescending(false, false, true);
    }
}
