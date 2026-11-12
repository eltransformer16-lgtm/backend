using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using backend.Data;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace backend.Controllers;

// Módulo de usuarios: solo para administradores.
[ApiController]
[Route("api/users")]
[Authorize(Roles = Roles.Admin)]
public class UsersController(AppDbContext db, PasswordService passwords) : ControllerBase
{
    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : Guid.Empty;

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    // Texto para buscar subcadenas con ILIKE (se escapan los comodines del usuario).
    private static string LikePattern(string value) =>
        "%" + value.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static UserDto ToDto(User u, bool hasRefreshToken) => new(
        u.Id, u.Username, u.Email, u.BirthDate, u.Role, u.IsActive,
        hasRefreshToken, u.CreatedAtUtc, u.UpdatedAtUtc, u.LastLoginAtUtc);

    private bool TryGetBirthDate(DateOnly? input, out DateOnly birthDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (input is { } value && value <= today && value >= new DateOnly(1900, 1, 1))
        {
            birthDate = value;
            return true;
        }

        birthDate = default;
        ModelState.AddModelError("BirthDate", "La fecha de nacimiento no es válida.");
        return false;
    }

    // Lista paginada. Los filtros de texto buscan subcadenas.
    [HttpGet]
    public async Task<ActionResult<PagedResult<UserDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? name = null,
        [FromQuery] string? email = null,
        [FromQuery] string? birthDate = null,
        [FromQuery] bool? isAdmin = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] bool? hasRefreshToken = null)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<User> query = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(name))
        {
            var pattern = LikePattern(name);
            query = query.Where(u => EF.Functions.ILike(u.Username, pattern));
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            var pattern = LikePattern(email);
            query = query.Where(u => EF.Functions.ILike(u.Email, pattern));
        }

        if (isAdmin == true)
            query = query.Where(u => u.Role == Roles.Admin);
        else if (isAdmin == false)
            query = query.Where(u => u.Role != Roles.Admin);

        if (isActive is { } active)
            query = query.Where(u => u.IsActive == active);

        if (hasRefreshToken == true)
            query = query.Where(u => u.RefreshToken != null);
        else if (hasRefreshToken == false)
            query = query.Where(u => u.RefreshToken == null);

        var projected = query
            .OrderByDescending(u => u.CreatedAtUtc)
            .ThenBy(u => u.Username)
            .Select(u => new UserDto(
                u.Id, u.Username, u.Email, u.BirthDate, u.Role, u.IsActive,
                u.RefreshToken != null, u.CreatedAtUtc, u.UpdatedAtUtc, u.LastLoginAtUtc));

        // La fecha de nacimiento se filtra como texto "aaaa-mm-dd" (subcadena).
        if (!string.IsNullOrWhiteSpace(birthDate))
        {
            var text = birthDate.Trim();
            var all = await projected.ToListAsync();
            var filtered = all
                .Where(u => u.BirthDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture).Contains(text))
                .ToList();

            var pageItems = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return new PagedResult<UserDto>(pageItems, filtered.Count, page, pageSize);
        }

        var total = await projected.CountAsync();
        var items = await projected.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedResult<UserDto>(items, total, page, pageSize);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request)
    {
        if (!TryGetBirthDate(request.BirthDate, out var birthDate))
            return ValidationProblem(ModelState);

        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            NormalizedUsername = Normalize(request.Username),
            Email = request.Email.Trim(),
            NormalizedEmail = Normalize(request.Email),
            BirthDate = birthDate,
            PasswordHash = passwords.Hash(request.Password),
            Role = request.Role,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Conflict(new { message = "El nombre de usuario o el correo ya están en uso." });
        }

        return StatusCode(StatusCodes.Status201Created, ToDto(user, false));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateUserRequest request)
    {
        if (!TryGetBirthDate(request.BirthDate, out var birthDate))
            return ValidationProblem(ModelState);

        var user = await db.Users.Include(u => u.RefreshToken).FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return NotFound(new { message = "El usuario no existe." });

        // Un administrador no puede quitarse a sí mismo el rol de administrador.
        if (id == CurrentUserId && request.Role != Roles.Admin)
            return BadRequest(new { message = "Un administrador no puede quitarse a sí mismo el rol de administrador." });

        user.Username = request.Username;
        user.NormalizedUsername = Normalize(request.Username);
        user.Email = request.Email.Trim();
        user.NormalizedEmail = Normalize(request.Email);
        user.BirthDate = birthDate;
        user.Role = request.Role;
        user.UpdatedAtUtc = DateTime.UtcNow;

        var hasRefreshToken = user.RefreshToken is not null;

        // Al cambiar la contraseña, el refresh token del usuario se revoca.
        if (!string.IsNullOrEmpty(request.Password))
        {
            user.PasswordHash = passwords.Hash(request.Password);
            if (user.RefreshToken is not null)
            {
                db.RefreshTokens.Remove(user.RefreshToken);
                hasRefreshToken = false;
            }
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Conflict(new { message = "El nombre de usuario o el correo ya están en uso." });
        }

        return Ok(ToDto(user, hasRefreshToken));
    }

    // Aceptar una solicitud de registro (o reactivar una cuenta desactivada).
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id)
    {
        var user = await db.Users.Include(u => u.RefreshToken).FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return NotFound(new { message = "El usuario no existe." });

        user.IsActive = true;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(ToDto(user, user.RefreshToken is not null));
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        if (id == CurrentUserId)
            return BadRequest(new { message = "Un administrador no puede desactivarse a sí mismo." });

        var user = await db.Users.Include(u => u.RefreshToken).FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return NotFound(new { message = "El usuario no existe." });

        user.IsActive = false;
        user.UpdatedAtUtc = DateTime.UtcNow;

        // Al desactivar, el refresh token del usuario se revoca.
        if (user.RefreshToken is not null)
            db.RefreshTokens.Remove(user.RefreshToken);

        await db.SaveChangesAsync();

        return Ok(ToDto(user, false));
    }

    [HttpDelete("{id:guid}/refresh-token")]
    public async Task<IActionResult> RevokeRefreshToken(Guid id)
    {
        if (id == CurrentUserId)
            return BadRequest(new { message = "No puedes revocar tu propio refresh token." });

        if (!await db.Users.AnyAsync(u => u.Id == id))
            return NotFound(new { message = "El usuario no existe." });

        await db.RefreshTokens.Where(t => t.UserId == id).ExecuteDeleteAsync();
        return NoContent();
    }
}
