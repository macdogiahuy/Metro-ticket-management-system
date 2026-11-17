using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Persistence.Mapping;

/// <summary>Key-only references for incidents and cars outside BE-02A/BE-02B.
/// Core references use BE-02A entities directly. SQL migrations own DDL.</summary>
internal static class ExternalReferences
{
    internal const string Incidents = "IncidentReference";
    internal const string Cars = "CarReference";

    internal static void Configure(ModelBuilder builder)
    {
        builder.SharedTypeEntity<Dictionary<string, object>>(Incidents, entity =>
        {
            entity.ToTable("incidents", "metro", table => table.ExcludeFromMigrations());
            entity.IndexerProperty<Guid>("OperatorId").HasColumnName("operator_id").HasColumnType("uuid").ValueGeneratedNever();
            entity.IndexerProperty<Guid>("Id").HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
            entity.HasKey("Id");
            entity.HasAlternateKey("OperatorId", "Id");
        });

        builder.SharedTypeEntity<Dictionary<string, object>>(Cars, entity =>
        {
            entity.ToTable("cars", "metro", table => table.ExcludeFromMigrations());
            entity.IndexerProperty<Guid>("OperatorId").HasColumnName("operator_id").HasColumnType("uuid").ValueGeneratedNever();
            entity.IndexerProperty<Guid>("Id").HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
            entity.HasKey("Id");
            entity.HasAlternateKey("OperatorId", "Id");
        });

    }
}
