using Metro.Domain.Entities.LostProperty;
using Metro.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations.LostProperty;

public sealed class FoundItemConfiguration : IEntityTypeConfiguration<FoundItem>
{
    public void Configure(EntityTypeBuilder<FoundItem> builder)
    {
        // The existing SQL deployment owns DDL, checks, triggers and retention rules.
        builder.ToTable("found_items", "metro", table => table.ExcludeFromMigrations());
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.OperatorId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").IsRequired(true).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.ItemCode).HasColumnName("item_code").HasColumnType("varchar(60)").IsRequired(true).HasMaxLength(60);
        builder.Property(x => x.ReceivedByUserId).HasColumnName("received_by_user_id").HasColumnType("text").IsRequired(true);
        builder.Property(x => x.ReceivedAt).HasColumnName("received_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.HoldingStationId).HasColumnName("holding_station_id").HasColumnType("uuid").IsRequired(true);
        builder.Property(x => x.FoundAt).HasColumnName("found_at").HasColumnType("timestamptz").IsRequired(false);
        builder.Property(x => x.PlannedTripId).HasColumnName("planned_trip_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.CarId).HasColumnName("car_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.PublicDescription).HasColumnName("public_description").HasColumnType("text").IsRequired(true);
        builder.Property(x => x.SecretCharacteristics).HasColumnName("secret_characteristics").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.AttachmentReference).HasColumnName("attachment_reference").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("text").IsRequired(true).HasConversion(new UpperSnakeCaseEnumConverter<FoundItemStatus>()).HasDefaultValueSql("'HELD'");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired(true).HasDefaultValueSql("clock_timestamp()");

        builder.HasOne<Metro.Domain.Entities.Identity.Operator>().WithMany()
            .HasForeignKey("OperatorId")
            .HasPrincipalKey("Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Microsoft.AspNetCore.Identity.IdentityUser>().WithMany().HasForeignKey(x => x.ReceivedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.Station>().WithMany()
            .HasForeignKey("OperatorId", "HoldingStationId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Metro.Domain.Entities.Network.PlannedTrip>().WithMany()
            .HasForeignKey("OperatorId", "PlannedTripId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(ExternalReferences.Cars, null).WithMany()
            .HasForeignKey("OperatorId", "CarId")
            .HasPrincipalKey("OperatorId", "Id").OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.OperatorId, x.ItemCode }).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.Status, x.HoldingStationId, x.ReceivedAt }).HasDatabaseName("ix_found_items_search").IsDescending(false, false, false, true);
    }
}
