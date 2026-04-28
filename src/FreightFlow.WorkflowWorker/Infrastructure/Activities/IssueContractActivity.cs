using FreightFlow.SharedKernel;
using FreightFlow.WorkflowWorker.Application;
using FreightFlow.WorkflowWorker.Domain;
using FreightFlow.WorkflowWorker.Infrastructure.Persistence;
using MassTransit;

namespace FreightFlow.WorkflowWorker.Infrastructure.Activities;

/// <summary>
/// Step 2 of 4: creates the Contract aggregate and persists it to postgres-rfp.
/// Runs after <see cref="CapacityReservedEvent"/>.
/// On failure: compensation must release the capacity reserved in step 1.
/// </summary>
public sealed class IssueContractActivity : IIssueContractActivity
{
    private readonly WorkflowDbContext               _db;
    private readonly ILogger<IssueContractActivity>  _logger;

    public IssueContractActivity(
        WorkflowDbContext              db,
        ILogger<IssueContractActivity> logger)
    {
        _db     = db;
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

    async Task IStateMachineActivity<AwardWorkflowState, CapacityReservedEvent>.Execute(
        BehaviorContext<AwardWorkflowState, CapacityReservedEvent> context,
        IBehavior<AwardWorkflowState, CapacityReservedEvent>       next)
    {
        await ExecuteCoreAsync(context);
        await next.Execute(context);
    }

    private async Task ExecuteCoreAsync(BehaviorContext<AwardWorkflowState> context)
    {
        var saga = context.Saga;

        _logger.LogInformation(
            "Saga {CorrelationId}: issuing contract for RFP {RfpId}, carrier {CarrierId}",
            saga.CorrelationId, saga.RfpId, saga.CarrierId);

        var contract = Contract.Create(
            rfpId:      RfpId.From(saga.RfpId),
            carrierId:  CarrierId.From(saga.CarrierId),
            laneId:     LaneId.From(saga.LaneId),
            agreedRate: new Money(saga.AgreedAmount, saga.AgreedCurrency));

        _db.Contracts.Add(contract);
        await _db.SaveChangesAsync(context.CancellationToken);

        saga.ContractId = contract.Id.Value;
        saga.UpdatedAt  = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Saga {CorrelationId}: contract {ContractId} issued. Advancing to ShipperNotifying.",
            saga.CorrelationId, contract.Id);

        await context.Publish(new ContractIssuedEvent(saga.CorrelationId, contract.Id.Value));
    }

    Task IStateMachineActivity<AwardWorkflowState, CapacityReservedEvent>.Faulted<TException>(
        BehaviorExceptionContext<AwardWorkflowState, CapacityReservedEvent, TException> context,
        IBehavior<AwardWorkflowState, CapacityReservedEvent>                            next)
        => next.Faulted(context);

    public async Task Faulted<TException>(
        BehaviorExceptionContext<AwardWorkflowState, TException> context,
        IBehavior<AwardWorkflowState>                            next)
        where TException : Exception
    {
        // If a draft contract was written before the exception, void it.
        if (context.Saga.ContractId.HasValue)
        {
            var contract = await _db.Contracts.FindAsync(ContractId.From(context.Saga.ContractId.Value));
            contract?.Void();
            await _db.SaveChangesAsync(context.CancellationToken);

            _logger.LogWarning(
                "Saga {CorrelationId}: voided draft contract {ContractId} during compensation",
                context.Saga.CorrelationId, context.Saga.ContractId);
        }

        await next.Faulted(context);
    }

    public Task Faulted<T, TException>(
        BehaviorExceptionContext<AwardWorkflowState, T, TException> context,
        IBehavior<AwardWorkflowState, T>                            next)
        where T : class
        where TException : Exception
        => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope(nameof(IssueContractActivity));

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}
