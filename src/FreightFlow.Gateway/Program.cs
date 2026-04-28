using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ───────────────────────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.WithProperty("ServiceName", "freight-gateway")
       .Enrich.FromLogContext()
       .WriteTo.Console(new CompactJsonFormatter()));

// ── Config ────────────────────────────────────────────────────────────────────
var jwtKey      = builder.Configuration["Jwt:Key"]!;
var jwtIssuer   = builder.Configuration["Jwt:Issuer"]!;
var jwtAudience = builder.Configuration["Jwt:Audience"]!;

// ── Authentication / JWT ──────────────────────────────────────────────────────
// MapInboundClaims = false keeps claim names as they appear in the token ("sub", "role", etc.)
// rather than mapping to legacy WS-Federation URN names. Downstream services receive the
// forwarded Authorization header and trust the gateway — no re-validation required.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey        = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer          = true,
            ValidIssuer             = jwtIssuer,
            ValidateAudience        = true,
            ValidAudience           = jwtAudience,
            ValidateLifetime        = true,
            ClockSkew               = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization(options =>
    options.AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser()));

// ── Rate limiting ─────────────────────────────────────────────────────────────
// One partitioned policy: anonymous → 100 req/min per remote IP;
// authenticated → 1000 req/min per "sub" claim.
// Rate limiting sits at the gateway so downstream services are not flooded.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("default", httpContext =>
    {
        var sub = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (sub is not null)
        {
            return RateLimitPartition.GetFixedWindowLimiter(
                $"auth:{sub}",
                _ => new FixedWindowRateLimiterOptions
                {
                    Window      = TimeSpan.FromMinutes(1),
                    PermitLimit = 1000,
                    QueueLimit  = 0
                });
        }

        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            $"anon:{ip}",
            _ => new FixedWindowRateLimiterOptions
            {
                Window      = TimeSpan.FromMinutes(1),
                PermitLimit = 100,
                QueueLimit  = 0
            });
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

// ── YARP reverse proxy ────────────────────────────────────────────────────────
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// ── OpenTelemetry ─────────────────────────────────────────────────────────────
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation());

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
// Order matters: auth must populate User before rate limiter partitions by "sub".
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// ── Health check (no auth, no rate limit) ─────────────────────────────────────
app.MapHealthChecks("/health").AllowAnonymous();

// ── JWT token endpoint (LOCAL DEV STUB — replace with a real IdP in production) ─
// Accepts { "sub": "...", "role": "..." } and returns a signed HS256 JWT.
// TRADE-OFF: this stub keeps the local demo self-contained.
// In production: Azure AD B2C, Keycloak, or any OIDC-compliant identity provider.
app.MapPost("/token", (TokenRequest request) =>
{
    var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, request.Sub),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new Claim("role", request.Role)
    };

    var token = new JwtSecurityToken(
        issuer:             jwtIssuer,
        audience:           jwtAudience,
        claims:             claims,
        expires:            DateTime.UtcNow.AddHours(1),
        signingCredentials: creds);

    return Results.Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
}).AllowAnonymous();

// ── Reverse proxy ─────────────────────────────────────────────────────────────
// Routes in appsettings.json carry AuthorizationPolicy and RateLimiterPolicy.
// The gateway validates the JWT; downstream services trust the forwarded header.
app.MapReverseProxy();

app.Run();

// ── Local record types ────────────────────────────────────────────────────────
internal sealed record TokenRequest(string Sub, string Role);
