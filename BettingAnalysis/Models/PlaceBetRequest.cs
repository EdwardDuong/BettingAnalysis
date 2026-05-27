using System.ComponentModel.DataAnnotations;

namespace BettingAnalysis.Models;

public class PlaceBetRequest
{
    [Required(ErrorMessage = "MatchId is required")]
    [StringLength(100, MinimumLength = 1)]
    public string MatchId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Outcome is required")]
    [RegularExpression("^(Home|Away|Draw)$", ErrorMessage = "Outcome must be 'Home', 'Away', or 'Draw'")]
    public string Outcome { get; set; } = string.Empty;

    [Range(0.01, 100_000, ErrorMessage = "CustomStake must be between $0.01 and $100,000")]
    public decimal? CustomStake { get; set; }
}
