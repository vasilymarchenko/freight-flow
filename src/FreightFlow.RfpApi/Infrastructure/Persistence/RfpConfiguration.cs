using FreightFlow.RfpApi.Domain;
using FreightFlow.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FreightFlow.RfpApi.Infrastructure.Persistence;

internal sealed class RfpConfiguration : IEntityTypeConfiguration<Rfp>
{
    public void Configure(EntityTypeBuilder<Rfp> builder)
    {
        builder.ToTable("rfps");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, v => RfpId.From(v));

        builder.Property(r => r.ShipperId).HasColumnName("shipper_id");

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<int>();

        builder.Property(r => r.OpenAt).HasColumnName("open_at");
        builder.Property(r => r.CloseAt).HasColumnName("close_at");
        builder.Property(r => r.MaxBidRounds).HasColumnName("max_bid_rounds");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");

        // PostgreSQL's built-in row-version column — no extra column needed.
        // Any UPDATE to rfps (status, updated_at) triggers a WHERE xmin = <original> check.
        // Two concurrent SubmitBid calls both read xmin = v1; first writer bumps xmin to v2;
        // second writer's UPDATE matches 0 rows → DbUpdateConcurrencyException → HTTP 409.
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // ── Lanes ────────────────────────────────────────────────────────────

        builder.Navigation(r => r.Lanes)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(r => r.Lanes, lane =>
        {
            lane.ToTable("lanes");
            lane.WithOwner().HasForeignKey("rfp_id");

            lane.HasKey(l => l.Id);
            lane.Property(l => l.Id)
                .HasColumnName("id")
                .HasConversion(id => id.Value, v => LaneId.From(v));

            lane.Property(l => l.OriginZip)
                .HasColumnName("origin_zip")
                .HasConversion(z => z.Value, v => new ZipCode(v));

            lane.Property(l => l.DestinationZip)
                .HasColumnName("dest_zip")
                .HasConversion(z => z.Value, v => new ZipCode(v));

            lane.Property(l => l.FreightClass)
                .HasColumnName("freight_class")
                .HasConversion<int>();

            lane.Property(l => l.Volume).HasColumnName("volume");
        });

        // ── Bids ─────────────────────────────────────────────────────────────

        builder.Navigation(r => r.Bids)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(r => r.Bids, bid =>
        {
            bid.ToTable("bids");
            bid.WithOwner().HasForeignKey("rfp_id");

            bid.HasKey(b => b.Id);
            bid.Property(b => b.Id)
                .HasColumnName("id")
                .HasConversion(id => id.Value, v => BidId.From(v));

            bid.Property(b => b.CarrierId)
                .HasColumnName("carrier_id")
                .HasConversion(id => id.Value, v => CarrierId.From(v));

            bid.Property(b => b.Round).HasColumnName("round");
            bid.Property(b => b.SubmittedAt).HasColumnName("submitted_at");

            // ── LanePrices (doubly-nested owned collection) ───────────────

            bid.Navigation(b => b.LanePrices)
                .UsePropertyAccessMode(PropertyAccessMode.Field);

            bid.OwnsMany(b => b.LanePrices, lp =>
            {
                lp.ToTable("bid_lane_prices");
                lp.WithOwner().HasForeignKey("bid_id");

                // Composite PK: (bid_id [shadow FK], LaneId [real property with converter]).
                lp.HasKey("bid_id", nameof(LanePrice.LaneId));

                lp.Property(l => l.LaneId)
                    .HasColumnName("lane_id")
                    .HasConversion(id => id.Value, v => LaneId.From(v));

                // Map Money's two components via private field access — no ComplexProperty needed.
                // EF Core writes _amount/_currency on INSERT and reads them on SELECT.
                lp.Property<decimal>("_amount")
                    .HasColumnName("amount")
                    .HasColumnType("numeric(12,2)")
                    .UsePropertyAccessMode(PropertyAccessMode.Field);

                lp.Property<string>("_currency")
                    .HasColumnName("currency")
                    .HasMaxLength(3)
                    .UsePropertyAccessMode(PropertyAccessMode.Field);
            });
        });

        // ── Award (optional owned entity — separate table) ────────────────────

        builder.OwnsOne(r => r.Award, award =>
        {
            award.ToTable("awards");

            // rfp_id is both the PK and FK — one award per RFP.
            award.WithOwner().HasForeignKey("rfp_id");

            award.Property(a => a.BidId)
                .HasColumnName("bid_id")
                .HasConversion(id => id.Value, v => BidId.From(v));

            award.Property(a => a.CarrierId)
                .HasColumnName("carrier_id")
                .HasConversion(id => id.Value, v => CarrierId.From(v));

            award.Property(a => a.ContractId)
                .HasColumnName("contract_id")
                .HasConversion(
                    new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<ContractId, Guid>(
                        id => id.Value,
                        v  => ContractId.From(v)))
                .IsRequired(false);

            award.Property(a => a.AwardedAt).HasColumnName("awarded_at");
        });

        builder.Navigation(r => r.Award).IsRequired(false);
    }
}
