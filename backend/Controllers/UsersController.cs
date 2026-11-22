using System.Security.Claims;
using backend.Data;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

public record AdminCreateUserRequest(
    string Username,
    string Email,
    string Password,
    DateOnly BirthDate,
    string Role = "User",
    bool IsActive = true
);

public record AdminUpdateUserRequest(
    string? Username,
    string? Email,
    DateOnly? BirthDate,
    string? Role,
    bool? IsActive,
    string? NewPassword
);

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly RefreshTokenService _refresh;

    public UsersController(AppDbContext db, RefreshTokenService refresh)
    {
        _db = db;
        _refresh = refresh;
    }

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? username = null,
        [FromQuery] string? email = null,
        [FromQuery] string? role = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? sortBy = "createdAt",
        [FromQuery] string? sortDir = "desc")
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 10;

        var query = _db.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(username))
            query = query.Where(u => EF.Functions.ILike(u.Username, $"%{username.Trim()}%"));

        if (!string.IsNullOrWhiteSpace(email))
            query = query.Where(u => EF.Functions.ILike(u.Email, $"%{email.Trim()}%"));

        if (!string.IsNullOrWhiteSpace(role))
            query = query.Where(u => u.Role == role.Trim());

        if (isActive.HasValue)
            query = query.Where(u => u.IsActive == isActive.Value);

        var total = await query.CountAsync();

        bool asc = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
        query = sortBy?.ToLowerInvariant() switch
        {
            "username" => asc ? query.OrderBy(u => u.Username) : query.OrderByDescending(u => u.Username),
            "email" => asc ? query.OrderBy(u => u.Email) : query.OrderByDescending(u => u.Email),
            "role" => asc ? query.OrderBy(u => u.Role) : query.OrderByDescending(u => u.Role),
            "isactive" => asc ? query.OrderBy(u => u.IsActive) : query.OrderByDescending(u => u.IsActive),
            _ => asc ? query.OrderBy(u => u.CreatedAtUtc) : query.OrderByDescending(u => u.CreatedAtUtc)
        };

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.Email,
                u.BirthDate,
                u.Role,
                u.IsActive,
                u.CreatedAtUtc,
                u.UpdatedAtUtc,
                HasActiveRefreshToken = u.RefreshTokens.Any(rt => rt.RevokedAtUtc == null && rt.ExpiresAtUtc > DateTime.UtcNow)
            })
            .ToListAsync();

        return Ok(new { items, total, page, pageSize });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var u = await _db.Users
            .Include(x => x.RefreshTokens)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (u is null) return NotFound(new { message = "Usuario no encontrado." });

        return Ok(new
        {
            u.Id,
            u.Username,
            u.Email,
            u.BirthDate,
            u.Role,
            u.IsActive,
            u.CreatedAtUtc,
            u.UpdatedAtUtc,
            HasActiveRefreshToken = u.RefreshTokens.Any(rt => rt.RevokedAtUtc == null && rt.ExpiresAtUtc > DateTime.UtcNow)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AdminCreateUserRequest req)
    {
        var username = req.Username.Trim();
        var normUser = username.ToUpperInvariant();
        var email = req.Email.Trim().ToLowerInvariant();
        var normEmail = email.ToUpperInvariant();
        var role = (req.Role == "Admin") ? "Admin" : "User";

        if (await _db.Users.AnyAsync(u => u.NormalizedUsername == normUser || u.NormalizedEmail == normEmail))
            return Conflict(new { message = "El usuario o correo ya existe." });

        var user = new User
        {
            Username = username,
            NormalizedUsername = normUser,
            Email = email,
            NormalizedEmail = normEmail,
            PasswordHash = PasswordHasher.Hash(req.Password),
            BirthDate = req.BirthDate,
            Role = role,
            IsActive = req.IsActive,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return StatusCode(201, new { user.Id, user.Username, user.Email, user.BirthDate, user.Role, user.IsActive, user.CreatedAtUtc, user.UpdatedAtUtc });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AdminUpdateUserRequest req)
    {
        var currentAdminId = GetCurrentUserId();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound(new { message = "Usuario no encontrado." });

        if (user.Id == currentAdminId)
        {
            if (req.IsActive.HasValue && !req.IsActive.Value)
                return BadRequest(new { message = "No puedes desactivar tu propia cuenta de administrador." });

            if (!string.IsNullOrWhiteSpace(req.Role) && req.Role != "Admin")
                return BadRequest(new { message = "No puedes quitarte tu propio rol de administrador." });
        }

        if (!string.IsNullOrWhiteSpace(req.Username))
        {
            var newU = req.Username.Trim();
            var normU = newU.ToUpperInvariant();
            if (await _db.Users.AnyAsync(u => u.NormalizedUsername == normU && u.Id != id))
                return Conflict(new { message = "Ese nombre de usuario ya está en uso." });
            user.Username = newU;
            user.NormalizedUsername = normU;
        }

        if (!string.IsNullOrWhiteSpace(req.Email))
        {
            var newE = req.Email.Trim().ToLowerInvariant();
            var normE = newE.ToUpperInvariant();
            if (await _db.Users.AnyAsync(u => u.NormalizedEmail == normE && u.Id != id))
                return Conflict(new { message = "Ese correo ya está en uso." });
            user.Email = newE;
            user.NormalizedEmail = normE;
        }

        if (req.BirthDate.HasValue)
            user.BirthDate = req.BirthDate.Value;

        bool mustRevokeTokens = false;

        if (!string.IsNullOrWhiteSpace(req.Role))
            user.Role = (req.Role == "Admin") ? "Admin" : "User";

        if (req.IsActive.HasValue)
        {
            if (user.IsActive && !req.IsActive.Value)
                mustRevokeTokens = true;
            user.IsActive = req.IsActive.Value;
        }

        if (!string.IsNullOrWhiteSpace(req.NewPassword))
        {
            if (req.NewPassword.Length < 8)
                return BadRequest(new { message = "La nueva contraseña debe tener al menos 8 caracteres." });
            user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
            mustRevokeTokens = true;
        }

        user.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        if (mustRevokeTokens)
            await _refresh.RevokeAllForUserAsync(user.Id);

        return Ok(new { user.Id, user.Username, user.Email, user.BirthDate, user.Role, user.IsActive, user.CreatedAtUtc, user.UpdatedAtUtc });
    }

    [HttpPost("{id:guid}/revoke-token")]
    public async Task<IActionResult> RevokeUserToken(Guid id)
    {
        var currentAdminId = GetCurrentUserId();
        if (id == currentAdminId)
            return BadRequest(new { message = "No puedes revocar tu propio Refresh Token desde este panel." });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound(new { message = "Usuario no encontrado." });

        await _refresh.RevokeAllForUserAsync(id);
        return Ok(new { message = "Refresh Token revocado correctamente." });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var currentAdminId = GetCurrentUserId();
        if (id == currentAdminId)
            return BadRequest(new { message = "No puedes eliminar tu propia cuenta." });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound(new { message = "Usuario no encontrado." });

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Usuario eliminado." });
    }
}