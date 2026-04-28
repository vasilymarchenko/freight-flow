using FreightFlow.CarrierApi.Domain.Events;
using FreightFlow.SharedKernel;

namespace FreightFlow.CarrierApi.Domain;

public enum AuthorityStatus { Active, Inactive, Revoked }

public sealed class Carrier : AggregateRoot
{
    private readonly List<CapacityRecord> _capacityRecords = [];

    public CarrierId                      Id               { get; private set; }
    public DotNumber                      DotNumber        { get; private set; }
    public string                         Name             { get; private set; } = string.Empty;
    public AuthorityStatus                AuthorityStatus  { get; private set; }
    public DateOnly                       InsuranceExpiry  { get; private set; }
    public CarrierProfile                 Profile          { get; private set; } = null!;
    public IReadOnlyList<CapacityRecord>  CapacityRecords  => _capacityRecords.AsReadOnly();
    public DateTimeOffset                 CreatedAt        { get; private set; }

    private Carrier() { }  // EF Core

    public static Carrier Onboard(
        DotNumber dotNumber,
        string name,
        DateOnly insuranceExpiry,
        CarrierProfile profile)
    {
        var carrier = new Carrier
        {
            Id              = CarrierId.New(),
            DotNumber       = dotNumber,
            Name            = name,
            AuthorityStatus = AuthorityStatus.Active,
            InsuranceExpiry = insuranceExpiry,
            Profile         = profile,
            CreatedAt       = DateTimeOffset.UtcNow
        };
        carrier.Raise(new CarrierOnboarded(carrier.Id, dotNumber, DateTimeOffset.UtcNow));
        return carrier;
    }

    public CapacityRecord AddCapacity(LaneId laneId, int availableVolume)
    {
        var record = new CapacityRecord(CapacityRecordId.New(), laneId, availableVolume);
        _capacityRecords.Add(record);
        return record;
    }

    public void Deactivate()
    {
        if (AuthorityStatus == AuthorityStatus.Revoked)
            throw new DomainException("Cannot deactivate a carrier whose authority is already revoked.");

        AuthorityStatus = AuthorityStatus.Inactive;
    }

    public void Revoke()
    {
        AuthorityStatus = AuthorityStatus.Revoked;
    }

    public void ReserveCapacity(LaneId laneId, int volume)
    {
        if (AuthorityStatus != AuthorityStatus.Active)
            throw new CarrierNotActiveException();

        var record = _capacityRecords.FirstOrDefault(r => r.LaneId == laneId)
            ?? throw new DomainException($"No capacity record for lane '{laneId}'.");

        record.Reserve(volume);
    }

    public void ReleaseCapacity(LaneId laneId, int volume)
    {
        var record = _capacityRecords.FirstOrDefault(r => r.LaneId == laneId)
            ?? throw new DomainException($"No capacity record found for lane '{laneId}' to release.");

        record.Release(volume);
    }
}
