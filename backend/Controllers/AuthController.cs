using System.Security.Claims;
using backend.Data;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _jwt;
    private readonly RefreshTokenService _refresh;

    public AuthController(AppDbContext db, JwtTokenService jwt, RefreshTokenService refresh)
    {
        _db = db;
        _jwt = jwt;
        _refresh = refresh;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        if (req.Password != req.PasswordConfirm)
            return BadRequest(new { message = "La contraseña y su confirmación no coinciden." });

        var username = req.Username.Trim();
        var normUser = username.ToUpperInvariant();
        var email = req.Email.Trim().ToLowerInvariant();
        var normEmail = email.ToUpperInvariant();

        var exists = await _db.Users.AnyAsync(u => u.NormalizedUsername == normUser || u.NormalizedEmail == normEmail);
        if (exists)
            return Conflict(new { message = "El nombre de usuario o correo ya está registrado." });

        var user = new User
        {
            Username = username,
            NormalizedUsername = normUser,
            Email = email,
            NormalizedEmail = normEmail,
            PasswordHash = PasswordHasher.Hash(req.Password),
            BirthDate = req.BirthDate,
            Role = "User",
            IsActive = false,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return StatusCode(201, new
        {
            message = "Registro exitoso. Tu cuenta está pendiente de activación por un administrador.",
            user = ToProfile(user)
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var normInput = req.UsernameOrEmail.Trim().ToUpperInvariant();

        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.NormalizedUsername == normInput || u.NormalizedEmail == normInput);

        if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "Credenciales incorrectas." });

        if (!user.IsActive)
            return StatusCode(403, new { message = "Tu cuenta está desactivada. Contacta a un administrador." });

        var (accessToken, expiresIn) = _jwt.CreateAccessToken(user);
        var refreshToken = await _refresh.IssueAsync(user.Id);

        return Ok(new AuthResponse(accessToken, expiresIn, refreshToken, ToProfile(user)));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req)
    {
        var (ok, user, newRefresh, error) = await _refresh.RotateAsync(req.RefreshToken);
        if (!ok || user is null || newRefresh is null)
            return Unauthorized(new { message = error ?? "No se pudo renovar la sesión." });

        var (accessToken, expiresIn) = _jwt.CreateAccessToken(user);
        return Ok(new AuthResponse(accessToken, expiresIn, newRefresh, ToProfile(user)));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest req)
    {
        await _refresh.RevokeAsync(req.RefreshToken);
        return Ok(new { message = "Sesión cerrada correctamente." });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId))
            return Unauthorized();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null || !user.IsActive)
            return Unauthorized();

        return Ok(ToProfile(user));
    }

    private static UserProfileResponse ToProfile(User u) =>
        new(u.Id, u.Username, u.Email, u.BirthDate, u.Role, u.IsActive, u.CreatedAtUtc, u.UpdatedAtUtc);
}