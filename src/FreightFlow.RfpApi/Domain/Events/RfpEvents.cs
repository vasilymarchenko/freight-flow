using FreightFlow.SharedKernel;

namespace FreightFlow.RfpApi.Domain.Events;

public sealed record RfpCreated(RfpId RfpId, Guid ShipperId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record RfpOpened(RfpId RfpId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record RfpClosed(RfpId RfpId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record BidSubmitted(RfpId RfpId, BidId BidId, CarrierId CarrierId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AwardIssued(RfpId RfpId, BidId BidId, CarrierId CarrierId, DateTimeOffset OccurredAt) : IDomainEvent;
