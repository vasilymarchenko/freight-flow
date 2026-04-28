using FreightFlow.SharedKernel;
using MassTransit;

namespace FreightFlow.WorkflowWorker.Application;

/// <summary>
/// Marker interfaces for saga activities, owned by the Application layer.
/// Infrastructure implements these; the state machine depends only on the interfaces,
/// keeping Application free of any Infrastructure reference.
/// MassTransit resolves them from DI via OfType&lt;T&gt;() at runtime.
/// </summary>

public interface IReserveCapacityActivity
    : IStateMachineActivity<AwardWorkflowState>,
      IStateMachineActivity<AwardWorkflowState, AwardIssued> { }

public interface IIssueContractActivity
    : IStateMachineActivity<AwardWorkflowState>,
      IStateMachineActivity<AwardWorkflowState, CapacityReservedEvent> { }

public interface INotifyShipperActivity
    : IStateMachineActivity<AwardWorkflowState>,
      IStateMachineActivity<AwardWorkflowState, ContractIssuedEvent> { }

public interface IMarkRfpAwardedActivity
    : IStateMachineActivity<AwardWorkflowState>,
      IStateMachineActivity<AwardWorkflowState, ShipperNotifiedInternalEvent> { }

public interface ICompensateWorkflowActivity
    : IStateMachineActivity<AwardWorkflowState>,
      IStateMachineActivity<AwardWorkflowState, AwardWorkflowTimedOut> { }
