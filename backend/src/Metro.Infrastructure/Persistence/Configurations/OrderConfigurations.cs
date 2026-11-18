using Metro.Domain.Entities.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations;

public class TicketProductConfiguration : IEntityTypeConfiguration<TicketProduct>
{
    public void Configure(EntityTypeBuilder<TicketProduct> builder)
    {
        builder.ToTable("ticket_products", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").IsRequired();
        builder.Property(e => e.ProductType).HasColumnName("product_type").IsRequired();
        builder.Property(e => e.DurationValue).HasColumnName("duration_value");
        builder.Property(e => e.DurationUnit).HasColumnName("duration_unit");
        builder.Property(e => e.ExpiryRule).HasColumnName("expiry_rule").IsRequired();
        builder.Property(e => e.LatestActivationInterval).HasColumnName("latest_activation_interval");
        builder.Property(e => e.RequiresNamedHolder).HasColumnName("requires_named_holder").HasDefaultValue(false).IsRequired();
        builder.Property(e => e.CapacityControlled).HasColumnName("capacity_controlled").HasDefaultValue(false).IsRequired();
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

public class FarePolicyVersionConfiguration : IEntityTypeConfiguration<FarePolicyVersion>
{
    public void Configure(EntityTypeBuilder<FarePolicyVersion> builder)
    {
        builder.ToTable("fare_policy_versions", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();
        builder.Property(e => e.ScopeKey).HasColumnName("scope_key").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasDefaultValue("DRAFT").IsRequired();
        builder.Property(e => e.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("VND").IsRequired();
        builder.Property(e => e.EffectiveFrom).HasColumnName("effective_from").IsRequired();
        builder.Property(e => e.EffectiveTo).HasColumnName("effective_to");
        builder.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(e => e.ApprovedByUserId).HasColumnName("approved_by_user_id");
        builder.Property(e => e.PublishedByUserId).HasColumnName("published_by_user_id");
        builder.Property(e => e.ApprovedAt).HasColumnName("approved_at");
        builder.Property(e => e.PublishedAt).HasColumnName("published_at");
        builder.Property(e => e.DecisionReason).HasColumnName("decision_reason");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("clock_timestamp()").IsRequired();

        builder.HasAlternateKey(e => new { e.OperatorId, e.Id });
        builder.HasIndex(e => new { e.OperatorId, e.Code }).IsUnique();
        builder.HasIndex(e => new { e.OperatorId, e.ScopeKey, e.Version }).IsUnique();

        builder.HasOne(e => e.Operator)
            .WithMany()
            .HasForeignKey(e => e.OperatorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.OrderCode).HasColumnName("order_code").HasMaxLength(60).IsRequired();
        builder.Property(e => e.Channel).HasColumnName("channel").IsRequired();
        builder.Property(e => e.PayerUserId).HasColumnName("payer_user_id");
        builder.Property(e => e.BeneficiaryUserId).HasColumnName("beneficiary_user_id");
        builder.Property(e => e.SellingStationId).HasColumnName("selling_station_id");
        builder.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(e => e.PayerNameSnapshot).HasColumnName("payer_name_snapshot");
        builder.Property(e => e.PayerEmailSnapshot).HasColumnName("payer_email_snapshot");
        builder.Property(e => e.BeneficiaryNameSnapshot).HasColumnName("beneficiary_name_snapshot");
        builder.Property(e => e.Status).HasColumnName("status").HasDefaultValue("DRAFT").IsRequired();
        builder.Property(e => e.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("VND").IsRequired();
        builder.Property(e => e.SubtotalMinor).HasColumnName("subtotal_minor").HasDefaultValue(0L).IsRequired();
        builder.Property(e => e.DiscountMinor).HasColumnName("discount_minor").HasDefaultValue(0L).IsRequired();
        builder.Property(e => e.TotalMinor).HasColumnName("total_minor").HasDefaultValue(0L).IsRequired();
        builder.Property(e => e.FarePolicyVersionId).HasColumnName("fare_policy_version_id").IsRequired();
        builder.Property(e => e.FareSnapshot).HasColumnName("fare_snapshot").HasColumnType("jsonb").HasDefaultValueSql("'{}'").IsRequired();
        builder.Property(e => e.HoldExpiresAt).HasColumnName("hold_expires_at");
        builder.Property(e => e.PaymentStartedAt).HasColumnName("payment_started_at");
        builder.Property(e => e.PaidAt).HasColumnName("paid_at");
        builder.Property(e => e.IssuedAt).HasColumnName("issued_at");
        builder.Property(e => e.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(e => e.CancelledByUserId).HasColumnName("cancelled_by_user_id");
        builder.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
        builder.Property(e => e.ServiceDate).HasColumnName("service_date");
        builder.Property(e => e.FreeConfirmedAt).HasColumnName("free_confirmed_at");
        builder.Property(e => e.OrderVersion).HasColumnName("order_version").HasDefaultValue(1).IsRequired();
        builder.Property(e => e.EligibilityGroup).HasColumnName("eligibility_group").HasMaxLength(50);
        builder.Property(e => e.AgeReferenceDate).HasColumnName("age_reference_date");
        builder.Property(e => e.ConsentId).HasColumnName("consent_id");
        builder.Property(e => e.CorrelationId).HasColumnName("correlation_id").HasDefaultValueSql("gen_random_uuid()").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("clock_timestamp()").IsRequired();

        builder.HasAlternateKey(e => new { e.OperatorId, e.Id });
        builder.HasIndex(e => new { e.OperatorId, e.OrderCode }).IsUnique();
        builder.HasIndex(e => new { e.OperatorId, e.CorrelationId }).IsUnique();

        builder.HasOne(e => e.Operator)
            .WithMany()
            .HasForeignKey(e => e.OperatorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.SellingStation)
            .WithMany()
            .HasForeignKey(e => new { e.OperatorId, e.SellingStationId })
            .HasPrincipalKey(s => new { s.OperatorId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.FarePolicyVersion)
            .WithMany(f => f.Orders)
            .HasForeignKey(e => new { e.OperatorId, e.FarePolicyVersionId })
            .HasPrincipalKey(f => new { f.OperatorId, f.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(e => e.TicketProductId).HasColumnName("ticket_product_id").IsRequired();
        builder.Property(e => e.PlannedTripId).HasColumnName("planned_trip_id");
        builder.Property(e => e.OriginStationId).HasColumnName("origin_station_id");
        builder.Property(e => e.DestinationStationId).HasColumnName("destination_station_id");
        builder.Property(e => e.PassengerCount).HasColumnName("passenger_count").HasDefaultValue(1).IsRequired();
        builder.Property(e => e.UnitPriceMinor).HasColumnName("unit_price_minor").IsRequired();
        builder.Property(e => e.DiscountMinor).HasColumnName("discount_minor").HasDefaultValue(0L).IsRequired();
        builder.Property(e => e.LineTotalMinor).HasColumnName("line_total_minor").IsRequired();
        builder.Property(e => e.EligibilityApplicationId).HasColumnName("eligibility_application_id");
        builder.Property(e => e.ProductSnapshot).HasColumnName("product_snapshot").HasColumnType("jsonb").HasDefaultValueSql("'{}'").IsRequired();
        builder.Property(e => e.FareSnapshot).HasColumnName("fare_snapshot").HasColumnType("jsonb").HasDefaultValueSql("'{}'").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("clock_timestamp()").IsRequired();

        builder.HasAlternateKey(e => new { e.OperatorId, e.Id });

        builder.HasOne(e => e.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(e => new { e.OperatorId, e.OrderId })
            .HasPrincipalKey(o => new { o.OperatorId, o.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.TicketProduct)
            .WithMany(p => p.OrderItems)
            .HasForeignKey(e => new { e.OperatorId, e.TicketProductId })
            .HasPrincipalKey(p => new { p.OperatorId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.PlannedTrip)
            .WithMany()
            .HasForeignKey(e => new { e.OperatorId, e.PlannedTripId })
            .HasPrincipalKey(p => new { p.OperatorId, p.Id })
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
    }
}

public class OrderVersionConfiguration : IEntityTypeConfiguration<OrderVersion>
{
    public void Configure(EntityTypeBuilder<OrderVersion> builder)
    {
        builder.ToTable("order_versions", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(e => e.Version).HasColumnName("version").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasDefaultValue("DRAFT").IsRequired();
        builder.Property(e => e.ServiceDate).HasColumnName("service_date");
        builder.Property(e => e.TicketProductId).HasColumnName("ticket_product_id").IsRequired();
        builder.Property(e => e.OriginStationId).HasColumnName("origin_station_id");
        builder.Property(e => e.DestinationStationId).HasColumnName("destination_station_id");
        builder.Property(e => e.BeneficiaryUserId).HasColumnName("beneficiary_user_id");
        builder.Property(e => e.EligibilityGroup).HasColumnName("eligibility_group").HasMaxLength(50);
        builder.Property(e => e.AgeReferenceDate).HasColumnName("age_reference_date");
        builder.Property(e => e.SubtotalMinor).HasColumnName("subtotal_minor").IsRequired();
        builder.Property(e => e.DiscountMinor).HasColumnName("discount_minor").HasDefaultValue(0L).IsRequired();
        builder.Property(e => e.TotalMinor).HasColumnName("total_minor").IsRequired();
        builder.Property(e => e.FarePolicyVersionId).HasColumnName("fare_policy_version_id").IsRequired();
        builder.Property(e => e.FareSnapshot).HasColumnName("fare_snapshot").HasColumnType("jsonb").HasDefaultValueSql("'{}'").IsRequired();
        builder.Property(e => e.ChangeReason).HasColumnName("change_reason");
        builder.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.ConfirmedAt).HasColumnName("confirmed_at");

        builder.HasAlternateKey(e => new { e.OperatorId, e.Id });
        builder.HasIndex(e => new { e.OperatorId, e.OrderId, e.Version }).IsUnique();

        builder.HasOne(e => e.Order)
            .WithMany(o => o.Versions)
            .HasForeignKey(e => new { e.OperatorId, e.OrderId })
            .HasPrincipalKey(o => new { o.OperatorId, o.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.TicketProduct)
            .WithMany(p => p.OrderVersions)
            .HasForeignKey(e => new { e.OperatorId, e.TicketProductId })
            .HasPrincipalKey(p => new { p.OperatorId, p.Id })
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

        builder.HasOne(e => e.FarePolicyVersion)
            .WithMany(f => f.OrderVersions)
            .HasForeignKey(e => new { e.OperatorId, e.FarePolicyVersionId })
            .HasPrincipalKey(f => new { f.OperatorId, f.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

