using System.ComponentModel.DataAnnotations;

namespace BettingAnalysis.Models;

public class UpdateResultRequest
{
    [Required(ErrorMessage = "Result is required")]
    [RegularExpression("^(Win|Loss)$", ErrorMessage = "Result must be 'Win' or 'Loss'")]
    public string Result { get; set; } = string.Empty;

    [Range(1.01, 1000, ErrorMessage = "ClosingOdds must be between 1.01 and 1000")]
    public decimal? ClosingOdds { get; set; }
}
