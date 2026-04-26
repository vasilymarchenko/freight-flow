namespace FreightFlow.SharedKernel;

public readonly record struct RfpId(Guid Value)
{
    public static RfpId New()            => new(Guid.NewGuid());
    public static RfpId From(Guid value) => new(value);
    public override string ToString()    => Value.ToString();
}

public readonly record struct CarrierId(Guid Value)
{
    public static CarrierId New()            => new(Guid.NewGuid());
    public static CarrierId From(Guid value) => new(value);
    public override string ToString()        => Value.ToString();
}

public readonly record struct LaneId(Guid Value)
{
    public static LaneId New()            => new(Guid.NewGuid());
    public static LaneId From(Guid value) => new(value);
    public override string ToString()     => Value.ToString();
}

public readonly record struct BidId(Guid Value)
{
    public static BidId New()            => new(Guid.NewGuid());
    public static BidId From(Guid value) => new(value);
    public override string ToString()    => Value.ToString();
}

public readonly record struct ContractId(Guid Value)
{
    public static ContractId New()            => new(Guid.NewGuid());
    public static ContractId From(Guid value) => new(value);
    public override string ToString()         => Value.ToString();
}

public readonly record struct CapacityRecordId(Guid Value)
{
    public static CapacityRecordId New()            => new(Guid.NewGuid());
    public static CapacityRecordId From(Guid value) => new(value);
    public override string ToString()               => Value.ToString();
}
