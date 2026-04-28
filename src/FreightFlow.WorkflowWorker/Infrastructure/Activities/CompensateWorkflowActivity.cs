using FreightFlow.WorkflowWorker.Application;
using FreightFlow.WorkflowWorker.Infrastructure.GrpcClients;
using FreightFlow.WorkflowWorker.Infrastructure.Persistence;
using FreightFlow.SharedKernel;
using MassTransit;

namespace FreightFlow.WorkflowWorker.Infrastructure.Activities;

/// <summary>
/// Compensation activity: executed when the saga times out or transitions to
/// <c>CompensationPending</c>. Releases the capacity reservation (if one was made)
/// and voids the draft contract (if one was written).
/// </summary>
public sealed class CompensateWorkflowActivity : ICompensateWorkflowActivity
{
    private readonly CarrierCapacityClient                 _grpcClient;
    private readonly WorkflowDbContext                     _db;
    private readonly ILogger<CompensateWorkflowActivity>   _logger;

    public CompensateWorkflowActivity(
        CarrierCapacityClient                grpcClient,
        WorkflowDbContext                    db,
        ILogger<CompensateWorkflowActivity>  logger)
    {
        _grpcClient = grpcClient;
        _db         = db;
        _logger     = logger;
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

    async Task IStateMachineActivity<AwardWorkflowState, AwardWorkflowTimedOut>.Execute(
        BehaviorContext<AwardWorkflowState, AwardWorkflowTimedOut> context,
        IBehavior<AwardWorkflowState, AwardWorkflowTimedOut>       next)
    {
        await ExecuteCoreAsync(context);
        await next.Execute(context);
    }

    private async Task ExecuteCoreAsync(BehaviorContext<AwardWorkflowState> context)
    {
        var saga = context.Saga;

        _logger.LogWarning(
            "Saga {CorrelationId}: compensating. ReservationId={ReservationId}, ContractId={ContractId}",
            saga.CorrelationId, saga.ReservationId, saga.ContractId);

        // Compensate step 2 first (reverse order): void draft contract if written.
        if (saga.ContractId.HasValue)
        {
            try
            {
                var contract = await _db.Contracts.FindAsync(ContractId.From(saga.ContractId.Value));
                if (contract is not null)
                {
                    contract.Void();
                    await _db.SaveChangesAsync(context.CancellationToken);
                    _logger.LogInformation(
                        "Saga {CorrelationId}: voided contract {ContractId}.",
                        saga.CorrelationId, saga.ContractId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Saga {CorrelationId}: failed to void contract {ContractId} during compensation.",
                    saga.CorrelationId, saga.ContractId);
            }
        }

        // Compensate step 1: release the capacity reservation.
        if (saga.ReservationId != Guid.Empty)
        {
            try
            {
                var result = await _grpcClient.ReleaseCapacityAsync(
                    reservationId: saga.ReservationId.ToString(),
                    carrierId:     saga.CarrierId.ToString(),
                    laneId:        saga.LaneId.ToString(),
                    volume:        saga.VolumeToReserve);

                if (result.Success)
                    _logger.LogInformation(
                        "Saga {CorrelationId}: capacity released for reservation {ReservationId}.",
                        saga.CorrelationId, saga.ReservationId);
                else
                    _logger.LogWarning(
                        "Saga {CorrelationId}: capacity release returned failure: {Reason}",
                        saga.CorrelationId, result.Reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Saga {CorrelationId}: failed to release capacity reservation {ReservationId}.",
                    saga.CorrelationId, saga.ReservationId);
            }
        }

        saga.UpdatedAt = DateTimeOffset.UtcNow;
    }

    Task IStateMachineActivity<AwardWorkflowState, AwardWorkflowTimedOut>.Faulted<TException>(
        BehaviorExceptionContext<AwardWorkflowState, AwardWorkflowTimedOut, TException> context,
        IBehavior<AwardWorkflowState, AwardWorkflowTimedOut>                            next)
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

    public void Probe(ProbeContext context) => context.CreateScope(nameof(CompensateWorkflowActivity));

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}
