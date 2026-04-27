using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace FreightFlow.RfpApi.Middleware;

/// <summary>
/// Intercepts requests that carry an <c>Idempotency-Key</c> header.
/// On the first call the response (status + body) is stored in Redis for 24 hours.
/// Subsequent calls with the same key receive the original response without re-executing the handler.
/// </summary>
public sealed class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;

    public IdempotencyMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IDistributedCache cache)
    {
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keyValues)
            || string.IsNullOrWhiteSpace(keyValues))
        {
            await _next(context);
            return;
        }

        var cacheKey = $"idempotency:{keyValues}";
        var cached   = await cache.GetStringAsync(cacheKey, context.RequestAborted);

        if (cached is not null)
        {
            var stored = JsonSerializer.Deserialize<IdempotencyEntry>(cached)!;
            context.Response.StatusCode  = stored.StatusCode;
            context.Response.ContentType = stored.ContentType;
            await context.Response.WriteAsync(stored.Body, context.RequestAborted);
            return;
        }

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);
        }
        finally
        {
            buffer.Seek(0, SeekOrigin.Begin);
            var body = await new StreamReader(buffer).ReadToEndAsync(context.RequestAborted);

            // Cache only 2xx — errors are never replayed as successes.
            if (context.Response.StatusCode is >= 200 and < 300)
            {
                var entry = new IdempotencyEntry(
                    context.Response.StatusCode,
                    context.Response.ContentType ?? "application/json",
                    body);

                await cache.SetStringAsync(
                    cacheKey,
                    JsonSerializer.Serialize(entry),
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1)
                    },
                    context.RequestAborted);
            }

            buffer.Seek(0, SeekOrigin.Begin);
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
            context.Response.Body = originalBody;
        }
    }

    private sealed record IdempotencyEntry(int StatusCode, string ContentType, string Body);
}
