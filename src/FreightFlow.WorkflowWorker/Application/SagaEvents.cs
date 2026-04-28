namespace FreightFlow.WorkflowWorker.Application;

/// <summary>Internal events used to advance the saga between states.</summary>
/// <remarks>
/// These never leave the freight-workflow-worker service — they are published to
/// RabbitMQ but consumed only by this saga instance (correlated by CorrelationId).
/// Using internal events (rather than inline activities) means the saga state is
/// persisted to the DB after every step, making progress observable.
/// </remarks>

public sealed record CapacityReservedEvent(Guid CorrelationId);

public sealed record ContractIssuedEvent(Guid CorrelationId, Guid ContractId);

public sealed record ShipperNotifiedInternalEvent(Guid CorrelationId);

public sealed record AwardWorkflowTimedOut(Guid CorrelationId);
