using System.IdentityModel.Tokens.Jwt;
using backend.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace backend.Controllers;

// Utilidades compartidas por los controladores de directorios y archivos.
public abstract class RepositoryControllerBase : ControllerBase
{
    protected Guid CurrentUserId =>
        Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : Guid.Empty;

    protected string CurrentUsername =>
        User.FindFirst(JwtRegisteredClaimNames.UniqueName)?.Value ?? "";

    protected bool IsAdmin => User.IsInRole(Roles.Admin);

    // Solo quien lo subió/creó o un administrador puede renombrar o eliminar.
    protected bool CanModify(Guid ownerId) => IsAdmin || ownerId == CurrentUserId;

    protected IActionResult Forbidden() =>
        StatusCode(StatusCodes.Status403Forbidden,
            new { message = "Solo quien lo creó o un administrador puede hacer esto." });

    // Texto para buscar subcadenas con ILIKE (se escapan los comodines del usuario).
    protected static string LikePattern(string value) =>
        "%" + value.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    protected static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
