using FreightFlow.SharedKernel;

namespace FreightFlow.RfpApi.Features.AddLane;

public sealed record AddLaneCommand(
    string       OriginZip,
    string       DestZip,
    FreightClass FreightClass,
    int          Volume);
