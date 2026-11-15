using Metro.Domain.Entities.Network;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations;

public class LineConfiguration : IEntityTypeConfiguration<Line>
{
    public void Configure(EntityTypeBuilder<Line> builder)
    {
        builder.ToTable("lines", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasDefaultValue("ACTIVE").IsRequired();
        builder.Property(e => e.OpenedOn).HasColumnName("opened_on");
        builder.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(e => e.UpdatedByUserId).HasColumnName("updated_by_user_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("clock_timestamp()").IsRequired();

        builder.HasAlternateKey(e => new { e.OperatorId, e.Id });
        builder.HasIndex(e => new { e.OperatorId, e.Code }).IsUnique();

        builder.HasOne(e => e.Operator)
            .WithMany()
            .HasForeignKey(e => e.OperatorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class StationConfiguration : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> builder)
    {
        builder.ToTable("stations", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").IsRequired();
        builder.Property(e => e.StationType).HasColumnName("station_type").HasDefaultValue("ELEVATED").IsRequired();
        builder.Property(e => e.Latitude).HasColumnName("latitude").HasPrecision(9, 6);
        builder.Property(e => e.Longitude).HasColumnName("longitude").HasPrecision(9, 6);
        builder.Property(e => e.Status).HasColumnName("status").HasDefaultValue("ACTIVE").IsRequired();
        builder.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(e => e.UpdatedByUserId).HasColumnName("updated_by_user_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("clock_timestamp()").IsRequired();

        builder.HasAlternateKey(e => new { e.OperatorId, e.Id });
        builder.HasIndex(e => new { e.OperatorId, e.Code }).IsUnique();

        builder.HasOne(e => e.Operator)
            .WithMany()
            .HasForeignKey(e => e.OperatorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class LineStationConfiguration : IEntityTypeConfiguration<LineStation>
{
    public void Configure(EntityTypeBuilder<LineStation> builder)
    {
        builder.ToTable("line_stations", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.LineId).HasColumnName("line_id").IsRequired();
        builder.Property(e => e.StationId).HasColumnName("station_id").IsRequired();
        builder.Property(e => e.Direction).HasColumnName("direction").IsRequired();
        builder.Property(e => e.SequenceNo).HasColumnName("sequence_no").IsRequired();
        builder.Property(e => e.DistanceFromOriginM).HasColumnName("distance_from_origin_m").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("clock_timestamp()").IsRequired();

        builder.HasOne(e => e.Line)
            .WithMany(l => l.LineStations)
            .HasForeignKey(e => new { e.OperatorId, e.LineId })
            .HasPrincipalKey(l => new { l.OperatorId, l.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Station)
            .WithMany(s => s.LineStations)
            .HasForeignKey(e => new { e.OperatorId, e.StationId })
            .HasPrincipalKey(s => new { s.OperatorId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.OperatorId, e.LineId, e.Direction, e.SequenceNo }).IsUnique();
        builder.HasIndex(e => new { e.OperatorId, e.LineId, e.Direction, e.StationId }).IsUnique();
    }
}

public class ServiceCalendarConfiguration : IEntityTypeConfiguration<ServiceCalendar>
{
    public void Configure(EntityTypeBuilder<ServiceCalendar> builder)
    {
        builder.ToTable("service_calendars", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").IsRequired();
        builder.Property(e => e.DayType).HasColumnName("day_type").IsRequired();
        builder.Property(e => e.DaysOfWeek).HasColumnName("days_of_week").HasDefaultValueSql("'{}'").IsRequired();
        builder.Property(e => e.EffectiveFrom).HasColumnName("effective_from").IsRequired();
        builder.Property(e => e.EffectiveTo).HasColumnName("effective_to");
        builder.Property(e => e.Priority).HasColumnName("priority").HasDefaultValue((short)0).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasDefaultValue("DRAFT").IsRequired();
        builder.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(e => e.ApprovedByUserId).HasColumnName("approved_by_user_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("clock_timestamp()").IsRequired();

        builder.HasAlternateKey(e => new { e.OperatorId, e.Id });
        builder.HasIndex(e => new { e.OperatorId, e.Code }).IsUnique();

        builder.HasOne(e => e.Operator)
            .WithMany()
            .HasForeignKey(e => e.OperatorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ScheduleVersionConfiguration : IEntityTypeConfiguration<ScheduleVersion>
{
    public void Configure(EntityTypeBuilder<ScheduleVersion> builder)
    {
        builder.ToTable("schedule_versions", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.LineId).HasColumnName("line_id").IsRequired();
        builder.Property(e => e.ServiceCalendarId).HasColumnName("service_calendar_id").IsRequired();
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();
        builder.Property(e => e.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasDefaultValue("DRAFT").IsRequired();
        builder.Property(e => e.EffectiveFrom).HasColumnName("effective_from").IsRequired();
        builder.Property(e => e.EffectiveTo).HasColumnName("effective_to");
        builder.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(e => e.ApprovedByUserId).HasColumnName("approved_by_user_id");
        builder.Property(e => e.ApprovedAt).HasColumnName("approved_at");
        builder.Property(e => e.PublishedByUserId).HasColumnName("published_by_user_id");
        builder.Property(e => e.PublishedAt).HasColumnName("published_at");
        builder.Property(e => e.DecisionReason).HasColumnName("decision_reason");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("clock_timestamp()").IsRequired();

        builder.HasAlternateKey(e => new { e.OperatorId, e.Id });

        builder.HasOne(e => e.Operator)
            .WithMany()
            .HasForeignKey(e => e.OperatorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Line)
            .WithMany(l => l.ScheduleVersions)
            .HasForeignKey(e => new { e.OperatorId, e.LineId })
            .HasPrincipalKey(l => new { l.OperatorId, l.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ServiceCalendar)
            .WithMany(s => s.ScheduleVersions)
            .HasForeignKey(e => new { e.OperatorId, e.ServiceCalendarId })
            .HasPrincipalKey(s => new { s.OperatorId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.OperatorId, e.Code }).IsUnique();
        builder.HasIndex(e => new { e.OperatorId, e.LineId, e.ServiceCalendarId, e.Version }).IsUnique();
    }
}

public class PlannedTripConfiguration : IEntityTypeConfiguration<PlannedTrip>
{
    public void Configure(EntityTypeBuilder<PlannedTrip> builder)
    {
        builder.ToTable("planned_trips", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.ScheduleVersionId).HasColumnName("schedule_version_id").IsRequired();
        builder.Property(e => e.LineId).HasColumnName("line_id").IsRequired();
        builder.Property(e => e.TripCode).HasColumnName("trip_code").HasMaxLength(60).IsRequired();
        builder.Property(e => e.ServiceDate).HasColumnName("service_date").IsRequired();
        builder.Property(e => e.Direction).HasColumnName("direction").IsRequired();
        builder.Property(e => e.PlannedStartAt).HasColumnName("planned_start_at").IsRequired();
        builder.Property(e => e.PlannedEndAt).HasColumnName("planned_end_at").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasDefaultValue("PLANNED").IsRequired();
        builder.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("clock_timestamp()").IsRequired();

        builder.HasAlternateKey(e => new { e.OperatorId, e.Id });

        builder.HasOne(e => e.ScheduleVersion)
            .WithMany(s => s.PlannedTrips)
            .HasForeignKey(e => new { e.OperatorId, e.ScheduleVersionId })
            .HasPrincipalKey(s => new { s.OperatorId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Line)
            .WithMany(l => l.PlannedTrips)
            .HasForeignKey(e => new { e.OperatorId, e.LineId })
            .HasPrincipalKey(l => new { l.OperatorId, l.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.OperatorId, e.ServiceDate, e.TripCode }).IsUnique();
    }
}

public class PlannedStopTimeConfiguration : IEntityTypeConfiguration<PlannedStopTime>
{
    public void Configure(EntityTypeBuilder<PlannedStopTime> builder)
    {
        builder.ToTable("planned_stop_times", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.PlannedTripId).HasColumnName("planned_trip_id").IsRequired();
        builder.Property(e => e.StationId).HasColumnName("station_id").IsRequired();
        builder.Property(e => e.SequenceNo).HasColumnName("sequence_no").IsRequired();
        builder.Property(e => e.PlannedArrivalAt).HasColumnName("planned_arrival_at").IsRequired();
        builder.Property(e => e.PlannedDepartureAt).HasColumnName("planned_departure_at").IsRequired();
        builder.Property(e => e.DwellSeconds).HasColumnName("dwell_seconds").HasDefaultValue(0).IsRequired();

        builder.HasOne(e => e.PlannedTrip)
            .WithMany(p => p.StopTimes)
            .HasForeignKey(e => new { e.OperatorId, e.PlannedTripId })
            .HasPrincipalKey(p => new { p.OperatorId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Station)
            .WithMany(s => s.PlannedStopTimes)
            .HasForeignKey(e => new { e.OperatorId, e.StationId })
            .HasPrincipalKey(s => new { s.OperatorId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.OperatorId, e.PlannedTripId, e.SequenceNo }).IsUnique();
        builder.HasIndex(e => new { e.OperatorId, e.PlannedTripId, e.StationId }).IsUnique();
    }
}

public class ActualTripConfiguration : IEntityTypeConfiguration<ActualTrip>
{
    public void Configure(EntityTypeBuilder<ActualTrip> builder)
    {
        builder.ToTable("actual_trips", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.PlannedTripId).HasColumnName("planned_trip_id").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").IsRequired();
        builder.Property(e => e.ActualStartAt).HasColumnName("actual_start_at");
        builder.Property(e => e.ActualEndAt).HasColumnName("actual_end_at");
        builder.Property(e => e.Source).HasColumnName("source").IsRequired();
        builder.Property(e => e.SourceReference).HasColumnName("source_reference");
        builder.Property(e => e.DelaySeconds).HasColumnName("delay_seconds");
        builder.Property(e => e.Reason).HasColumnName("reason");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("clock_timestamp()").IsRequired();

        builder.HasAlternateKey(e => new { e.OperatorId, e.Id });
        builder.HasIndex(e => new { e.OperatorId, e.PlannedTripId }).IsUnique();

        builder.HasOne(e => e.PlannedTrip)
            .WithOne(p => p.ActualTrip)
            .HasForeignKey<ActualTrip>(e => new { e.OperatorId, e.PlannedTripId })
            .HasPrincipalKey<PlannedTrip>(p => new { p.OperatorId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ActualStopTimeConfiguration : IEntityTypeConfiguration<ActualStopTime>
{
    public void Configure(EntityTypeBuilder<ActualStopTime> builder)
    {
        builder.ToTable("actual_stop_times", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.ActualTripId).HasColumnName("actual_trip_id").IsRequired();
        builder.Property(e => e.StationId).HasColumnName("station_id").IsRequired();
        builder.Property(e => e.SequenceNo).HasColumnName("sequence_no").IsRequired();
        builder.Property(e => e.ActualArrivalAt).HasColumnName("actual_arrival_at");
        builder.Property(e => e.ActualDepartureAt).HasColumnName("actual_departure_at");
        builder.Property(e => e.DwellSeconds).HasColumnName("dwell_seconds");
        builder.Property(e => e.Irregularity).HasColumnName("irregularity");

        builder.HasOne(e => e.ActualTrip)
            .WithMany(a => a.StopTimes)
            .HasForeignKey(e => new { e.OperatorId, e.ActualTripId })
            .HasPrincipalKey(a => new { a.OperatorId, a.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Station)
            .WithMany()
            .HasForeignKey(e => new { e.OperatorId, e.StationId })
            .HasPrincipalKey(s => new { s.OperatorId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
