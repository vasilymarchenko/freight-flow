using FreightFlow.SharedKernel;
using FreightFlow.WorkflowWorker.Application;
using MassTransit;

namespace FreightFlow.WorkflowWorker.Infrastructure.Activities;

/// <summary>
/// Step 4 of 4: publishes <see cref="RfpMarkAsAwarded"/> to rfp-api so it can
/// record the ContractId against the RFP.
/// On failure: logs the fault; the saga transitions to Failed but capacity and
/// contract are NOT rolled back (the award is already committed).
/// </summary>
public sealed class MarkRfpAwardedActivity : IMarkRfpAwardedActivity
{
    private readonly ILogger<MarkRfpAwardedActivity> _logger;

    public MarkRfpAwardedActivity(ILogger<MarkRfpAwardedActivity> logger)
    {
        _logger = logger;
    }

    public async Task Execute(
        BehaviorContext<AwardWorkflowState> context,
        IBehavior<AwardWorkflowState>       next)
    {
        await ExecuteCoreAsync(context);
        await next.Execute(context);
    }

    public Task Execute<T>(
        BehaviorContext<AwardWorkflowState, T> context,
        IBehavior<AwardWorkflowState, T>       next)
        where T : class
        => next.Execute(context);

    async Task IStateMachineActivity<AwardWorkflowState, ShipperNotifiedInternalEvent>.Execute(
        BehaviorContext<AwardWorkflowState, ShipperNotifiedInternalEvent> context,
        IBehavior<AwardWorkflowState, ShipperNotifiedInternalEvent>       next)
    {
        await ExecuteCoreAsync(context);
        await next.Execute(context);
    }

    private async Task ExecuteCoreAsync(BehaviorContext<AwardWorkflowState> context)
    {
        var saga = context.Saga;

        await context.Publish(new RfpMarkAsAwarded(
            RfpId:      saga.RfpId,
            ContractId: saga.ContractId ?? Guid.Empty,
            OccurredAt: DateTimeOffset.UtcNow));

        saga.UpdatedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Saga {CorrelationId}: RfpMarkAsAwarded published for RFP {RfpId}. Workflow complete.",
            saga.CorrelationId, saga.RfpId);
    }

    Task IStateMachineActivity<AwardWorkflowState, ShipperNotifiedInternalEvent>.Faulted<TException>(
        BehaviorExceptionContext<AwardWorkflowState, ShipperNotifiedInternalEvent, TException> context,
        IBehavior<AwardWorkflowState, ShipperNotifiedInternalEvent>                            next)
        => next.Faulted(context);

    public Task Faulted<TException>(
        BehaviorExceptionContext<AwardWorkflowState, TException> context,
        IBehavior<AwardWorkflowState>                            next)
        where TException : Exception
        => next.Faulted(context);

    public Task Faulted<T, TException>(
        BehaviorExceptionContext<AwardWorkflowState, T, TException> context,
        IBehavior<AwardWorkflowState, T>                            next)
        where T : class
        where TException : Exception
        => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope(nameof(MarkRfpAwardedActivity));

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}
