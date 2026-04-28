using FreightFlow.CarrierApi.Grpc;
using Grpc.Core;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace FreightFlow.WorkflowWorker.Infrastructure.GrpcClients;

/// <summary>
/// Wraps the generated gRPC CapacityServiceClient with a Polly v8 resilience pipeline.
/// Pipeline (outer → inner): Timeout(5s) → CircuitBreaker → Retry(3×, exponential+jitter).
/// Order matters: Retry is innermost so timed-out calls are NOT retried by the retry policy.
/// </summary>
public sealed class CarrierCapacityClient
{
    private readonly CapacityService.CapacityServiceClient _grpcClient;
    private readonly ResiliencePipeline                    _pipeline;
    private readonly ILogger<CarrierCapacityClient>        _logger;

    public CarrierCapacityClient(
        CapacityService.CapacityServiceClient grpcClient,
        ILogger<CarrierCapacityClient>        logger)
    {
        _grpcClient = grpcClient;
        _logger     = logger;

        _pipeline = new ResiliencePipelineBuilder()
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(5)
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio           = 0.5,
                MinimumThroughput      = 5,
                SamplingDuration       = TimeSpan.FromSeconds(30),
                BreakDuration          = TimeSpan.FromSeconds(30),
                OnOpened               = args =>
                {
                    _logger.LogWarning("CarrierCapacityClient circuit breaker opened for {Duration}s",
                        args.BreakDuration.TotalSeconds);
                    return ValueTask.CompletedTask;
                }
            })
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay            = TimeSpan.FromSeconds(1),
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true,
                MaxDelay         = TimeSpan.FromSeconds(8),
                OnRetry          = args =>
                {
                    _logger.LogWarning(args.Outcome.Exception,
                        "gRPC ReserveCapacity retry #{Attempt} after {Delay}ms",
                        args.AttemptNumber + 1, args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task<ReserveCapacityResponse> ReserveCapacityAsync(
        string            carrierId,
        string            laneId,
        int               volume,
        string            reservationId,
        CancellationToken ct = default)
    {
        return await _pipeline.ExecuteAsync(async token =>
        {
            var request = new ReserveCapacityRequest
            {
                CarrierId     = carrierId,
                LaneId        = laneId,
                Volume        = volume,
                ReservationId = reservationId
            };

            return await _grpcClient.ReserveCapacityAsync(request, cancellationToken: token);
        }, ct);
    }

    public async Task<ReleaseCapacityResponse> ReleaseCapacityAsync(
        string            reservationId,
        string            carrierId,
        string            laneId,
        int               volume,
        CancellationToken ct = default)
    {
        return await _pipeline.ExecuteAsync(async token =>
        {
            var request = new ReleaseCapacityRequest
            {
                ReservationId = reservationId,
                CarrierId     = carrierId,
                LaneId        = laneId,
                Volume        = volume
            };

            return await _grpcClient.ReleaseCapacityAsync(request, cancellationToken: token);
        }, ct);
    }
}
