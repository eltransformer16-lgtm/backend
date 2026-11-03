using System.IdentityModel.Tokens.Jwt;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    IUserStore users,
    PasswordService passwords,
    TokenService tokens) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    public IActionResult Register(RegisterRequest request)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            PasswordHash = passwords.Hash(request.Password),
            CreatedAtUtc = DateTime.UtcNow
        };

        if (!users.TryAdd(user))
            return Conflict(new { message = "El nombre de usuario ya existe." });

        return StatusCode(StatusCodes.Status201Created, new { user.Id, user.Username });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public ActionResult<AuthResponse> Login(LoginRequest request)
    {
        var user = users.FindByUsername(request.Username);

        // Se verifica siempre un hash (real o falso) para no revelar si el usuario existe.
        var hash = user?.PasswordHash ?? passwords.DummyHash;
        var passwordOk = passwords.Verify(request.Password, hash);

        if (user is null || !passwordOk)
            return Unauthorized(new { message = "Usuario o contraseña incorrectos." });

        var token = tokens.CreateToken(user, out var expiresAtUtc);
        return Ok(new AuthResponse(token, expiresAtUtc, user.Username));
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new
        {
            id = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
            username = User.FindFirst(JwtRegisteredClaimNames.UniqueName)?.Value
        });
    }
}
