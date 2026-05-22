using BettingAnalysis.Models;

namespace BettingAnalysis.Interfaces;

/// <summary>
/// Service for managing live betting configuration.
/// Allows runtime updates without application restart.
/// </summary>
public interface IBettingConfigService
{
    /// <summary>Gets the current betting configuration.</summary>
    BettingConfig Get();

    /// <summary>Updates the betting configuration with new values.</summary>
    void Update(BettingConfig updated);
}
