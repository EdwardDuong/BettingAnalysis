using BettingAnalysis.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Betting Analysis API", Version = "v1" });
});

// HttpClient for The Odds API
builder.Services.AddHttpClient<TheOddsApiService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

// Singletons: in-memory state shared across requests (replace with scoped + DB in production)
builder.Services.AddSingleton<BankrollService>();
builder.Services.AddSingleton<BettingLoggingService>();
builder.Services.AddSingleton<PoissonService>();
builder.Services.AddSingleton<EdgeService>();
builder.Services.AddSingleton<OddsService>();
builder.Services.AddSingleton<BetSizingService>();

// ── CORS: allow React dev server ──────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",   // Vite frontend
                "https://localhost:5173",
                "http://localhost:3000"    // CRA fallback
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// ── Middleware ────────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();          // Must be before UseAuthorization
app.UseAuthorization();
app.MapControllers();

app.Run();
