using FreightFlow.SharedKernel;
using FreightFlow.WorkflowWorker.Application;
using MassTransit;

namespace FreightFlow.WorkflowWorker.Infrastructure.Activities;

/// <summary>
/// Step 3 of 4: publishes <see cref="ShipperNotified"/> to RabbitMQ.
/// Best-effort — failure is logged and the saga continues rather than compensating.
/// </summary>
public sealed class NotifyShipperActivity : INotifyShipperActivity
{
    private readonly ILogger<NotifyShipperActivity> _logger;

    public NotifyShipperActivity(ILogger<NotifyShipperActivity> logger)
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

    async Task IStateMachineActivity<AwardWorkflowState, ContractIssuedEvent>.Execute(
        BehaviorContext<AwardWorkflowState, ContractIssuedEvent> context,
        IBehavior<AwardWorkflowState, ContractIssuedEvent>       next)
    {
        await ExecuteCoreAsync(context);
        await next.Execute(context);
    }

    private async Task ExecuteCoreAsync(BehaviorContext<AwardWorkflowState> context)
    {
        var saga = context.Saga;

        if (!saga.ContractId.HasValue)
        {
            _logger.LogWarning(
                "Saga {CorrelationId}: no ContractId set when notifying shipper — skipping.",
                saga.CorrelationId);
            return;
        }

        try
        {
            await context.Publish(new ShipperNotified(
                RfpId:      RfpId.From(saga.RfpId),
                ContractId: ContractId.From(saga.ContractId.Value),
                OccurredAt: DateTimeOffset.UtcNow));

            _logger.LogInformation(
                "Saga {CorrelationId}: ShipperNotified published for contract {ContractId}.",
                saga.CorrelationId, saga.ContractId);
        }
        catch (Exception ex)
        {
            // Best-effort: log and continue — shipper notification failure does not compensate.
            _logger.LogError(ex,
                "Saga {CorrelationId}: failed to publish ShipperNotified — continuing anyway.",
                saga.CorrelationId);
        }

        saga.UpdatedAt = DateTimeOffset.UtcNow;
        await context.Publish(new ShipperNotifiedInternalEvent(saga.CorrelationId));
    }

    Task IStateMachineActivity<AwardWorkflowState, ContractIssuedEvent>.Faulted<TException>(
        BehaviorExceptionContext<AwardWorkflowState, ContractIssuedEvent, TException> context,
        IBehavior<AwardWorkflowState, ContractIssuedEvent>                            next)
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

    public void Probe(ProbeContext context) => context.CreateScope(nameof(NotifyShipperActivity));

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}
