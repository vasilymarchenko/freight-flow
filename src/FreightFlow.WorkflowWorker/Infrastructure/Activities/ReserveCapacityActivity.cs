using FreightFlow.SharedKernel;
using FreightFlow.WorkflowWorker.Application;
using FreightFlow.WorkflowWorker.Infrastructure.GrpcClients;
using MassTransit;

namespace FreightFlow.WorkflowWorker.Infrastructure.Activities;

/// <summary>
/// Step 1 of 4: reserves capacity on the carrier via gRPC.
/// On success publishes <see cref="CapacityReservedEvent"/> to advance the saga.
/// On failure throws — MassTransit retries, then dead-letters; the saga's
/// 30-second schedule fires <see cref="AwardWorkflowTimedOut"/> to compensate.
/// </summary>
public sealed class ReserveCapacityActivity : IReserveCapacityActivity
{
    private readonly CarrierCapacityClient                _client;
    private readonly ILogger<ReserveCapacityActivity>     _logger;

    public ReserveCapacityActivity(
        CarrierCapacityClient             client,
        ILogger<ReserveCapacityActivity>  logger)
    {
        _client = client;
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

    async Task IStateMachineActivity<AwardWorkflowState, AwardIssued>.Execute(
        BehaviorContext<AwardWorkflowState, AwardIssued> context,
        IBehavior<AwardWorkflowState, AwardIssued>       next)
    {
        await ExecuteCoreAsync(context);
        await next.Execute(context);
    }

    private async Task ExecuteCoreAsync(BehaviorContext<AwardWorkflowState> context)
    {
        var saga = context.Saga;

        _logger.LogInformation(
            "Saga {CorrelationId}: reserving capacity for carrier {CarrierId}, lane {LaneId}, volume {Volume}",
            saga.CorrelationId, saga.CarrierId, saga.LaneId, saga.VolumeToReserve);

        var result = await _client.ReserveCapacityAsync(
            carrierId:     saga.CarrierId.ToString(),
            laneId:        saga.LaneId.ToString(),
            volume:        saga.VolumeToReserve,
            reservationId: saga.ReservationId.ToString());

        if (!result.Success)
        {
            _logger.LogWarning(
                "Saga {CorrelationId}: capacity reservation failed — {Reason}",
                saga.CorrelationId, result.Reason);

            throw new InvalidOperationException($"Capacity reservation failed: {result.Reason}");
        }

        saga.UpdatedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Saga {CorrelationId}: capacity reserved. Advancing to ContractIssuing.",
            saga.CorrelationId);

        await context.Publish(new CapacityReservedEvent(saga.CorrelationId));
    }

    Task IStateMachineActivity<AwardWorkflowState, AwardIssued>.Faulted<TException>(
        BehaviorExceptionContext<AwardWorkflowState, AwardIssued, TException> context,
        IBehavior<AwardWorkflowState, AwardIssued>                            next)
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

    public void Probe(ProbeContext context) => context.CreateScope(nameof(ReserveCapacityActivity));

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}
