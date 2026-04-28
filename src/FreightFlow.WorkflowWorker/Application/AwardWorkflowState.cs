using MassTransit;

namespace FreightFlow.WorkflowWorker.Application;

/// <summary>
/// Persisted state for the AwardWorkflow saga instance.
/// Stored in saga.award_workflow_state on postgres-rfp.
/// Each field maps to a DB column; RowVersion is used by MassTransit for optimistic concurrency.
/// </summary>
public sealed class AwardWorkflowState : SagaStateMachineInstance
{
    public Guid   CorrelationId    { get; set; }
    public string CurrentState     { get; set; } = string.Empty;

    // ── Trigger event data ────────────────────────────────────────────────────
    public Guid    RfpId            { get; set; }
    public Guid    CarrierId        { get; set; }
    public Guid    BidId            { get; set; }
    public Guid    LaneId           { get; set; }
    public decimal AgreedAmount     { get; set; }
    public string  AgreedCurrency   { get; set; } = string.Empty;
    public int     VolumeToReserve  { get; set; }

    // ── Step outputs ──────────────────────────────────────────────────────────
    // Set before the gRPC call — used as an idempotency key so replays are safe.
    public Guid   ReservationId     { get; set; }
    public Guid?  ContractId        { get; set; }

    // ── Timeout token (Schedule) ──────────────────────────────────────────────
    public Guid?  SagaTimeoutTokenId { get; set; }

    // ── Timestamps ────────────────────────────────────────────────────────────
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // ── MassTransit optimistic concurrency ────────────────────────────────────
    public int RowVersion { get; set; }
}
