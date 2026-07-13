# Betting Analysis - Professional Enhancement Roadmap

## Status Update (2026-07-13)

This roadmap was written when the project was 2/14 services tested, JSON-file-backed,
and unauthenticated. Since then, TIER 1 items #1-#4 have shipped:

- **Testing** — every service in `BettingAnalysis/Services/` now has a dedicated test
  file in `BettingAnalysis.Tests/Services/` (151 tests passing). The last two gaps,
  `BettingConfigService` and `OddsRefreshService`, were closed in this pass.
- **Database** — EF Core 8 on SQL Server replaced the JSON file store (see
  `BettingAnalysis/Data/`), with migrations checked into `Migrations/`.
- **Auth** — JWT bearer auth + BCrypt hashing + refresh tokens (`AuthService`,
  `JwtBearer` middleware) replaced the "no security layer" state.
- **Validation/error handling** — `Middleware/GlobalExceptionHandler.cs` maps
  exceptions to `ProblemDetails`; rate limiting (`AddRateLimiter`) and health checks
  (`/health`) are wired in `Program.cs`.

The code snippets below for those items are historical design reference, not a
to-do list — don't re-implement them. **Still open**, in rough priority order:
API versioning (TIER 1 #5, low priority for a single-client app), containerization
(TIER 3 #14 — no `Dockerfile`/`docker-compose.yml` exists yet), and the CI/CD
deploy stage (`.github/workflows/ci.yml` runs build+test only, no deploy job).
TIER 2's Redis/Hangfire and TIER 4 (CQRS, event sourcing, ML.NET) are YAGNI at
this project's current single-user scale — revisit only if that changes.

## Current Status Assessment (original, superseded above)

**Overall Grade:** B+ (Production-Grade Design, Development-Stage Implementation)

**Exceptional Strengths:**
- Outstanding documentation (README is exemplary)
- Sophisticated domain logic (Poisson models, Kelly criterion, 11 risk rules)
- Clean service architecture with excellent separation of concerns
- Thoughtful AI validation layer with nuanced decision-making
- Professional code quality with comprehensive comments
- Rich feature set covering entire betting workflow

**Critical Gaps (as of original writing — see Status Update above):**
- ~~Testing coverage inadequate (15% of services tested - only 2/14)~~ — done
- ~~No database (fundamental production requirement)~~ — done
- ~~No security layer (authentication, authorization, rate limiting)~~ — done
- Limited scalability (singleton services with in-memory state)
- Frontend could benefit from state management library
- No deployment automation or infrastructure-as-code

---

## TIER 1: Critical - Must Have (Implement Immediately)

### 1. Testing Infrastructure (Priority #1) — ✅ DONE (2026-07-13)

**Status:** All 15 services in `BettingAnalysis/Services/` have dedicated tests under
`BettingAnalysis.Tests/Services/`, plus controller-level integration tests. 151 tests
passing. The code below is kept as historical reference for the testing approach
that was followed, not an outstanding task list.

**Implementation Plan:**

#### A. Add Testing Dependencies
```xml
<!-- BettingAnalysis.Tests.csproj -->
<ItemGroup>
  <PackageReference Include="FluentAssertions" Version="6.12.0" />
  <PackageReference Include="Moq" Version="4.20.70" />
  <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.0" />
</ItemGroup>
```

#### B. Unit Tests for ValidationService
```csharp
// ValidationServiceTests.cs
public class ValidationServiceTests
{
    private readonly Mock<IBankrollService> _mockBankrollService;
    private readonly Mock<IBettingConfigService> _mockConfigService;
    private readonly Mock<IBettingLoggingService> _mockLoggingService;
    private readonly ValidationService _validationService;

    public ValidationServiceTests()
    {
        _mockBankrollService = new Mock<IBankrollService>();
        _mockConfigService = new Mock<IBettingConfigService>();
        _mockLoggingService = new Mock<IBettingLoggingService>();

        _validationService = new ValidationService(
            _mockBankrollService.Object,
            _mockConfigService.Object,
            _mockLoggingService.Object
        );
    }

    [Fact]
    public void ShouldRejectBet_WhenDailyLossLimitExceeded()
    {
        // Arrange
        _mockBankrollService.Setup(x => x.GetDailyLoss()).Returns(1500);
        _mockBankrollService.Setup(x => x.CurrentBankroll).Returns(10000);
        _mockConfigService.Setup(x => x.GetConfig()).Returns(new BettingConfig
        {
            DailyLossLimitPercent = 10 // $1000 limit
        });

        var opportunity = new Opportunity { /* ... */ };

        // Act
        var result = _validationService.ValidateBet(opportunity, 100);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Reasons.Should().Contain(r => r.Contains("Daily loss limit"));
    }

    [Fact]
    public void ShouldRejectBet_WhenStopLossTriggered()
    {
        // Arrange
        _mockBankrollService.Setup(x => x.CurrentBankroll).Returns(7500);
        _mockBankrollService.Setup(x => x.InitialBankroll).Returns(10000);
        _mockConfigService.Setup(x => x.GetConfig()).Returns(new BettingConfig
        {
            StopLossPercent = 20 // 20% loss = $8000 threshold
        });

        var opportunity = new Opportunity { /* ... */ };

        // Act
        var result = _validationService.ValidateBet(opportunity, 100);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Reasons.Should().Contain(r => r.Contains("stop-loss"));
    }

    [Fact]
    public void ShouldRejectBet_WhenTiltProtectionActive()
    {
        // Arrange
        _mockBankrollService.Setup(x => x.GetConsecutiveLosses()).Returns(3);
        _mockConfigService.Setup(x => x.GetConfig()).Returns(new BettingConfig
        {
            MaxConsecutiveLosses = 3
        });

        var opportunity = new Opportunity { /* ... */ };

        // Act
        var result = _validationService.ValidateBet(opportunity, 100);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Reasons.Should().Contain(r => r.Contains("consecutive losses"));
    }

    [Fact]
    public void ShouldRejectBet_WhenExposureLimitExceeded()
    {
        // Test exposure limit validation
    }

    [Fact]
    public void ShouldRejectBet_WhenMaxBetsPerMatchExceeded()
    {
        // Test max bets per match validation
    }

    [Fact]
    public void ShouldRejectBet_WhenEdgeBelowThreshold()
    {
        // Test minimum edge threshold
    }

    [Fact]
    public void ShouldRejectBet_WhenOutsideTimingWindow()
    {
        // Test timing window (1h - 2 weeks)
    }

    [Fact]
    public void ShouldRejectBet_WhenTeamIsBlacklisted()
    {
        // Test team blacklist
    }

    [Fact]
    public void ShouldRejectBet_WhenLineMovingAgainst()
    {
        // Test line movement blocking
    }

    [Fact]
    public void ShouldFlagForManualReview_WhenHighEdgeDetected()
    {
        // Test high edge verification (>20%)
    }

    [Fact]
    public void ShouldAcceptBet_WhenAllValidationsPassed()
    {
        // Arrange
        _mockBankrollService.Setup(x => x.CurrentBankroll).Returns(10000);
        _mockBankrollService.Setup(x => x.InitialBankroll).Returns(10000);
        _mockBankrollService.Setup(x => x.GetDailyLoss()).Returns(0);
        _mockBankrollService.Setup(x => x.GetConsecutiveLosses()).Returns(0);
        _mockBankrollService.Setup(x => x.GetTotalExposure()).Returns(0);

        var config = new BettingConfig
        {
            EdgeThreshold = 5,
            MaxStakePercent = 3,
            PreMatchMinHoursAhead = 1,
            PreMatchMaxHoursAhead = 336
        };
        _mockConfigService.Setup(x => x.GetConfig()).Returns(config);

        var opportunity = new Opportunity
        {
            Edge = 8.5,
            KickoffTime = DateTime.Now.AddHours(24),
            LineMovement = "STABLE"
        };

        // Act
        var result = _validationService.ValidateBet(opportunity, 100);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Reasons.Should().BeEmpty();
    }
}
```

#### C. Unit Tests for BankrollService
```csharp
// BankrollServiceTests.cs
public class BankrollServiceTests
{
    [Fact]
    public void DeductStake_ShouldReduceBankroll()
    {
        // Arrange
        var service = new BankrollService();
        var initialBankroll = service.CurrentBankroll;

        // Act
        service.DeductStake(100);

        // Assert
        service.CurrentBankroll.Should().Be(initialBankroll - 100);
    }

    [Fact]
    public void AddWinnings_ShouldIncreaseBankroll()
    {
        // Arrange
        var service = new BankrollService();
        service.DeductStake(100);
        var currentBankroll = service.CurrentBankroll;

        // Act
        service.AddWinnings(250); // $100 stake @ 2.5 odds

        // Assert
        service.CurrentBankroll.Should().Be(currentBankroll + 250);
    }

    [Fact]
    public void RecordLoss_ShouldIncrementConsecutiveLosses()
    {
        // Test consecutive loss tracking
    }

    [Fact]
    public void RecordWin_ShouldResetConsecutiveLosses()
    {
        // Test consecutive loss reset
    }

    [Fact]
    public void GetDailyLoss_ShouldCalculateCorrectly()
    {
        // Test daily loss calculation
    }

    [Fact]
    public void GetHealthScore_ShouldReturnCorrectPercentage()
    {
        // Test health score calculation
    }

    [Fact]
    public void GetTotalExposure_ShouldSumActiveBets()
    {
        // Test exposure calculation
    }

    [Fact]
    public void ThreadSafety_MultipleConcurrentBets_ShouldMaintainConsistency()
    {
        // Arrange
        var service = new BankrollService();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => service.DeductStake(10)));
        }
        Task.WaitAll(tasks.ToArray());

        // Assert
        service.CurrentBankroll.Should().Be(service.InitialBankroll - 1000);
    }
}
```

#### D. Unit Tests for BetSizingService
```csharp
// BetSizingServiceTests.cs
public class BetSizingServiceTests
{
    [Theory]
    [InlineData(10000, 10.0, 2.0, 0.5, 250)] // 5% edge, 2.0 odds, half-Kelly
    [InlineData(10000, 5.0, 1.5, 0.5, 0)]    // No edge at fair odds
    [InlineData(10000, 20.0, 3.0, 0.5, 500)] // High edge
    public void CalculateKellyStake_ShouldReturnCorrectAmount(
        decimal bankroll, decimal edge, decimal odds, decimal kellyFraction, decimal expected)
    {
        // Arrange
        var config = new BettingConfig
        {
            KellyFraction = kellyFraction,
            MaxStakePercent = 3
        };
        var service = new BetSizingService(config);

        // Act
        var stake = service.CalculateKellyStake(bankroll, edge, odds);

        // Assert
        stake.Should().BeApproximately(expected, 1); // Within $1
    }

    [Fact]
    public void CalculateKellyStake_ShouldCapAtMaxStakePercent()
    {
        // Arrange
        var config = new BettingConfig
        {
            KellyFraction = 0.5,
            MaxStakePercent = 3 // $300 max on $10k bankroll
        };
        var service = new BetSizingService(config);

        // Act
        var stake = service.CalculateKellyStake(10000, 50, 2.0); // Huge edge

        // Assert
        stake.Should().Be(300); // Capped at 3%
    }
}
```

#### E. Unit Tests for AIValidatorService
```csharp
// AIValidatorServiceTests.cs
public class AIValidatorServiceTests
{
    [Fact]
    public void ScoreOpportunity_HighEdgeNoFlags_ShouldScoreHigh()
    {
        // Arrange
        var service = new AIValidatorService();
        var opportunity = new Opportunity
        {
            Edge = 12.0,
            Odds = 2.5,
            LineMovement = "STABLE"
        };

        // Act
        var score = service.ScoreOpportunity(opportunity);

        // Assert
        score.Should().BeGreaterThanOrEqualTo(7);
    }

    [Fact]
    public void ClassifyBet_GoodScoreAndEdge_ShouldReturnGoodBet()
    {
        // Test GOOD_BET classification logic
    }

    [Fact]
    public void ClassifyBet_MultipleFlags_ShouldReturnSkip()
    {
        // Test SKIP classification
    }

    [Fact]
    public void IdentifyRiskFlags_LineMovingAgainst_ShouldFlagIt()
    {
        // Test risk flag detection
    }
}
```

#### F. Integration Tests for BettingController
```csharp
// BettingControllerIntegrationTests.cs
public class BettingControllerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public BettingControllerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetOpportunities_ShouldReturn200()
    {
        // Act
        var response = await _client.GetAsync("/Betting/opportunities");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeEmpty();
    }

    [Fact]
    public async Task PlaceBet_WithValidData_ShouldReturn200()
    {
        // Arrange
        var request = new PlaceBetRequest
        {
            OpportunityId = 1,
            ManualStake = 100
        };

        // Act
        var response = await _client.PostAsJsonAsync("/Betting/place-bet", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PlaceBet_WithInsufficientBankroll_ShouldReturn400()
    {
        // Test validation rejection
    }

    [Fact]
    public async Task GetBankroll_ShouldReturnCurrentState()
    {
        // Act
        var response = await _client.GetAsync("/Betting/bankroll");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bankroll = await response.Content.ReadFromJsonAsync<BankrollInfo>();
        bankroll.Should().NotBeNull();
        bankroll.CurrentBankroll.Should().BeGreaterThan(0);
    }
}
```

#### G. Test Coverage Goals
- **Target:** 80% overall code coverage
- **Critical services:** 100% coverage (ValidationService, BankrollService, BetSizingService)
- **Integration tests:** Cover all 16 controller endpoints
- **Edge cases:** Test boundary conditions, null inputs, concurrent access

#### H. Running Tests
```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura

# Generate coverage report
reportgenerator -reports:coverage.cobertura.xml -targetdir:coverage-report
```

---

### 2. Database Layer (Priority #2)

**Current State:** In-memory data + JSON file persistence

**Problems:**
- Data lost if application crashes before JSON write
- No transactions (atomic operations)
- Can't query efficiently (no indexes)
- No concurrent access control
- No relationships between entities
- No backup/recovery strategy

**Solution: Entity Framework Core with SQL Server/PostgreSQL**

#### A. Add NuGet Packages
```xml
<!-- BettingAnalysis.csproj -->
<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.0" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.0.0" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.0" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Tools" Version="8.0.0" />
</ItemGroup>
```

#### B. Create Entity Models
```csharp
// Models/Bet.cs
public class Bet
{
    public int Id { get; set; }
    public string Sport { get; set; }
    public string HomeTeam { get; set; }
    public string AwayTeam { get; set; }
    public string Selection { get; set; }
    public decimal Odds { get; set; }
    public decimal Stake { get; set; }
    public decimal Edge { get; set; }
    public decimal ModelProbability { get; set; }
    public DateTime KickoffTime { get; set; }
    public DateTime PlacedAt { get; set; }
    public string Status { get; set; } // "PENDING", "WON", "LOST", "VOID"
    public decimal? ClosingOdds { get; set; }
    public decimal? CLV { get; set; }
    public int AIScore { get; set; }
    public string AIDecision { get; set; }
    public string[] RiskFlags { get; set; }
    public string LineMovement { get; set; }

    // Navigation properties
    public int UserId { get; set; }
    public User User { get; set; }
}

// Models/User.cs
public class User
{
    public int Id { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }
    public string Role { get; set; } // "Admin", "User"
    public decimal InitialBankroll { get; set; }
    public decimal CurrentBankroll { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public ICollection<Bet> Bets { get; set; }
    public ICollection<BankrollSnapshot> BankrollSnapshots { get; set; }
}

// Models/BankrollSnapshot.cs
public class BankrollSnapshot
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public decimal Bankroll { get; set; }
    public decimal DailyProfit { get; set; }
    public decimal TotalProfit { get; set; }
    public int ConsecutiveLosses { get; set; }
    public DateTime SnapshotAt { get; set; }

    public User User { get; set; }
}

// Models/RejectedBet.cs
public class RejectedBet
{
    public int Id { get; set; }
    public string Sport { get; set; }
    public string Selection { get; set; }
    public decimal Odds { get; set; }
    public decimal Edge { get; set; }
    public string[] RejectionReasons { get; set; }
    public DateTime RejectedAt { get; set; }
    public int UserId { get; set; }
    public User User { get; set; }
}
```

#### C. Create DbContext
```csharp
// Data/BettingDbContext.cs
public class BettingDbContext : DbContext
{
    public BettingDbContext(DbContextOptions<BettingDbContext> options)
        : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Bet> Bets { get; set; }
    public DbSet<BankrollSnapshot> BankrollSnapshots { get; set; }
    public DbSet<RejectedBet> RejectedBets { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Bet configuration
        modelBuilder.Entity<Bet>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Odds).HasPrecision(18, 2);
            entity.Property(e => e.Stake).HasPrecision(18, 2);
            entity.Property(e => e.Edge).HasPrecision(18, 2);
            entity.Property(e => e.ModelProbability).HasPrecision(18, 4);
            entity.Property(e => e.ClosingOdds).HasPrecision(18, 2);
            entity.Property(e => e.CLV).HasPrecision(18, 2);

            entity.HasIndex(e => e.PlacedAt);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => new { e.UserId, e.PlacedAt });

            entity.HasOne(e => e.User)
                .WithMany(u => u.Bets)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // User configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(100);
            entity.Property(e => e.InitialBankroll).HasPrecision(18, 2);
            entity.Property(e => e.CurrentBankroll).HasPrecision(18, 2);

            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
        });

        // BankrollSnapshot configuration
        modelBuilder.Entity<BankrollSnapshot>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Bankroll).HasPrecision(18, 2);
            entity.Property(e => e.DailyProfit).HasPrecision(18, 2);
            entity.Property(e => e.TotalProfit).HasPrecision(18, 2);

            entity.HasIndex(e => new { e.UserId, e.SnapshotAt });
        });

        // Seed default admin user
        modelBuilder.Entity<User>().HasData(new User
        {
            Id = 1,
            Username = "admin",
            Email = "admin@bettinganalysis.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
            Role = "Admin",
            InitialBankroll = 10000,
            CurrentBankroll = 10000,
            CreatedAt = DateTime.UtcNow
        });
    }
}
```

#### D. Repository Pattern
```csharp
// Repositories/IBetRepository.cs
public interface IBetRepository
{
    Task<Bet> AddAsync(Bet bet);
    Task<Bet> GetByIdAsync(int id);
    Task<IEnumerable<Bet>> GetByUserIdAsync(int userId);
    Task<IEnumerable<Bet>> GetRecentBetsAsync(int userId, int count);
    Task<IEnumerable<Bet>> GetPendingBetsAsync(int userId);
    Task<decimal> GetDailyLossAsync(int userId, DateTime date);
    Task<int> GetConsecutiveLossesAsync(int userId);
    Task<decimal> GetTotalExposureAsync(int userId);
    Task UpdateAsync(Bet bet);
}

// Repositories/BetRepository.cs
public class BetRepository : IBetRepository
{
    private readonly BettingDbContext _context;

    public BetRepository(BettingDbContext context)
    {
        _context = context;
    }

    public async Task<Bet> AddAsync(Bet bet)
    {
        _context.Bets.Add(bet);
        await _context.SaveChangesAsync();
        return bet;
    }

    public async Task<IEnumerable<Bet>> GetByUserIdAsync(int userId)
    {
        return await _context.Bets
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.PlacedAt)
            .ToListAsync();
    }

    public async Task<decimal> GetDailyLossAsync(int userId, DateTime date)
    {
        var startOfDay = date.Date;
        var endOfDay = startOfDay.AddDays(1);

        var dailyBets = await _context.Bets
            .Where(b => b.UserId == userId
                && b.PlacedAt >= startOfDay
                && b.PlacedAt < endOfDay)
            .ToListAsync();

        return dailyBets
            .Where(b => b.Status == "LOST")
            .Sum(b => b.Stake);
    }

    public async Task<int> GetConsecutiveLossesAsync(int userId)
    {
        var recentBets = await _context.Bets
            .Where(b => b.UserId == userId && b.Status != "PENDING")
            .OrderByDescending(b => b.PlacedAt)
            .Take(20)
            .ToListAsync();

        int consecutiveLosses = 0;
        foreach (var bet in recentBets)
        {
            if (bet.Status == "LOST")
                consecutiveLosses++;
            else
                break;
        }

        return consecutiveLosses;
    }

    // ... implement other methods
}
```

#### E. Update Program.cs
```csharp
// Program.cs
builder.Services.AddDbContext<BettingDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IBetRepository, BetRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
```

#### F. Connection String
```json
// appsettings.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=BettingAnalysis;Trusted_Connection=True;MultipleActiveResultSets=true"
  }
}
```

#### G. Migrations
```bash
# Create initial migration
dotnet ef migrations add InitialCreate

# Update database
dotnet ef database update

# Add migration for new changes
dotnet ef migrations add AddRejectedBetsTable
dotnet ef database update
```

---

### 3. Authentication & Authorization (Priority #3)

**Current State:** No security - all endpoints are public

**Risks:**
- Anyone can place bets
- Anyone can modify configuration
- No user isolation (single shared bankroll)
- API abuse (unlimited requests)

**Solution: JWT Authentication with Role-Based Authorization**

#### A. Add NuGet Packages
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.0" />
  <PackageReference Include="BCrypt.Net-Next" Version="4.0.3" />
</ItemGroup>
```

#### B. JWT Configuration
```json
// appsettings.json
{
  "Jwt": {
    "Key": "YourSuperSecretKeyMinimum32CharactersLong!",
    "Issuer": "BettingAnalysisAPI",
    "Audience": "BettingAnalysisClient",
    "ExpiryMinutes": 60
  }
}
```

#### C. Authentication Service
```csharp
// Services/AuthenticationService.cs
public interface IAuthenticationService
{
    Task<AuthResult> LoginAsync(string username, string password);
    Task<AuthResult> RegisterAsync(string username, string email, string password);
    string GenerateJwtToken(User user);
}

public class AuthenticationService : IAuthenticationService
{
    private readonly BettingDbContext _context;
    private readonly IConfiguration _configuration;

    public AuthenticationService(BettingDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            return new AuthResult { Success = false, Error = "Invalid credentials" };
        }

        var token = GenerateJwtToken(user);
        return new AuthResult { Success = true, Token = token, User = user };
    }

    public async Task<AuthResult> RegisterAsync(string username, string email, string password)
    {
        if (await _context.Users.AnyAsync(u => u.Username == username))
        {
            return new AuthResult { Success = false, Error = "Username already exists" };
        }

        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = "User",
            InitialBankroll = 10000,
            CurrentBankroll = 10000,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var token = GenerateJwtToken(user);
        return new AuthResult { Success = true, Token = token, User = user };
    }

    public string GenerateJwtToken(User user)
    {
        var securityKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.Now.AddMinutes(
                int.Parse(_configuration["Jwt:ExpiryMinutes"])),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

#### D. Configure Authentication in Program.cs
```csharp
// Program.cs
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdminRole",
        policy => policy.RequireRole("Admin"));
    options.AddPolicy("RequireUserRole",
        policy => policy.RequireRole("User", "Admin"));
});

// ... in app configuration
app.UseAuthentication();
app.UseAuthorization();
```

#### E. Auth Controller
```csharp
// Controllers/AuthController.cs
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authService;

    public AuthController(IAuthenticationService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request.Username, request.Password);

        if (!result.Success)
            return Unauthorized(new { error = result.Error });

        return Ok(new { token = result.Token, user = new
        {
            result.User.Id,
            result.User.Username,
            result.User.Email,
            result.User.Role
        }});
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(
            request.Username,
            request.Email,
            request.Password);

        if (!result.Success)
            return BadRequest(new { error = result.Error });

        return Ok(new { token = result.Token, user = new
        {
            result.User.Id,
            result.User.Username,
            result.User.Email
        }});
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
        // Return user info
        return Ok(new { userId, username = User.Identity.Name });
    }
}
```

#### F. Secure BettingController
```csharp
// Controllers/BettingController.cs
[Authorize] // All endpoints require authentication
[ApiController]
[Route("[controller]")]
public class BettingController : ControllerBase
{
    // Admin-only endpoints
    [Authorize(Policy = "RequireAdminRole")]
    [HttpPut("config")]
    public IActionResult UpdateConfig([FromBody] BettingConfigDto config)
    {
        // Only admins can modify configuration
    }

    [Authorize(Policy = "RequireAdminRole")]
    [HttpPost("reset-bankroll")]
    public IActionResult ResetBankroll()
    {
        // Only admins can reset bankroll
    }

    // User endpoints (auto-filter by current user)
    [HttpGet("opportunities")]
    public async Task<IActionResult> GetOpportunities()
    {
        var userId = GetCurrentUserId();
        // Return opportunities for this user
    }

    [HttpPost("place-bet")]
    public async Task<IActionResult> PlaceBet([FromBody] PlaceBetRequest request)
    {
        var userId = GetCurrentUserId();
        // Place bet for current user only
    }

    private int GetCurrentUserId()
    {
        return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
    }
}
```

#### G. Frontend Authentication
```jsx
// Frontend/src/services/auth.js
const API_BASE = 'http://localhost:5100';

export async function login(username, password) {
  const response = await fetch(`${API_BASE}/api/Auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username, password })
  });

  if (!response.ok) {
    throw new Error('Invalid credentials');
  }

  const data = await response.json();
  localStorage.setItem('token', data.token);
  localStorage.setItem('user', JSON.stringify(data.user));

  return data;
}

