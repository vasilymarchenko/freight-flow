using FreightFlow.SharedKernel;

namespace FreightFlow.WorkflowWorker.Domain;

public enum ContractStatus { Draft, Active, Void }

public sealed class Contract : AggregateRoot
{
    public ContractId     Id          { get; private set; }
    public RfpId          RfpId       { get; private set; }
    public CarrierId      CarrierId   { get; private set; }
    public LaneId         LaneId      { get; private set; }
    public Money          AgreedRate  { get; private set; }
    public DateTimeOffset IssuedAt    { get; private set; }
    public ContractStatus Status      { get; private set; }

    private Contract() { }  // EF Core

    public static Contract Create(
        RfpId rfpId,
        CarrierId carrierId,
        LaneId laneId,
        Money agreedRate)
    {
        return new Contract
        {
            Id         = ContractId.New(),
            RfpId      = rfpId,
            CarrierId  = carrierId,
            LaneId     = laneId,
            AgreedRate = agreedRate,
            IssuedAt   = DateTimeOffset.UtcNow,
            Status     = ContractStatus.Draft
        };
    }

    public void Activate()
    {
        if (Status != ContractStatus.Draft)
            throw new DomainException("Only a Draft contract can be activated.");

        Status = ContractStatus.Active;
    }

    public void Void()
    {
        if (Status == ContractStatus.Void)
            throw new DomainException("Contract is already void.");

        Status = ContractStatus.Void;
    }
}
