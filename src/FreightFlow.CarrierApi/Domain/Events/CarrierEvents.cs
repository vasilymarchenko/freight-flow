using FreightFlow.SharedKernel;

namespace FreightFlow.CarrierApi.Domain.Events;

public sealed record CarrierOnboarded(CarrierId CarrierId, DotNumber DotNumber, DateTimeOffset OccurredAt) : IDomainEvent;