export function logout() {
  localStorage.removeItem('token');
  localStorage.removeItem('user');
}

export function getToken() {
  return localStorage.getItem('token');
}

export function getCurrentUser() {
  const user = localStorage.getItem('user');
  return user ? JSON.parse(user) : null;
}

export function isAuthenticated() {
  return !!getToken();
}

// Authenticated fetch wrapper
export async function fetchWithAuth(url, options = {}) {
  const token = getToken();

  const response = await fetch(url, {
    ...options,
    headers: {
      ...options.headers,
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    }
  });

  if (response.status === 401) {
    logout();
    window.location.href = '/login';
  }

  return response;
}
```

---

### 4. Input Validation & Error Handling (Priority #4)

#### A. Data Transfer Objects (DTOs)
```csharp
// DTOs/PlaceBetRequest.cs
public class PlaceBetRequest
{
    [Required(ErrorMessage = "Opportunity ID is required")]
    [Range(1, int.MaxValue, ErrorMessage = "Invalid opportunity ID")]
    public int OpportunityId { get; set; }

    [Range(0.01, 1000000, ErrorMessage = "Stake must be between $0.01 and $1,000,000")]
    public decimal? ManualStake { get; set; }
}

// DTOs/UpdateConfigRequest.cs
public class UpdateConfigRequest
{
    [Range(0.1, 1.0)]
    public decimal? KellyFraction { get; set; }

