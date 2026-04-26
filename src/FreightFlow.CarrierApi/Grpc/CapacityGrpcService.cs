using FreightFlow.CarrierApi.Infrastructure.Persistence;
using FreightFlow.SharedKernel;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace FreightFlow.CarrierApi.Grpc;

public sealed class CapacityGrpcService : CapacityService.CapacityServiceBase
{
    private readonly CarrierDbContext _db;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CapacityGrpcService> _logger;

    public CapacityGrpcService(
        CarrierDbContext db,
        IDistributedCache cache,
        ILogger<CapacityGrpcService> logger)
    {
        _db     = db;
        _cache  = cache;
        _logger = logger;
    }

    public override async Task<ReserveCapacityResponse> ReserveCapacity(
        ReserveCapacityRequest request, ServerCallContext context)
    {
        // Idempotency: if this reservation_id has already been processed, return cached result.
        if (!string.IsNullOrEmpty(request.ReservationId))
        {
            var cacheKey = $"reservation:{request.ReservationId}";
            var cached   = await _cache.GetStringAsync(cacheKey, context.CancellationToken);
            if (cached is not null)
            {
                _logger.LogInformation("Returning cached result for reservation {ReservationId}",
                    request.ReservationId);
                return new ReserveCapacityResponse { Success = true };
            }
        }

        if (!Guid.TryParse(request.CarrierId, out var carrierGuid) ||
            !Guid.TryParse(request.LaneId,    out var laneGuid))
        {
            return new ReserveCapacityResponse
            {
                Success = false,
                Reason  = "Invalid carrier_id or lane_id format — expected UUID."
            };
        }

        var carrier = await _db.Carriers
            .Include(c => c.CapacityRecords)
            .FirstOrDefaultAsync(c => c.Id == CarrierId.From(carrierGuid),
                context.CancellationToken);

        if (carrier is null)
        {
            return new ReserveCapacityResponse
            {
                Success = false,
                Reason  = $"Carrier '{carrierGuid}' not found."
            };
        }

        try
        {
            carrier.ReserveCapacity(LaneId.From(laneGuid), request.Volume);
            await _db.SaveChangesAsync(context.CancellationToken);

            // Cache the successful reservation so retries are safe.
            if (!string.IsNullOrEmpty(request.ReservationId))
            {
                await _cache.SetStringAsync(
                    $"reservation:{request.ReservationId}",
                    "ok",
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1)
                    },
                    context.CancellationToken);
            }

            _logger.LogInformation(
                "Reserved {Volume} units on lane {LaneId} for carrier {CarrierId}",
                request.Volume, laneGuid, carrierGuid);

            return new ReserveCapacityResponse { Success = true };
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning(
                "Concurrency conflict reserving capacity for carrier {CarrierId}", carrierGuid);
            return new ReserveCapacityResponse
            {
                Success = false,
                Reason  = "Concurrent reservation conflict — please retry."
            };
        }
        catch (DomainException ex)
        {
            return new ReserveCapacityResponse { Success = false, Reason = ex.Message };
        }
    }

    public override async Task<ReleaseCapacityResponse> ReleaseCapacity(
        ReleaseCapacityRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.CarrierId, out var carrierGuid) ||
            !Guid.TryParse(request.LaneId,    out var laneGuid))
        {
            return new ReleaseCapacityResponse
            {
                Success = false,
                Reason  = "Invalid carrier_id or lane_id format — expected UUID."
            };
        }

        var carrier = await _db.Carriers
            .Include(c => c.CapacityRecords)
            .FirstOrDefaultAsync(c => c.Id == CarrierId.From(carrierGuid),
                context.CancellationToken);

        if (carrier is null)
        {
            return new ReleaseCapacityResponse
            {
                Success = false,
                Reason  = $"Carrier '{carrierGuid}' not found."
            };
        }

        try
        {
            carrier.ReleaseCapacity(LaneId.From(laneGuid), request.Volume);
            await _db.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation(
                "Released {Volume} units on lane {LaneId} for carrier {CarrierId}",
                request.Volume, laneGuid, carrierGuid);

            return new ReleaseCapacityResponse { Success = true };
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ReleaseCapacityResponse
            {
                Success = false,
                Reason  = "Concurrent update conflict — please retry."
            };
        }
        catch (DomainException ex)
        {
            return new ReleaseCapacityResponse { Success = false, Reason = ex.Message };
        }
    }
}
