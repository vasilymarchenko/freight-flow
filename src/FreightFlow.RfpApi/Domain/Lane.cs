using FreightFlow.SharedKernel;

namespace FreightFlow.RfpApi.Domain;

public sealed class Lane
{
    public LaneId      Id           { get; private set; }
    public ZipCode     OriginZip    { get; private set; }
    public ZipCode     DestinationZip { get; private set; }
    public FreightClass FreightClass { get; private set; }
    public int         Volume       { get; private set; }

    private Lane() { }  // EF Core

    internal Lane(LaneId id, ZipCode originZip, ZipCode destinationZip, FreightClass freightClass, int volume)
    {
        Id             = id;
        OriginZip      = originZip;
        DestinationZip = destinationZip;
        FreightClass   = freightClass;
        Volume         = volume;
    }
}