    [Range(1, 10)]
    public decimal? MaxStakePercent { get; set; }

    [Range(0, 100)]
    public decimal? EdgeThreshold { get; set; }
}
```

#### B. Global Exception Handler
```csharp
// Middleware/GlobalExceptionHandler.cs
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "An unhandled exception occurred");

        var problemDetails = exception switch
        {
            ValidationException validationEx => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation Error",
                Detail = validationEx.Message
            },
            UnauthorizedAccessException => new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized"
            },
            KeyNotFoundException => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Resource Not Found"
            },
            InvalidOperationException invalidOpEx => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid Operation",
                Detail = invalidOpEx.Message
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred"
            }
        };

        httpContext.Response.StatusCode = problemDetails.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}

// Program.cs
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// ... in app configuration
app.UseExceptionHandler();
```

#### C. Model Validation
```csharp
// Enable automatic model validation
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(e => e.Value.Errors.Count > 0)
            .Select(e => new
            {
                Field = e.Key,
                Errors = e.Value.Errors.Select(x => x.ErrorMessage).ToArray()
            })
            .ToArray();

        return new BadRequestObjectResult(new
        {
            Title = "Validation Failed",
            Errors = errors
        });
    };
});
```

---

### 5. API Versioning & DTOs (Priority #5)

#### A. API Versioning
```xml
<PackageReference Include="Asp.Versioning.Mvc" Version="8.0.0" />
<PackageReference Include="Asp.Versioning.Mvc.ApiExplorer" Version="8.0.0" />
```

```csharp
// Program.cs
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// Controller
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public class BettingController : ControllerBase
```

#### B. Response DTOs
```csharp
// DTOs/OpportunityDto.cs
public class OpportunityDto
{
    public int Id { get; set; }
    public string Sport { get; set; }
    public string HomeTeam { get; set; }
    public string AwayTeam { get; set; }
    public string Selection { get; set; }
    public decimal Odds { get; set; }
    public decimal Edge { get; set; }
    public decimal SuggestedStake { get; set; }
    public decimal PotentialProfit { get; set; }
    public DateTime KickoffTime { get; set; }
    public int AIScore { get; set; }
    public string AIDecision { get; set; }
    public string[] RiskFlags { get; set; }
}

