using Metro.Domain.Entities.Ticketing;
using Metro.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations.Ticketing;

public sealed class TripSuggestionConfiguration : IEntityTypeConfiguration<TripSuggestion>
{
    public void Configure(EntityTypeBuilder<TripSuggestion> builder)
    {
        // The existing SQL deployment owns DDL, checks, triggers and retention rules.
        builder.ToTable("trip_suggestions", "metro", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.OperatorId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.OrderId).HasColumnName("order_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.OrderVersion).HasColumnName("order_version").HasColumnType("integer").IsRequired(true);
        builder.Property(x => x.SuggestionCode).HasColumnName("suggestion_code").HasColumnType("varchar(60)").IsRequired(true).HasMaxLength(60);
        builder.Property(x => x.BeneficiaryUserId).HasColumnName("beneficiary_user_id").HasColumnType("text").IsRequired(true);
        builder.Property(x => x.ServiceDate).HasColumnName("service_date").HasColumnType("date").IsRequired(true);
        builder.Property(x => x.OriginStationId).HasColumnName("origin_station_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.DestinationStationId).HasColumnName("destination_station_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.LineId).HasColumnName("line_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.Direction).HasColumnName("direction").HasColumnType("smallint").IsRequired(false);
        builder.Property(x => x.SuggestedPlannedTripId).HasColumnName("suggested_planned_trip_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.SuggestedDepartureAt).HasColumnName("suggested_departure_at").HasColumnType("timestamptz").IsRequired(false);
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<TripSuggestionStatus>()).HasDefaultValueSql("'SUGGESTED'");
        builder.Property(x => x.NoTripReason).HasColumnName("no_trip_reason").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.Source).HasColumnName("source").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<TripSuggestionSource>()).HasDefaultValueSql("'SCHEDULE'");
        builder.Property(x => x.ScheduleVersionId).HasColumnName("schedule_version_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.SourceVersionLabel).HasColumnName("source_version_label").HasColumnType("varchar(60)").IsRequired(false).HasMaxLength(60);
        builder.Property(x => x.DataFreshnessSeconds).HasColumnName("data_freshness_seconds").HasColumnType("integer").IsRequired(false);
        builder.Property(x => x.UpdateReason).HasColumnName("update_reason").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.ComputedAt).HasColumnName("computed_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.SupersededAt).HasColumnName("superseded_at").HasColumnType("timestamptz").IsRequired(false);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");

        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.BeneficiaryUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Orders.Order>().WithMany()
            .HasForeignKey("OperatorId", "OrderId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Orders.OrderVersion>().WithMany()
            .HasForeignKey("OperatorId", "OrderId", "OrderVersion")
            .HasPrincipalKey("OperatorId", "OrderId", "Version").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.Station>().WithMany()
            .HasForeignKey("OperatorId", "OriginStationId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.Station>().WithMany()
            .HasForeignKey("OperatorId", "DestinationStationId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.Line>().WithMany()
            .HasForeignKey("OperatorId", "LineId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.PlannedTrip>().WithMany()
            .HasForeignKey("OperatorId", "SuggestedPlannedTripId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.ScheduleVersion>().WithMany()
            .HasForeignKey("OperatorId", "ScheduleVersionId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.OperatorId, x.SuggestionCode }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.CorrelationId }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.OrderId, x.OrderVersion }).HasDatabaseName("uq_trip_suggestions_current").IsUnique().HasFilter("superseded_at IS NULL");
        builder.HasIndex(x => new { x.OperatorId, x.SuggestedPlannedTripId, x.ServiceDate }).HasDatabaseName("ix_trip_suggestions_trip");
    }
}
