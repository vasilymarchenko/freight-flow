using FreightFlow.SharedKernel;
using MassTransit;

namespace FreightFlow.WorkflowWorker.Application;

/// <summary>
/// Orchestrates the 4-step award workflow.
///
/// State flow (happy path):
///   Initial
///     → (AwardIssued) → ReserveCapacity → CapacityReserving
///     → (CapacityReserved) → IssueContract → ContractIssuing
///     → (ContractIssued) → NotifyShipper → ShipperNotifying
///     → (ShipperNotified) → MarkRfpAwarded → RfpAwarding
///     → (RfpAwardAcknowledged from rfp-api) → Completed
///
/// Compensation path (timeout at any active state):
///   Any → (SagaTimeout) → CompensateWorkflow → Compensated
///
/// On activity failure: MassTransit retries 3× then dead-letters.
/// On 30-second timeout: compensation runs in reverse order (contract void → capacity release).
///
/// Activity interfaces (IReserveCapacityActivity etc.) are defined in ActivityContracts.cs.
/// Infrastructure implementations are registered in Program.cs as interface → concrete.
/// </summary>
public sealed class AwardWorkflowStateMachine : MassTransitStateMachine<AwardWorkflowState>
{
    // ── States ────────────────────────────────────────────────────────────────
    public State CapacityReserving   { get; private set; } = null!;
    public State ContractIssuing     { get; private set; } = null!;
    public State ShipperNotifying    { get; private set; } = null!;
    public State RfpAwarding         { get; private set; } = null!;
    public State Completed           { get; private set; } = null!;
    public State Compensated         { get; private set; } = null!;

    // ── External trigger ──────────────────────────────────────────────────────
    public Event<AwardIssued>               AwardIssuedEvent { get; private set; } = null!;

    // ── Internal step-advancement events ──────────────────────────────────────
    public Event<CapacityReservedEvent>         CapacityReserved       { get; private set; } = null!;
    public Event<ContractIssuedEvent>           ContractIssued         { get; private set; } = null!;
    public Event<ShipperNotifiedInternalEvent>  ShipperNotifiedEvt     { get; private set; } = null!;

    // ── External acknowledgement from rfp-api ─────────────────────────────────
    public Event<RfpAwardAcknowledged>          RfpAwardAcknowledgedEvt { get; private set; } = null!;

    // ── Timeout schedule ──────────────────────────────────────────────────────
    public Schedule<AwardWorkflowState, AwardWorkflowTimedOut> SagaTimeout { get; private set; } = null!;

    public AwardWorkflowStateMachine()
    {
        InstanceState(x => x.CurrentState);

        // ── Event correlations ────────────────────────────────────────────────
        // External trigger: one saga per RFP award, correlated by RfpId.
        Event(() => AwardIssuedEvent, e => e.CorrelateById(ctx => ctx.Message.RfpId.Value));

        // Internal step events: correlated by the saga's CorrelationId.
        Event(() => CapacityReserved,   e => e.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => ContractIssued,     e => e.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => ShipperNotifiedEvt, e => e.CorrelateById(ctx => ctx.Message.CorrelationId));

        // rfp-api ack: correlated by RfpId which equals the saga CorrelationId.
        Event(() => RfpAwardAcknowledgedEvt, e => e.CorrelateById(ctx => ctx.Message.RfpId));

        // 30-second global deadline.
        Schedule(() => SagaTimeout, x => x.SagaTimeoutTokenId, s =>
        {
            s.Delay    = TimeSpan.FromSeconds(30);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        // ── Step 1: Initial → CapacityReserving ───────────────────────────────
        Initially(
            When(AwardIssuedEvent)
                .Then(ctx =>
                {
                    var msg = ctx.Message;
                    ctx.Saga.RfpId           = msg.RfpId.Value;
                    ctx.Saga.BidId           = msg.BidId.Value;
                    ctx.Saga.CarrierId       = msg.CarrierId.Value;
                    ctx.Saga.LaneId          = msg.LaneId.Value;
                    ctx.Saga.AgreedAmount    = msg.AgreedRate.Amount;
                    ctx.Saga.AgreedCurrency  = msg.AgreedRate.Currency;
                    ctx.Saga.VolumeToReserve = msg.VolumeToReserve;
                    // Derive ReservationId from CorrelationId for idempotency across retries.
                    ctx.Saga.ReservationId   = ctx.Saga.CorrelationId;
                    ctx.Saga.CreatedAt       = DateTimeOffset.UtcNow;
                    ctx.Saga.UpdatedAt       = DateTimeOffset.UtcNow;
                })
                .Schedule(SagaTimeout,
                    ctx => new AwardWorkflowTimedOut(ctx.Saga.CorrelationId))
                .Activity(x => x.OfType<IReserveCapacityActivity>())
                .TransitionTo(CapacityReserving));

        // ── Step 2: CapacityReserving → ContractIssuing ───────────────────────
        During(CapacityReserving,
            When(CapacityReserved)
                .Activity(x => x.OfType<IIssueContractActivity>())
                .TransitionTo(ContractIssuing),
            Ignore(AwardIssuedEvent));  // idempotency — discard re-deliveries

        // ── Step 3: ContractIssuing → ShipperNotifying ────────────────────────
        During(ContractIssuing,
            When(ContractIssued)
                .Then(ctx => ctx.Saga.ContractId = ctx.Message.ContractId)
                .Activity(x => x.OfType<INotifyShipperActivity>())
                .TransitionTo(ShipperNotifying),
            Ignore(AwardIssuedEvent));

        // ── Step 4: ShipperNotifying → RfpAwarding ────────────────────────────────
        // MarkRfpAwardedActivity publishes RfpMarkAsAwarded command to rfp-api.
        // The saga then waits in RfpAwarding for rfp-api to acknowledge.
        During(ShipperNotifying,
            When(ShipperNotifiedEvt)
                .Activity(x => x.OfType<IMarkRfpAwardedActivity>())
                .TransitionTo(RfpAwarding),
            Ignore(AwardIssuedEvent));

        // ── Step 5: RfpAwarding → Completed ───────────────────────────────────────
        // rfp-api consumer writes the ContractId and publishes RfpAwardAcknowledged.
        During(RfpAwarding,
            When(RfpAwardAcknowledgedEvt)
                .TransitionTo(Completed),
            Ignore(AwardIssuedEvent));

        // ── Completed: ignore re-deliveries ──────────────────────────────────────
        During(Completed,
            Ignore(AwardIssuedEvent),
            Ignore(CapacityReserved),
            Ignore(ContractIssued),
            Ignore(ShipperNotifiedEvt),
            Ignore(RfpAwardAcknowledgedEvt));

        // ── Timeout: any active state → Compensated ─────────────────────────────────
        // DuringAny covers all active states including RfpAwarding.
        // Final states (Completed, Compensated) are excluded by design.
        DuringAny(
            When(SagaTimeout.Received)
                .Unschedule(SagaTimeout)
                .Activity(x => x.OfType<ICompensateWorkflowActivity>())
                .TransitionTo(Compensated));
    }
}