// DTOs/BetHistoryDto.cs
public class BetHistoryDto
{
    public int Id { get; set; }
    public string Sport { get; set; }
    public string Selection { get; set; }
    public decimal Odds { get; set; }
    public decimal Stake { get; set; }
    public string Status { get; set; }
    public decimal? Profit { get; set; }
    public decimal? CLV { get; set; }
    public DateTime PlacedAt { get; set; }
}
```

---

## TIER 2: Important - Should Have

### 6. Observability & Monitoring

#### A. Structured Logging with Serilog
```xml
<PackageReference Include="Serilog.AspNetCore" Version="8.0.0" />
<PackageReference Include="Serilog.Sinks.Console" Version="5.0.0" />
<PackageReference Include="Serilog.Sinks.File" Version="5.0.0" />
<PackageReference Include="Serilog.Sinks.Seq" Version="7.0.0" />
```

```csharp
// Program.cs
builder.Host.UseSerilog((context, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "BettingAnalysis")
        .WriteTo.Console()
        .WriteTo.File("logs/betting-.log", rollingInterval: RollingInterval.Day)
        .WriteTo.Seq("http://localhost:5341")); // Seq for log aggregation

// Usage in services
_logger.LogInformation("Bet placed: {BetId} for user {UserId} with stake {Stake}",
    bet.Id, userId, bet.Stake);

