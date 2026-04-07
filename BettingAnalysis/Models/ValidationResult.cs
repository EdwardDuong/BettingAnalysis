namespace BettingAnalysis.Models;

/// <summary>
/// Result of the ValidationService gate check.
/// Violations are hard blocks — bet is rejected.
/// Warnings are soft alerts — bet is allowed but flagged.
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; } = true;

    /// <summary>Hard blocks — each represents a rule violation that prevents the bet.</summary>
    public List<string> Violations { get; set; } = new();

    /// <summary>Soft warnings — bet is allowed but requires attention.</summary>
    public List<string> Warnings { get; set; } = new();

    public void Fail(string reason)
    {
        IsValid = false;
        Violations.Add(reason);
    }

    public void Warn(string reason) => Warnings.Add(reason);
}
