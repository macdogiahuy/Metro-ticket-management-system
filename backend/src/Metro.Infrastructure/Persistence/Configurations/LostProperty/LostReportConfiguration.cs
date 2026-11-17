using Metro.Domain.Entities.LostProperty;
using Metro.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations.LostProperty;

public sealed class LostReportConfiguration : IEntityTypeConfiguration<LostReport>
{
    public void Configure(EntityTypeBuilder<LostReport> builder)
    {
        // The existing SQL deployment owns DDL, checks, triggers and retention rules.
        builder.ToTable("lost_reports", "metro", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.OperatorId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.ReportCode).HasColumnName("report_code").HasColumnType("varchar(60)").IsRequired(true).HasMaxLength(60);
        builder.Property(x => x.SubmittedByUserId).HasColumnName("submitted_by_user_id").HasColumnType("text").IsRequired(true);
        builder.Property(x => x.OwnerUserId).HasColumnName("owner_user_id").HasColumnType("text").IsRequired(true);
        builder.Property(x => x.ItemCategory).HasColumnName("item_category").HasColumnType("varchar(60)").IsRequired(true).HasMaxLength(60);
        builder.Property(x => x.Color).HasColumnName("color").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.PublicDescription).HasColumnName("public_description").HasColumnType("text").IsRequired(true);
        builder.Property(x => x.SecretCharacteristics).HasColumnName("secret_characteristics").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.LostFrom).HasColumnName("lost_from").HasColumnType("timestamptz").IsRequired(true);
        builder.Property(x => x.LostTo).HasColumnName("lost_to").HasColumnType("timestamptz").IsRequired(false);
        builder.Property(x => x.StationId).HasColumnName("station_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.LineId).HasColumnName("line_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.PlannedTripId).HasColumnName("planned_trip_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.CarId).HasColumnName("car_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.AttachmentReference).HasColumnName("attachment_reference").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<LostReportStatus>()).HasDefaultValueSql("'OPEN'");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");

        builder.HasOne<Metro.Domain.Entities.Identity.Operator>().WithMany()
            .HasForeignKey("OperatorId")
            .HasPrincipalKey("Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.SubmittedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.Station>().WithMany()
            .HasForeignKey("OperatorId", "StationId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.Line>().WithMany()
            .HasForeignKey("OperatorId", "LineId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.PlannedTrip>().WithMany()
            .HasForeignKey("OperatorId", "PlannedTripId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(ExternalReferences.Cars, null).WithMany()
            .HasForeignKey("OperatorId", "CarId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.OperatorId, x.ReportCode }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.OwnerUserId, x.CreatedAt }).HasDatabaseName("ix_lost_reports_owner").IsDescending(false, false, true);
        builder.HasIndex(x => new { x.OperatorId, x.Status, x.ItemCategory, x.LostFrom }).HasDatabaseName("ix_lost_reports_filter").IsDescending(false, false, false, true);
    }
}
