using BettingAnalysis.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(opts =>
        opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new() { Title = "Betting Analysis API", Version = "v1" }));

// ── HTTP client for The Odds API ──────────────────────────────────────────────
builder.Services.AddHttpClient<TheOddsApiService>(c => c.Timeout = TimeSpan.FromSeconds(15));

// ── Singletons (shared state + config) ───────────────────────────────────────
builder.Services.AddSingleton<BettingConfigService>();   // Live-editable config
builder.Services.AddSingleton<BankrollService>();
builder.Services.AddSingleton<BettingLoggingService>();

// ── Stateless services ────────────────────────────────────────────────────────
builder.Services.AddSingleton<PoissonService>();
builder.Services.AddSingleton<EdgeService>();
builder.Services.AddSingleton<LineMovementService>();
builder.Services.AddSingleton<CLVService>();
builder.Services.AddSingleton<BetSizingService>();
builder.Services.AddSingleton<AIValidatorService>();
builder.Services.AddSingleton<OddsService>();
builder.Services.AddSingleton<ValidationService>();
builder.Services.AddSingleton<ParlayService>();

// ── CORS: allow Vite dev server ───────────────────────────────────────────────
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:5173", "https://localhost:5173", "http://localhost:3000")
     .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }

app.UseCors();
app.UseAuthorization();
app.MapControllers();
app.Run();