_logger.LogWarning("Validation failed for opportunity {OpportunityId}: {Reasons}",
    opportunityId, string.Join(", ", validationResult.Reasons));

_logger.LogError(ex, "Failed to fetch odds from API");
```

#### B. Application Insights
```xml
<PackageReference Include="Microsoft.ApplicationInsights.AspNetCore" Version="2.22.0" />
```

```csharp
builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
});
```

#### C. Health Checks
```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<BettingDbContext>("database")
    .AddUrlGroup(new Uri("https://api.the-odds-api.com"), "odds-api")
    .AddCheck<BankrollHealthCheck>("bankroll");

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            })
        });
        await context.Response.WriteAsync(result);
    }
});

// Custom health check
public class BankrollHealthCheck : IHealthCheck
{
    private readonly IBankrollService _bankrollService;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var healthScore = _bankrollService.GetHealthScore();

        if (healthScore < 50)
            return HealthCheckResult.Unhealthy("Bankroll critically low");

        if (healthScore < 80)
            return HealthCheckResult.Degraded("Bankroll below optimal level");

        return HealthCheckResult.Healthy($"Bankroll healthy at {healthScore}%");
    }
}
```

---

### 7. Distributed Caching with Redis

**Current:** In-memory cache (doesn't work with multiple instances)

```xml
<PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="8.0.0" />
```

```csharp
// Program.cs
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:ConnectionString"];
    options.InstanceName = "BettingAnalysis:";
});

// Services/OddsService.cs
public class OddsService
{
    private readonly IDistributedCache _cache;
    private readonly TheOddsApiService _oddsApiService;

    public async Task<List<Opportunity>> GetOpportunitiesAsync()
    {
        var cacheKey = "opportunities:all";
        var cachedData = await _cache.GetStringAsync(cacheKey);

        if (cachedData != null)
        {
            _logger.LogInformation("Returning cached opportunities");
            return JsonSerializer.Deserialize<List<Opportunity>>(cachedData);
        }

        var opportunities = await _oddsApiService.FetchOpportunitiesAsync();

        await _cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(opportunities),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
            });

        return opportunities;
    }
}
```

---

### 8. Background Jobs with Hangfire

```xml
<PackageReference Include="Hangfire.Core" Version="1.8.9" />
<PackageReference Include="Hangfire.SqlServer" Version="1.8.9" />
<PackageReference Include="Hangfire.AspNetCore" Version="1.8.9" />
```

```csharp
// Program.cs
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHangfireServer();

// Configure recurring jobs
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() }
});

RecurringJob.AddOrUpdate<IOddsService>(
    "refresh-odds",
    service => service.RefreshOddsAsync(),
    "*/5 * * * *"); // Every 5 minutes

RecurringJob.AddOrUpdate<ICLVService>(
    "calculate-clv",
    service => service.CalculateAllCLVAsync(),
    "0 * * * *"); // Every hour

RecurringJob.AddOrUpdate<IBankrollService>(
    "daily-snapshot",
    service => service.CreateDailySnapshotAsync(),
    "0 0 * * *"); // Daily at midnight
```

---

### 9. Rate Limiting

```csharp
// Program.cs (ASP.NET Core 8)
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var username = context.User.Identity?.Name ?? context.Request.Headers.Host.ToString();

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: username,
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            });
    });

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        await context.HttpContext.Response.WriteAsync(
            "Too many requests. Please try again later.", token);
    };
});

