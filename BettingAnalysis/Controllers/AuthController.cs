using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BettingAnalysis.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BettingAnalysis.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService    _auth;
    private readonly IUserRepository _users;

    public AuthController(IAuthService auth, IUserRepository users)
    {
        _auth  = auth;
        _users = users;
    }

    // POST /api/auth/login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var result = await _auth.LoginAsync(req.Username, req.Password);

        if (!result.Success)
            return Unauthorized(new { error = result.Error });

        return Ok(new
        {
            token        = result.Token,
            refreshToken = result.RefreshToken,
            user         = new { result.User!.Id, result.User.Username, result.User.Email, result.User.Role }
        });
    }

    // POST /api/auth/register
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        var result = await _auth.RegisterAsync(req.Username, req.Email, req.Password, req.InitialBankroll);

        if (!result.Success)
            return BadRequest(new { error = result.Error });

        return Ok(new
        {
            token        = result.Token,
            refreshToken = result.RefreshToken,
            user         = new { result.User!.Id, result.User.Username, result.User.Email, result.User.Role }
        });
    }

    // POST /api/auth/refresh
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req)
    {
        var result = await _auth.RefreshAsync(req.RefreshToken);

        if (!result.Success)
            return Unauthorized(new { error = result.Error });

        return Ok(new
        {
            token        = result.Token,
            refreshToken = result.RefreshToken,
        });
    }

    // POST /api/auth/logout — revokes the refresh token server-side
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest req)
    {
        await _auth.RevokeAsync(req.RefreshToken);
        return NoContent();
    }

    // POST /api/auth/change-password — requires valid JWT
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
            ?? "0");

        var result = await _auth.ChangePasswordAsync(userId, req.CurrentPassword, req.NewPassword);

        if (!result.Success)
            return BadRequest(new { error = result.Error });

        return NoContent();
    }

    // GET /api/auth/me  — requires valid JWT
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
            ?? "0");

        var user = await _users.GetByIdAsync(userId);
        if (user is null) return NotFound();

        return Ok(new
        {
            user.Id,
            user.Username,
            user.Email,
            user.Role,
            user.CurrentBankroll,
            user.InitialBankroll,
            user.CreatedAt,
            user.LastLoginAt,
        });
    }
}

public record LoginRequest(
    [Required] string Username,
    [Required] string Password);

public record RegisterRequest(
    [Required][MinLength(3)] string Username,
    [Required][EmailAddress] string Email,
    [Required][MinLength(8)] string Password,
    decimal InitialBankroll = 10_000m);

public record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required][MinLength(8)] string NewPassword);

public record RefreshRequest([Required] string RefreshToken);
