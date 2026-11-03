using System.IdentityModel.Tokens.Jwt;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace backend.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    IUserStore users,
    PasswordService passwords,
    TokenService tokens,
    RefreshTokenService refreshTokens) : ControllerBase
{
    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 15;

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            Email = request.Email.Trim(),
            PasswordHash = passwords.Hash(request.Password),
            CreatedAtUtc = DateTime.UtcNow
        };

        if (!await users.TryAddAsync(user))
            return Conflict(new { message = "No se pudo completar el registro. El usuario o el correo ya están en uso." });

        return StatusCode(StatusCodes.Status201Created, new { user.Id, user.Username, user.Email });
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);

        if (user?.LockoutEndUtc is { } lockoutEnd && lockoutEnd > DateTime.UtcNow)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { message = "Demasiados intentos fallidos. Intenta de nuevo más tarde." });

        // Se verifica siempre un hash (real o falso) para no revelar si el correo existe.
        var hash = user?.PasswordHash ?? passwords.DummyHash;
        var passwordOk = passwords.Verify(request.Password, hash);

        if (user is null || !passwordOk || !user.IsActive)
        {
            if (user is not null && !passwordOk)
            {
                user.FailedLoginCount++;
                if (user.FailedLoginCount >= MaxFailedAttempts)
                {
                    user.LockoutEndUtc = DateTime.UtcNow.AddMinutes(LockoutMinutes);
                    user.FailedLoginCount = 0;
                }
                await users.SaveChangesAsync();
            }

            return Unauthorized(new { message = "Correo o contraseña incorrectos." });
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAtUtc = DateTime.UtcNow;
        await users.SaveChangesAsync();

        return Ok(await BuildAuthResponseAsync(user));
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request)
    {
        var result = await refreshTokens.RotateAsync(request.RefreshToken);
        if (result is null)
            return Unauthorized(new { message = "Sesión inválida o expirada. Inicia sesión de nuevo." });

        return Ok(await BuildAuthResponseAsync(result.User, result.RefreshToken));
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest request)
    {
        await refreshTokens.RevokeAsync(request.RefreshToken);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new
        {
            id = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
            username = User.FindFirst(JwtRegisteredClaimNames.UniqueName)?.Value,
            email = User.FindFirst(JwtRegisteredClaimNames.Email)?.Value,
            role = User.FindFirst("role")?.Value
        });
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(User user, string? refreshToken = null)
    {
        var accessToken = tokens.CreateToken(user, out var expiresAtUtc);
        var refresh = refreshToken ?? await refreshTokens.IssueAsync(user.Id);
        return new AuthResponse(accessToken, expiresAtUtc, refresh, user.Username);
    }
}
