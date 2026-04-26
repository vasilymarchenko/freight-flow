using System.Text.Json;
using FreightFlow.CarrierApi.Domain;
using FreightFlow.CarrierApi.Domain.Events;
using FreightFlow.CarrierApi.Infrastructure.Persistence;
using FreightFlow.SharedKernel;

namespace FreightFlow.CarrierApi.Features.OnboardCarrier;

public sealed class OnboardCarrierHandler
{
    private readonly CarrierDbContext _db;
    private readonly ILogger<OnboardCarrierHandler> _logger;

    public OnboardCarrierHandler(CarrierDbContext db, ILogger<OnboardCarrierHandler> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<CarrierId> HandleAsync(OnboardCarrierCommand command, CancellationToken ct = default)
    {
        var carrier = Carrier.Onboard(
            new DotNumber(command.DotNumber),
            command.Name,
            command.InsuranceExpiry,
            new CarrierProfile(command.EquipmentTypes, command.Certifications, command.Notes));

        _db.Carriers.Add(carrier);

        // Outbox row written in the same SaveChanges call — both succeed or both fail.
        var evt = carrier.DomainEvents.OfType<CarrierOnboarded>().First();
        _db.OutboxMessages.Add(OutboxMessage.Create(
            typeof(CarrierOnboarded).AssemblyQualifiedName!,
            JsonSerializer.Serialize(evt)));

        await _db.SaveChangesAsync(ct);
        carrier.ClearDomainEvents();

        _logger.LogInformation("Carrier {CarrierId} onboarded with DOT {DotNumber}",
            carrier.Id, carrier.DotNumber);

        return carrier.Id;
    }
}
