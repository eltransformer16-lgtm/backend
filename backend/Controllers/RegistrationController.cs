using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace backend.Controllers;

// Autoregistro de usuarios. Las cuentas quedan desactivadas y con rol de usuario común
// hasta que un administrador las active.
[ApiController]
[Route("api/register")]
public class RegistrationController(
    IUserStore users,
    PasswordService passwords) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (request.BirthDate is not { } birthDate || birthDate > today || birthDate < new DateOnly(1900, 1, 1))
        {
            ModelState.AddModelError(nameof(request.BirthDate), "La fecha de nacimiento no es válida.");
            return ValidationProblem(ModelState);
        }

        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            Email = request.Email.Trim(),
            BirthDate = birthDate,
            PasswordHash = passwords.Hash(request.Password),
            Role = Roles.User,
            IsActive = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        if (!await users.TryAddAsync(user))
            return Conflict(new { message = "No se pudo completar el registro. El usuario o el correo ya están en uso." });

        return StatusCode(StatusCodes.Status201Created, new
        {
            message = "Registro exitoso. Tu cuenta quedó pendiente de activación por un administrador.",
            username = user.Username,
            email = user.Email
        });
    }
}
