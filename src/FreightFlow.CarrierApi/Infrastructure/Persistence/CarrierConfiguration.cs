using System.Text.Json;
using FreightFlow.CarrierApi.Domain;
using FreightFlow.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FreightFlow.CarrierApi.Infrastructure.Persistence;

internal sealed class CarrierConfiguration : IEntityTypeConfiguration<Carrier>
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Carrier> builder)
    {
        builder.ToTable("carriers");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, v => CarrierId.From(v));

        builder.Property(c => c.DotNumber)
            .HasColumnName("dot_number")
            .HasConversion(d => d.Value, v => new DotNumber(v));

        builder.Property(c => c.Name)
            .HasColumnName("name")
            .HasMaxLength(200);

        builder.Property(c => c.AuthorityStatus)
            .HasColumnName("authority_status")
            .HasConversion<int>();

        builder.Property(c => c.InsuranceExpiry)
            .HasColumnName("insurance_expiry");

        builder.Property(c => c.Profile)
            .HasColumnName("profile")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, _jsonOptions),
                v => JsonSerializer.Deserialize<CarrierProfile>(v, _jsonOptions)!);

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at");

        // CapacityRecords is backed by _capacityRecords field — use field access so EF Core
        // can populate the private list without needing a public setter.
        builder.Navigation(c => c.CapacityRecords)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(c => c.CapacityRecords, cr =>
        {
            cr.ToTable("capacity_records");

            cr.WithOwner().HasForeignKey("carrier_id");

            cr.HasKey(r => r.Id);
            cr.Property(r => r.Id)
                .HasColumnName("id")
                .HasConversion(id => id.Value, v => CapacityRecordId.From(v));

            cr.Property(r => r.LaneId)
                .HasColumnName("lane_id")
                .HasConversion(id => id.Value, v => LaneId.From(v));

            cr.Property(r => r.AvailableVolume)
                .HasColumnName("available_volume");

            cr.Property(r => r.ReservedVolume)
                .HasColumnName("reserved_volume");

            // Map PostgreSQL's built-in xmin system column as a concurrency token.
            // xmin is updated by Postgres on every row modification — no extra column needed.
            cr.Property<uint>("xmin")
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });
    }
}