app.UseRateLimiter();

// Per-endpoint rate limiting
[EnableRateLimiting("strict")]
[HttpPost("place-bet")]
public async Task<IActionResult> PlaceBet([FromBody] PlaceBetRequest request)
{
    // Limited to fewer requests
}
```

---

### 10. Enhanced API Documentation

```csharp
// Program.cs
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Betting Analysis API",
        Version = "v1",
        Description = "AI-powered sports betting edge finder with Kelly criterion staking and comprehensive risk management",
        Contact = new OpenApiContact
        {
            Name = "Edward Duong",
            Email = "edward@example.com"
        },
        License = new OpenApiLicense
        {
            Name = "MIT License"
        }
    });

    // XML comments
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath);

    // JWT authentication in Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
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
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    // Examples
    c.ExampleFilters();
});

builder.Services.AddSwaggerExamplesFromAssemblyOf<Program>();
```

---

## TIER 3: Nice to Have (Advanced Features)

### 11. CQRS Pattern with MediatR

**Purpose:** Separate read and write operations for better scalability

```xml
<PackageReference Include="MediatR" Version="12.2.0" />
```

```csharp
// Commands/PlaceBetCommand.cs
public record PlaceBetCommand(int UserId, int OpportunityId, decimal? ManualStake)
    : IRequest<BetResult>;

public class PlaceBetHandler : IRequestHandler<PlaceBetCommand, BetResult>
{
    private readonly IBetRepository _betRepository;
    private readonly IValidationService _validationService;
    private readonly IBetSizingService _betSizingService;

    public async Task<BetResult> Handle(PlaceBetCommand request, CancellationToken cancellationToken)
    {
        // Complex business logic here
        var opportunity = await _opportunityRepository.GetByIdAsync(request.OpportunityId);

        var validationResult = _validationService.ValidateBet(opportunity);
        if (!validationResult.IsValid)
            return BetResult.Failed(validationResult.Reasons);

        var stake = request.ManualStake ?? _betSizingService.CalculateKellyStake(/* ... */);

        var bet = new Bet { /* ... */ };
        await _betRepository.AddAsync(bet);

        return BetResult.Success(bet);
    }
}

// Queries/GetOpportunitiesQuery.cs
public record GetOpportunitiesQuery(int UserId, string? Sport, decimal? MinEdge)
    : IRequest<List<OpportunityDto>>;

public class GetOpportunitiesHandler : IRequestHandler<GetOpportunitiesQuery, List<OpportunityDto>>
{
    private readonly IOddsService _oddsService;
    private readonly IDistributedCache _cache;

    public async Task<List<OpportunityDto>> Handle(
        GetOpportunitiesQuery request,
        CancellationToken cancellationToken)
    {
        // Read from cache or database
        var opportunities = await _oddsService.GetOpportunitiesAsync();

        if (!string.IsNullOrEmpty(request.Sport))
            opportunities = opportunities.Where(o => o.Sport == request.Sport).ToList();

        if (request.MinEdge.HasValue)
            opportunities = opportunities.Where(o => o.Edge >= request.MinEdge).ToList();

        return opportunities;
    }
}

// Controller usage
[HttpPost("place-bet")]
public async Task<IActionResult> PlaceBet([FromBody] PlaceBetCommand command)
{
    var result = await _mediator.Send(command);

    if (!result.Success)
        return BadRequest(new { errors = result.Errors });

    return Ok(result.Bet);
}
```

---

### 12. Event Sourcing for Complete Audit Trail

```csharp
// Events/DomainEvent.cs
public abstract class DomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public int UserId { get; set; }
}

public class BetPlacedEvent : DomainEvent
{
    public int BetId { get; set; }
    public decimal Stake { get; set; }
    public decimal Odds { get; set; }
    public string Selection { get; set; }
}

public class BetWonEvent : DomainEvent
{
    public int BetId { get; set; }
    public decimal Profit { get; set; }
}

public class BetLostEvent : DomainEvent
{
    public int BetId { get; set; }
    public decimal Loss { get; set; }
}

public class BetRejectedEvent : DomainEvent
{
    public int OpportunityId { get; set; }
    public string[] RejectionReasons { get; set; }
}

// EventStore/EventStore.cs
public interface IEventStore
{
    Task AppendAsync<T>(T @event) where T : DomainEvent;
    Task<IEnumerable<DomainEvent>> GetEventsAsync(int userId, DateTime from, DateTime to);
}

public class EventStore : IEventStore
{
    private readonly BettingDbContext _context;

    public async Task AppendAsync<T>(T @event) where T : DomainEvent
    {
        var eventEntity = new Event
        {
            EventId = @event.EventId,
            EventType = typeof(T).Name,
            Data = JsonSerializer.Serialize(@event),
            UserId = @event.UserId,
            OccurredAt = @event.OccurredAt
        };

        _context.Events.Add(eventEntity);
        await _context.SaveChangesAsync();
    }

    // Replay events to rebuild state
    public async Task<BankrollState> ReplayEventsAsync(int userId)
    {
        var events = await _context.Events
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync();

        var state = new BankrollState { InitialBankroll = 10000 };

        foreach (var evt in events)
        {
            switch (evt.EventType)
            {
                case nameof(BetPlacedEvent):
                    var placed = JsonSerializer.Deserialize<BetPlacedEvent>(evt.Data);
                    state.CurrentBankroll -= placed.Stake;
                    break;
                case nameof(BetWonEvent):
                    var won = JsonSerializer.Deserialize<BetWonEvent>(evt.Data);
                    state.CurrentBankroll += won.Profit;
                    break;
                // ... handle other events
            }
        }

        return state;
    }
}
```

---

### 13. Frontend State Management

**Current:** Props drilling across components

#### Option A: Context API (Simple)
```jsx
// contexts/BettingContext.jsx
import { createContext, useContext, useState, useCallback } from 'react';
import { fetchWithAuth } from '../services/auth';

const BettingContext = createContext();

export function BettingProvider({ children }) {
  const [opportunities, setOpportunities] = useState([]);
  const [bankroll, setBankroll] = useState(null);
  const [betHistory, setBetHistory] = useState([]);
  const [loading, setLoading] = useState(false);

  const refreshOpportunities = useCallback(async () => {
    setLoading(true);
    try {
      const response = await fetchWithAuth('/Betting/opportunities');
      const data = await response.json();
      setOpportunities(data);
    } catch (error) {
      console.error('Failed to fetch opportunities:', error);
    } finally {
      setLoading(false);
    }
  }, []);

  const refreshBankroll = useCallback(async () => {
    const response = await fetchWithAuth('/Betting/bankroll');
    const data = await response.json();
    setBankroll(data);
  }, []);

  const placeBet = useCallback(async (opportunityId, manualStake) => {
    const response = await fetchWithAuth('/Betting/place-bet', {
      method: 'POST',
      body: JSON.stringify({ opportunityId, manualStake })
    });

    if (response.ok) {
      await Promise.all([refreshOpportunities(), refreshBankroll()]);
      return true;
    }
    return false;
  }, [refreshOpportunities, refreshBankroll]);

  return (
    <BettingContext.Provider value={{
      opportunities,
      bankroll,
      betHistory,
      loading,
      refreshOpportunities,
      refreshBankroll,
      placeBet
    }}>
      {children}
    </BettingContext.Provider>
  );
}

