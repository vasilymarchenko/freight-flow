namespace FreightFlow.RfpApi.Features.CreateRfp;

public sealed record CreateRfpCommand(
    Guid            ShipperId,
    DateTimeOffset  OpenAt,
    DateTimeOffset  CloseAt,
    int             MaxBidRounds);
