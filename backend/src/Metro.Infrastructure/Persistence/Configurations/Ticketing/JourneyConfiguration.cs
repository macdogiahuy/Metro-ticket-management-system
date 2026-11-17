using Metro.Domain.Entities.Ticketing;
using Metro.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations.Ticketing;

public sealed class JourneyConfiguration : IEntityTypeConfiguration<Journey>
{
    public void Configure(EntityTypeBuilder<Journey> builder)
    {
        // The existing SQL deployment owns DDL, checks, triggers and retention rules.
        builder.ToTable("journeys", "metro", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.OperatorId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.JourneyCode).HasColumnName("journey_code").HasColumnType("varchar(80)").IsRequired(true).HasMaxLength(80);
        builder.Property(x => x.TicketId).HasColumnName("ticket_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.PlannedOriginStationId).HasColumnName("planned_origin_station_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.PlannedDestinationStationId).HasColumnName("planned_destination_station_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.ActualEntryStationId).HasColumnName("actual_entry_station_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.ActualExitStationId).HasColumnName("actual_exit_station_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<JourneyStatus>()).HasDefaultValueSql("'OPEN'");
        builder.Property(x => x.EnteredAt).HasColumnName("entered_at").HasColumnType("timestamptz").IsRequired(false);
        builder.Property(x => x.ExitedAt).HasColumnName("exited_at").HasColumnType("timestamptz").IsRequired(false);
        builder.Property(x => x.SupplementalFareMinor).HasColumnName("supplemental_fare_minor").HasColumnType("bigint").IsRequired(true).HasDefaultValueSql("0");
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.ServiceDate).HasColumnName("service_date").HasColumnType("date").IsRequired(false);
        builder.Property(x => x.EntryPlannedTripId).HasColumnName("entry_planned_trip_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.ExitPlannedTripId).HasColumnName("exit_planned_trip_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.EntryDirection).HasColumnName("entry_direction").HasColumnType("smallint").IsRequired(false);
        builder.Property(x => x.PairedJourneyId).HasColumnName("paired_journey_id").HasColumnType("uuid").IsRequired(false);

        builder.HasOne<Metro.Domain.Entities.Tickets.Ticket>().WithMany()
            .HasForeignKey("OperatorId", "TicketId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.Station>().WithMany()
            .HasForeignKey("OperatorId", "PlannedOriginStationId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.Station>().WithMany()
            .HasForeignKey("OperatorId", "PlannedDestinationStationId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.Station>().WithMany()
            .HasForeignKey("OperatorId", "ActualEntryStationId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.Station>().WithMany()
            .HasForeignKey("OperatorId", "ActualExitStationId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.PlannedTrip>().WithMany()
            .HasForeignKey("OperatorId", "EntryPlannedTripId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.PlannedTrip>().WithMany()
            .HasForeignKey("OperatorId", "ExitPlannedTripId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.PairedJourney).WithMany().HasForeignKey(x => new { x.OperatorId, x.PairedJourneyId })
            .HasPrincipalKey(x => new { x.OperatorId, x.Id }).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.OperatorId, x.JourneyCode }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.CorrelationId }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.TicketId }).HasDatabaseName("uq_ticket_open_journey").IsUnique().HasFilter("status = 'OPEN'");
    }
}
