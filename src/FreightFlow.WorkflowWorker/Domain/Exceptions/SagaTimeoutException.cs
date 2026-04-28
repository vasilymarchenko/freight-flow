using FreightFlow.SharedKernel;

namespace FreightFlow.WorkflowWorker.Domain.Exceptions;

public sealed class SagaTimeoutException : DomainException
{
    public SagaTimeoutException(Guid correlationId)
        : base($"AwardWorkflow saga {correlationId} timed out before reaching Completed state.") { }
}
