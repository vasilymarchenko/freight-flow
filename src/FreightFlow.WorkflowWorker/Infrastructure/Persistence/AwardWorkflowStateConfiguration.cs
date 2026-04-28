using FreightFlow.WorkflowWorker.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FreightFlow.WorkflowWorker.Infrastructure.Persistence;

internal sealed class AwardWorkflowStateConfiguration : IEntityTypeConfiguration<AwardWorkflowState>
{
    public void Configure(EntityTypeBuilder<AwardWorkflowState> builder)
    {
        builder.ToTable("award_workflow_state", "saga");

        builder.HasKey(x => x.CorrelationId);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id");
        builder.Property(x => x.CurrentState).HasColumnName("current_state").HasMaxLength(64);

        builder.Property(x => x.RfpId).HasColumnName("rfp_id");
        builder.Property(x => x.CarrierId).HasColumnName("carrier_id");
        builder.Property(x => x.BidId).HasColumnName("bid_id");
        builder.Property(x => x.LaneId).HasColumnName("lane_id");
        builder.Property(x => x.AgreedAmount).HasColumnName("agreed_amount").HasColumnType("numeric(12,2)");
        builder.Property(x => x.AgreedCurrency).HasColumnName("agreed_currency").HasMaxLength(3);
        builder.Property(x => x.VolumeToReserve).HasColumnName("volume_to_reserve");
        builder.Property(x => x.ReservationId).HasColumnName("reservation_id");
        builder.Property(x => x.ContractId).HasColumnName("contract_id");
        builder.Property(x => x.SagaTimeoutTokenId).HasColumnName("saga_timeout_token_id");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");

        // MassTransit uses RowVersion for optimistic concurrency on the saga state row.
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsConcurrencyToken();
    }
}
