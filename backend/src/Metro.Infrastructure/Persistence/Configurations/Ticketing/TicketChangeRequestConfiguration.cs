using Metro.Domain.Entities.Ticketing;
using Metro.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations.Ticketing;

public sealed class TicketChangeRequestConfiguration : IEntityTypeConfiguration<TicketChangeRequest>
{
    public void Configure(EntityTypeBuilder<TicketChangeRequest> builder)
    {
        // The existing SQL deployment owns DDL, checks, triggers and retention rules.
        builder.ToTable("ticket_change_requests", "metro", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.OperatorId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.RequestCode).HasColumnName("request_code").HasColumnType("varchar(60)").IsRequired(true).HasMaxLength(60);
        builder.Property(x => x.TicketId).HasColumnName("ticket_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.OrderId).HasColumnName("order_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id").HasColumnType("text").IsRequired(true);
        builder.Property(x => x.ReasonType).HasColumnName("reason_type").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<TicketChangeReasonType>());
        builder.Property(x => x.IncidentId).HasColumnName("incident_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.OriginalServiceDate).HasColumnName("original_service_date").HasColumnType("date").IsRequired(false);
        builder.Property(x => x.RequestedServiceDate).HasColumnName("requested_service_date").HasColumnType("date").IsRequired(false);
        builder.Property(x => x.OriginalOriginStationId).HasColumnName("original_origin_station_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.OriginalDestinationStationId).HasColumnName("original_destination_station_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.NewOriginStationId).HasColumnName("new_origin_station_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.NewDestinationStationId).HasColumnName("new_destination_station_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.OriginalPlannedTripId).HasColumnName("original_planned_trip_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.NewPlannedTripId).HasColumnName("new_planned_trip_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<TicketChangeRequestStatus>()).HasDefaultValueSql("'REQUESTED'");
        builder.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.DecidedAt).HasColumnName("decided_at").HasColumnType("timestamptz").IsRequired(false);
        builder.Property(x => x.DecisionReason).HasColumnName("decision_reason").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.AppliedAt).HasColumnName("applied_at").HasColumnType("timestamptz").IsRequired(false);
        builder.Property(x => x.ChangeSequence).HasColumnName("change_sequence").HasColumnType("smallint").IsRequired(true).HasDefaultValueSql("1");
        builder.Property(x => x.RelatedChildTicketId).HasColumnName("related_child_ticket_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");

        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.RequestedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.DecidedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Tickets.Ticket>().WithMany()
            .HasForeignKey("OperatorId", "TicketId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Orders.Order>().WithMany()
            .HasForeignKey("OperatorId", "OrderId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(ExternalReferences.Incidents, null).WithMany()
            .HasForeignKey("OperatorId", "IncidentId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.Station>().WithMany()
            .HasForeignKey("OperatorId", "OriginalOriginStationId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.Station>().WithMany()
            .HasForeignKey("OperatorId", "OriginalDestinationStationId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.Station>().WithMany()
            .HasForeignKey("OperatorId", "NewOriginStationId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.Station>().WithMany()
            .HasForeignKey("OperatorId", "NewDestinationStationId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.PlannedTrip>().WithMany()
            .HasForeignKey("OperatorId", "OriginalPlannedTripId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.PlannedTrip>().WithMany()
            .HasForeignKey("OperatorId", "NewPlannedTripId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Tickets.Ticket>().WithMany()
            .HasForeignKey("OperatorId", "RelatedChildTicketId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.OperatorId, x.RequestCode }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.CorrelationId }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.TicketId }, "uq_ticket_change_open").HasDatabaseName("uq_ticket_change_open").IsUnique().HasFilter("status IN ('REQUESTED','APPROVED')");
        builder.HasIndex(x => new { x.OperatorId, x.TicketId }, "uq_ticket_change_applied").HasDatabaseName("uq_ticket_change_applied").IsUnique().HasFilter("status = 'APPLIED'");
    }
}
