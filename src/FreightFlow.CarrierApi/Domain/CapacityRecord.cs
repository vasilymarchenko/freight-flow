using FreightFlow.SharedKernel;

namespace FreightFlow.CarrierApi.Domain;

public sealed class CapacityRecord
{
    public CapacityRecordId Id              { get; private set; }
    public LaneId           LaneId          { get; private set; }
    public int              AvailableVolume { get; private set; }
    public int              ReservedVolume  { get; private set; }

    private CapacityRecord() { }  // EF Core

    internal CapacityRecord(CapacityRecordId id, LaneId laneId, int availableVolume)
    {
        Id              = id;
        LaneId          = laneId;
        AvailableVolume = availableVolume;
        ReservedVolume  = 0;
    }

    internal void Reserve(int volume)
    {
        if (AvailableVolume - ReservedVolume < volume)
            throw new InsufficientCapacityException();

        ReservedVolume += volume;
    }

    internal void Release(int volume)
    {
        if (ReservedVolume < volume)
            throw new DomainException($"Cannot release {volume} units — only {ReservedVolume} units are reserved on lane {LaneId}.");

        ReservedVolume -= volume;
    }
}