export function useBetting() {
  const context = useContext(BettingContext);
  if (!context) {
    throw new Error('useBetting must be used within BettingProvider');
  }
  return context;
}

// Usage in components
function OpportunitiesTable() {
  const { opportunities, loading, placeBet } = useBetting();

  // No more props drilling!
}
```

#### Option B: Zustand (Lightweight, more powerful)
```jsx
// stores/bettingStore.js
import create from 'zustand';
import { fetchWithAuth } from '../services/auth';

export const useBettingStore = create((set, get) => ({
  opportunities: [],
  bankroll: null,
  betHistory: [],
  loading: false,

  refreshOpportunities: async () => {
    set({ loading: true });
    try {
      const response = await fetchWithAuth('/Betting/opportunities');
      const data = await response.json();
      set({ opportunities: data });
    } finally {
      set({ loading: false });
    }
  },

  refreshBankroll: async () => {
    const response = await fetchWithAuth('/Betting/bankroll');
    const data = await response.json();
    set({ bankroll: data });
  },

  placeBet: async (opportunityId, manualStake) => {
    const response = await fetchWithAuth('/Betting/place-bet', {
      method: 'POST',
      body: JSON.stringify({ opportunityId, manualStake })
    });

    if (response.ok) {
      await Promise.all([
        get().refreshOpportunities(),
        get().refreshBankroll()
      ]);
      return true;
    }
    return false;
  }
}));

// Usage
function OpportunitiesTable() {
  const { opportunities, loading, placeBet } = useBettingStore();
  // Clean and simple!
}
```

---

### 14. Containerization

```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["BettingAnalysis/BettingAnalysis.csproj", "BettingAnalysis/"]
RUN dotnet restore "BettingAnalysis/BettingAnalysis.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/BettingAnalysis"
RUN dotnet build "BettingAnalysis.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BettingAnalysis.csproj" -c Release -o /app/publish

# Final stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 80
EXPOSE 443

ENTRYPOINT ["dotnet", "BettingAnalysis.dll"]
```

```yaml
# docker-compose.yml
version: '3.8'

services:
  api:
    build:
      context: .
      dockerfile: Dockerfile
    ports:
      - "5100:80"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ConnectionStrings__DefaultConnection=Server=db;Database=BettingAnalysis;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True
      - Redis__ConnectionString=redis:6379
      - OddsApiKey=${ODDS_API_KEY}
    depends_on:
      - db
      - redis
    networks:
      - betting-network

  db:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=YourStrong@Passw0rd
    ports:
      - "1433:1433"
    volumes:
      - mssql-data:/var/opt/mssql
    networks:
      - betting-network

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    volumes:
      - redis-data:/data
    networks:
      - betting-network

  seq:
    image: datalust/seq:latest
    environment:
      - ACCEPT_EULA=Y
    ports:
      - "5341:80"
    volumes:
      - seq-data:/data
    networks:
      - betting-network

volumes:
  mssql-data:
  redis-data:
  seq-data:

networks:
  betting-network:
    driver: bridge
```

```bash
# Run with Docker Compose
docker-compose up -d

# View logs
docker-compose logs -f api

# Stop
docker-compose down
```

---

### 15. CI/CD Pipeline

```yaml
# .github/workflows/dotnet.yml
name: .NET CI/CD

on:
  push:
    branches: [ master ]
  pull_request:
    branches: [ master ]

env:
  DOTNET_VERSION: '8.0.x'
  AZURE_WEBAPP_NAME: betting-analysis-api

jobs:
  build-and-test:
    runs-on: ubuntu-latest

    steps:
    - uses: actions/checkout@v3

    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --no-restore --configuration Release

    - name: Run tests
      run: dotnet test --no-build --verbosity normal --collect:"XPlat Code Coverage" --configuration Release

    - name: Upload coverage to Codecov
      uses: codecov/codecov-action@v3
      with:
        files: '**/coverage.cobertura.xml'
        fail_ci_if_error: true

    - name: Publish
      run: dotnet publish BettingAnalysis/BettingAnalysis.csproj -c Release -o ./publish

    - name: Upload artifact
      uses: actions/upload-artifact@v3
      with:
        name: webapp
        path: ./publish

  deploy:
    needs: build-and-test
    runs-on: ubuntu-latest
    if: github.ref == 'refs/heads/master'

    steps:
    - name: Download artifact
      uses: actions/download-artifact@v3
      with:
        name: webapp
        path: ./publish

    - name: Deploy to Azure Web App
      uses: azure/webapps-deploy@v2
      with:
        app-name: ${{ env.AZURE_WEBAPP_NAME }}
        publish-profile: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE }}
        package: ./publish

  code-quality:
    runs-on: ubuntu-latest

    steps:
    - uses: actions/checkout@v3

    - name: Run SonarCloud Scan
      uses: SonarSource/sonarcloud-github-action@master
      env:
        GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
```

---

## TIER 4: Advanced - Production-Ready Features

### 16. Machine Learning Pipeline with ML.NET

```csharp
// ML/BetQualityModel.cs
public class BetQualityData
{
    [LoadColumn(0)] public float Edge { get; set; }
    [LoadColumn(1)] public float Odds { get; set; }
    [LoadColumn(2)] public float TimeToKickoff { get; set; }
    [LoadColumn(3)] public float LineMovement { get; set; }
    [LoadColumn(4)] public bool IsHomeTeam { get; set; }
    [LoadColumn(5)] public float AIScore { get; set; }
    [LoadColumn(6)] public bool WasWinner { get; set; } // Label
}

public class BetQualityPrediction
{
    [ColumnName("PredictedLabel")]
    public bool WillWin { get; set; }

    public float Probability { get; set; }
    public float Score { get; set; }
}

// Services/MLPredictionService.cs
public class MLPredictionService
{
    private readonly MLContext _mlContext;
    private ITransformer _model;
    private readonly string _modelPath = "BetQualityModel.zip";

    public MLPredictionService()
    {
        _mlContext = new MLContext(seed: 0);
        LoadModel();
    }

