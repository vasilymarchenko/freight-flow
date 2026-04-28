using FreightFlow.SharedKernel;
using FreightFlow.WorkflowWorker.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FreightFlow.WorkflowWorker.Infrastructure.Persistence;

internal sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.ToTable("contracts");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, v => ContractId.From(v));

        builder.Property(c => c.RfpId)
            .HasColumnName("rfp_id")
            .HasConversion(id => id.Value, v => RfpId.From(v));

        builder.Property(c => c.CarrierId)
            .HasColumnName("carrier_id")
            .HasConversion(id => id.Value, v => CarrierId.From(v));

        builder.Property(c => c.LaneId)
            .HasColumnName("lane_id")
            .HasConversion(id => id.Value, v => LaneId.From(v));

        builder.ComplexProperty(c => c.AgreedRate, owned =>
        {
            owned.Property(m => m.Amount).HasColumnName("agreed_amount").HasColumnType("numeric(12,2)");
            owned.Property(m => m.Currency).HasColumnName("agreed_currency").HasMaxLength(3);
        });

        builder.Property(c => c.IssuedAt)
            .HasColumnName("issued_at")
            .HasColumnType("timestamptz");

        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasConversion<int>();
    }
}
