using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using BettingAnalysis.Data;
using BettingAnalysis.Hubs;
using BettingAnalysis.Data.Repositories;
using BettingAnalysis.Interfaces;
using BettingAnalysis.Middleware;
using BettingAnalysis.Models;
using BettingAnalysis.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;

// ── Serilog: configure before anything that might log ─────────────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore",       LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        "logs/betting-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting BettingAnalysis API");
    await RunApp(args);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static async Task RunApp(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog host integration ──────────────────────────────────────────────
    builder.Host.UseSerilog();

    // ── Global exception handler + problem details ─────────────────────────────
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    // ── Controllers ───────────────────────────────────────────────────────────
    builder.Services.AddControllers()
        .AddJsonOptions(opts =>
            opts.JsonSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter()))
        .ConfigureApiBehaviorOptions(opts =>
        {
            opts.InvalidModelStateResponseFactory = ctx =>
            {
                var errors = ctx.ModelState
                    .Where(e => e.Value?.Errors.Count > 0)
                    .ToDictionary(
                        e => e.Key,
                        e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray());
                return new Microsoft.AspNetCore.Mvc.UnprocessableEntityObjectResult(
                    new { title = "Validation failed", errors });
            };
        });

    builder.Services.AddSignalR();
    builder.Services.AddEndpointsApiExplorer();

    // ── Swagger / OpenAPI ─────────────────────────────────────────────────────
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title       = "Betting Analysis API",
            Version     = "v1",
            Description = "AI-powered sports betting edge finder with Poisson modelling, "
                        + "half-Kelly staking, and 11-rule risk management."
        });

        // JWT bearer in Swagger UI (wired up once auth lands)
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Authorization. Enter: Bearer {token}",
            Name        = "Authorization",
            In          = ParameterLocation.Header,
            Type        = SecuritySchemeType.Http,
            Scheme      = "bearer",
            BearerFormat = "JWT"
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id   = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });

    // ── Database Context ──────────────────────────────────────────────────────
    builder.Services.AddDbContext<BettingDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("BettingDatabase")));

    // ── Repositories ──────────────────────────────────────────────────────────
    builder.Services.AddScoped<IBetRepository, BetRepository>();
    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<IBankrollSnapshotRepository, BankrollSnapshotRepository>();

    // ── Auth service ──────────────────────────────────────────────────────────
    builder.Services.AddScoped<IAuthService, AuthService>();

    // ── JWT bearer authentication ─────────────────────────────────────────────
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidateAudience         = true,
                ValidateLifetime         = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer              = builder.Configuration["Jwt:Issuer"],
                ValidAudience            = builder.Configuration["Jwt:Audience"],
                IssuerSigningKey         = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
            };
            // SignalR WebSockets can't send headers — read JWT from query string instead
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    var token = ctx.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(token) &&
                        ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        ctx.Token = token;
                    return Task.CompletedTask;
                }
            };
        });

    builder.Services.AddAuthorization();

    // ── Health checks ─────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<BettingDbContext>("database", tags: ["ready"])
        .AddCheck<BankrollHealthCheck>("bankroll", tags: ["live"]);

    // ── HTTP client for The Odds API ──────────────────────────────────────────
    builder.Services.AddHttpClient<TheOddsApiService>(c => c.Timeout = TimeSpan.FromSeconds(15));

    // ── Core services ─────────────────────────────────────────────────────────
    builder.Services.AddSingleton<IBettingConfigService, BettingConfigService>();
    builder.Services.AddSingleton<IBankrollService, BankrollService>();
    builder.Services.AddSingleton<IBettingLoggingService, BettingLoggingService>();
    builder.Services.AddSingleton<IValidationService, ValidationService>();
    builder.Services.AddSingleton<ILineMovementService, LineMovementService>();
    builder.Services.AddSingleton<IPoissonService, PoissonService>();
    builder.Services.AddSingleton<IEdgeService, EdgeService>();
    builder.Services.AddSingleton<IBetSizingService, BetSizingService>();
    builder.Services.AddSingleton<ICLVService, CLVService>();
    builder.Services.AddSingleton<IAIValidatorService, AIValidatorService>();
    builder.Services.AddSingleton<IOddsService, OddsService>();
    builder.Services.AddSingleton<IParlayService, ParlayService>();
    builder.Services.AddHostedService<OddsRefreshService>();

    // ── Rate limiting (100 req/min per IP; stricter on bet placement) ─────────
    builder.Services.AddRateLimiter(options =>
    {
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit       = 100,
                Window            = TimeSpan.FromMinutes(1),
            });
        });

        // Tighter limit on bet placement: 20 req/min per IP
        options.AddFixedWindowLimiter("place-bet", o =>
        {
            o.AutoReplenishment = true;
            o.PermitLimit       = 20;
            o.Window            = TimeSpan.FromMinutes(1);
        });

        options.OnRejected = async (ctx, token) =>
        {
            ctx.HttpContext.Response.StatusCode = 429;
            await ctx.HttpContext.Response.WriteAsJsonAsync(
                new { error = "Too many requests. Please retry after 60 seconds." }, token);
        };
    });

    // ── CORS: allow Vite dev server ───────────────────────────────────────────
    builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
        p.WithOrigins(
                "http://localhost:5173",
                "https://localhost:5173",
                "http://localhost:3000")
         .AllowAnyHeader()
         .AllowAnyMethod()
         .AllowCredentials()));  // Required for SignalR WebSocket handshake

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Betting Analysis API v1");
            c.DocumentTitle = "Betting Analysis API";
        });
    }

    // Order matters: exception handler → rate limiter → CORS → auth → controllers
    app.UseExceptionHandler();
    app.UseRateLimiter();
    app.UseCors();
    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate = "HTTP {RequestMethod} {RequestPath} → {StatusCode} ({Elapsed:0.0}ms)";
    });
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapHealthChecks("/health");
    app.MapHub<BettingHub>("/hubs/betting");
    app.MapControllers();

    // Seed default admin user (runs migrations + upserts admin account)
    await DataSeeder.SeedAsync(app.Services, app.Logger);

    app.Run();
}

// Expose Program for WebApplicationFactory in integration tests
public partial class Program { }

// ── Custom health check: bankroll still above 50% ─────────────────────────────
public class BankrollHealthCheck : IHealthCheck
{
    private readonly IBankrollService _bankrollService;

    public BankrollHealthCheck(IBankrollService bankrollService)
    {
        _bankrollService = bankrollService;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var b       = _bankrollService.GetBankroll();
        var initial = b.TotalBankroll + b.CumulativeLoss;  // reconstruct initial approximation
        if (initial <= 0) initial = b.TotalBankroll;

        var healthPct = initial > 0 ? (double)(b.TotalBankroll / initial) * 100 : 100;

        return healthPct switch
        {
            < 50  => Task.FromResult(HealthCheckResult.Unhealthy($"Bankroll critically low: {healthPct:F1}% of starting amount")),
            < 80  => Task.FromResult(HealthCheckResult.Degraded($"Bankroll below optimal: {healthPct:F1}% remaining")),
            _     => Task.FromResult(HealthCheckResult.Healthy($"Bankroll healthy: {healthPct:F1}% remaining"))
        };
    }
}