    public void TrainModel(IEnumerable<BetQualityData> trainingData)
    {
        var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

        var pipeline = _mlContext.Transforms.CopyColumns("Label", nameof(BetQualityData.WasWinner))
            .Append(_mlContext.Transforms.Concatenate("Features",
                nameof(BetQualityData.Edge),
                nameof(BetQualityData.Odds),
                nameof(BetQualityData.TimeToKickoff),
                nameof(BetQualityData.LineMovement),
                nameof(BetQualityData.IsHomeTeam),
                nameof(BetQualityData.AIScore)))
            .Append(_mlContext.BinaryClassification.Trainers.LightGbm());

        _model = pipeline.Fit(dataView);
        _mlContext.Model.Save(_model, dataView.Schema, _modelPath);
    }

    public BetQualityPrediction Predict(Opportunity opportunity)
    {
        var predictionEngine = _mlContext.Model.CreatePredictionEngine<BetQualityData, BetQualityPrediction>(_model);

        var input = new BetQualityData
        {
            Edge = (float)opportunity.Edge,
            Odds = (float)opportunity.Odds,
            TimeToKickoff = (float)(opportunity.KickoffTime - DateTime.Now).TotalHours,
            LineMovement = opportunity.LineMovement == "STEAMING" ? 1f :
                          opportunity.LineMovement == "DRIFTING" ? -1f : 0f,
            IsHomeTeam = opportunity.Selection.Contains(opportunity.HomeTeam),
            AIScore = opportunity.AIScore
        };

        return predictionEngine.Predict(input);
    }

    private void LoadModel()
    {
        if (File.Exists(_modelPath))
        {
            _model = _mlContext.Model.Load(_modelPath, out _);
        }
    }
}

// Nightly training job
RecurringJob.AddOrUpdate<MLPredictionService>(
    "train-ml-model",
    service => service.TrainModelFromHistoricalBets(),
    "0 2 * * *"); // 2 AM daily
```

---

### 17. Real-Time Updates with SignalR

```xml
<PackageReference Include="Microsoft.AspNetCore.SignalR" Version="8.0.0" />
```

```csharp
// Hubs/BettingHub.cs
public class BettingHub : Hub
{
    public async Task SubscribeToOddsUpdates(string sport)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"sport-{sport}");
        await Clients.Caller.SendAsync("Subscribed", $"Subscribed to {sport} updates");
    }

    public async Task UnsubscribeFromOddsUpdates(string sport)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"sport-{sport}");
    }
}

// Services/OddsRefreshService.cs
public class OddsRefreshService : BackgroundService
{
    private readonly IHubContext<BettingHub> _hubContext;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var opportunities = await FetchOpportunities();

            // Broadcast to all connected clients
            await _hubContext.Clients.All.SendAsync("OpportunitiesUpdated", opportunities);

            // Broadcast to specific sports
            var groupedBySport = opportunities.GroupBy(o => o.Sport);
            foreach (var group in groupedBySport)
            {
                await _hubContext.Clients.Group($"sport-{group.Key}")
                    .SendAsync("OpportunitiesUpdated", group.ToList());
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}

// Program.cs
builder.Services.AddSignalR();
app.MapHub<BettingHub>("/hubs/betting");
```

```jsx
// Frontend integration
import * as signalR from '@microsoft/signalr';

function OpportunitiesTable() {
  const [opportunities, setOpportunities] = useState([]);

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('http://localhost:5100/hubs/betting', {
        accessTokenFactory: () => getToken()
      })
      .withAutomaticReconnect()
      .build();

    connection.on('OpportunitiesUpdated', (newOpportunities) => {
      setOpportunities(newOpportunities);
      toast.success('Opportunities updated in real-time!');
    });

    connection.start()
      .then(() => connection.invoke('SubscribeToOddsUpdates', 'EPL'))
      .catch(err => console.error('SignalR error:', err));

    return () => connection.stop();
  }, []);

  return (
    <div>
      {opportunities.map(opp => <OpportunityRow key={opp.id} opportunity={opp} />)}
    </div>
  );
}
```

---

## Implementation Timeline

### Week 1-2: Testing (TIER 1 #1) ✅ START HERE
- Set up testing infrastructure
- Write unit tests for ValidationService, BankrollService, BetSizingService
- Write integration tests for BettingController
- Target: 80% code coverage

### Week 3-4: Database (TIER 1 #2)
- Design and implement Entity Framework Core models
- Create DbContext and repositories
- Run migrations
- Migrate from JSON file to database

### Week 5-6: Security (TIER 1 #3-5)
- Implement JWT authentication
- Add role-based authorization
- Create DTOs and input validation
- Implement global error handling
- Add API versioning

### Week 7-8: Infrastructure (TIER 2 #6-10)
- Set up Serilog + Seq logging
- Implement Redis caching
- Add Hangfire background jobs
- Configure rate limiting
- Enhance Swagger documentation

### Week 9-10: DevOps (TIER 3 #14-15)
- Create Dockerfile and docker-compose.yml
- Set up GitHub Actions CI/CD
- Configure health checks
- Deploy to Azure/AWS

### Week 11+: Advanced Features (TIER 4)
- Implement CQRS with MediatR
- Add event sourcing
- Integrate ML.NET for predictions
- Implement SignalR real-time updates
- Add frontend state management

---

## Success Metrics

After completing enhancements:

**Code Quality:**
- ✅ 80%+ test coverage
- ✅ 0 critical security vulnerabilities
- ✅ A+ rating on SonarCloud
- ✅ All PRs pass CI checks

**Performance:**
- ✅ API response time <200ms (p95)
- ✅ Database queries <50ms
- ✅ Redis cache hit rate >90%
- ✅ Can handle 1000 concurrent users

**Reliability:**
- ✅ 99.9% uptime
- ✅ Zero data loss
- ✅ Automatic failover
- ✅ Complete audit trail

**Security:**
- ✅ JWT authentication on all endpoints
- ✅ Role-based authorization
- ✅ Rate limiting prevents abuse
- ✅ Secrets in Azure Key Vault (not appsettings.json)

**Observability:**
- ✅ Centralized logging with Seq
- ✅ Application Insights dashboards
- ✅ Health check endpoints
- ✅ Distributed tracing

---

## Resources for Learning

**Testing:**
- xUnit docs: https://xunit.net/
- Moq tutorial: https://github.com/moq/moq4/wiki/Quickstart
- FluentAssertions: https://fluentassertions.com/

**Entity Framework Core:**
- Official docs: https://learn.microsoft.com/en-us/ef/core/
- Migrations: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/

**Authentication:**
- JWT in ASP.NET Core: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/
- Role-based authorization: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles

**DevOps:**
- Docker for .NET: https://learn.microsoft.com/en-us/dotnet/core/docker/
- GitHub Actions: https://docs.github.com/en/actions

**Advanced:**
- MediatR: https://github.com/jbogard/MediatR
- SignalR: https://learn.microsoft.com/en-us/aspnet/core/signalr/
- ML.NET: https://dotnet.microsoft.com/en-us/apps/machinelearning-ai/ml-dotnet

---

**Next Step:** Start with comprehensive testing (Week 1-2). Let me know when ready to begin!
