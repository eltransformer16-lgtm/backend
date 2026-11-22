using System.Security.Claims;
using backend.Data;
using backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

public record CreateDirectoryRequest(string Name, Guid? ParentId);
public record RenameRequest(string NewName);

[ApiController]
[Route("api/storage")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly string _storageRoot;
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    public FilesController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _storageRoot = config["Storage:RootPath"] ?? "/app/uploads";
        Directory.CreateDirectory(_storageRoot);
    }

    private (Guid UserId, bool IsAdmin) GetCurrentUser()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        Guid.TryParse(sub, out var id);
        var isAdmin = User.IsInRole("Admin");
        return (id, isAdmin);
    }

    // --- DIRECTORIOS ---

    [HttpGet("directories")]
    public async Task<IActionResult> ListDirectories(
        [FromQuery] Guid? parentId = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _db.Directories
            .Include(d => d.Owner)
            .Where(d => d.ParentId == parentId);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(d => EF.Functions.ILike(d.Name, $"%{search.Trim()}%"));

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(d => d.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.ParentId,
                d.OwnerId,
                OwnerUsername = d.Owner != null ? d.Owner.Username : "Desconocido",
                d.CreatedAtUtc,
                d.UpdatedAtUtc
            })
            .ToListAsync();

        return Ok(new { items, total, page, pageSize });
    }

    [HttpPost("directories")]
    public async Task<IActionResult> CreateDirectory([FromBody] CreateDirectoryRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { message = "El nombre del directorio es obligatorio." });

        var cleanName = req.Name.Trim();
        if (cleanName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return BadRequest(new { message = "El nombre contiene caracteres no válidos." });

        if (req.ParentId.HasValue)
        {
            var parentExists = await _db.Directories.AnyAsync(d => d.Id == req.ParentId.Value);
            if (!parentExists)
                return NotFound(new { message = "El directorio padre no existe." });
        }

        var duplicate = await _db.Directories.AnyAsync(d =>
            d.ParentId == req.ParentId && d.Name.ToLower() == cleanName.ToLower());
        if (duplicate)
            return Conflict(new { message = "Ya existe un directorio con ese nombre en esta ubicación." });

        var (userId, _) = GetCurrentUser();
        var dir = new StorageDirectory
        {
            Name = cleanName,
            ParentId = req.ParentId,
            OwnerId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.Directories.Add(dir);
        await _db.SaveChangesAsync();

        return StatusCode(201, new { dir.Id, dir.Name, dir.ParentId, dir.OwnerId, dir.CreatedAtUtc, dir.UpdatedAtUtc });
    }

    [HttpPut("directories/{id:guid}/rename")]
    public async Task<IActionResult> RenameDirectory(Guid id, [FromBody] RenameRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NewName))
            return BadRequest(new { message = "El nuevo nombre es obligatorio." });

        var dir = await _db.Directories.FirstOrDefaultAsync(d => d.Id == id);
        if (dir is null) return NotFound(new { message = "Directorio no encontrado." });

        var (userId, isAdmin) = GetCurrentUser();
        if (!isAdmin && dir.OwnerId != userId)
            return StatusCode(403, new { message = "Solo el propietario o un administrador puede renombrar este directorio." });

        var cleanName = req.NewName.Trim();
        var duplicate = await _db.Directories.AnyAsync(d =>
            d.ParentId == dir.ParentId && d.Id != id && d.Name.ToLower() == cleanName.ToLower());
        if (duplicate)
            return Conflict(new { message = "Ya existe otro directorio con ese nombre aquí." });

        dir.Name = cleanName;
        dir.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { dir.Id, dir.Name, dir.UpdatedAtUtc });
    }

    [HttpDelete("directories/{id:guid}")]
    public async Task<IActionResult> DeleteDirectory(Guid id)
    {
        var dir = await _db.Directories
            .Include(d => d.Children)
            .Include(d => d.Files)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (dir is null) return NotFound(new { message = "Directorio no encontrado." });

        var (userId, isAdmin) = GetCurrentUser();
        if (!isAdmin && dir.OwnerId != userId)
            return StatusCode(403, new { message = "Solo el propietario o un administrador puede eliminar este directorio." });

        if (dir.Children.Count > 0 || dir.Files.Count > 0)
            return BadRequest(new { message = "No se puede eliminar un directorio que contiene subdirectorios o archivos. Vacíalo primero." });

        _db.Directories.Remove(dir);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Directorio eliminado." });
    }

    // --- ARCHIVOS ---

    [HttpGet("files")]
    public async Task<IActionResult> ListFiles(
        [FromQuery] Guid? directoryId = null,
        [FromQuery] string? name = null,
        [FromQuery] string? owner = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 10;

        var query = _db.Files
            .Include(f => f.Owner)
            .Include(f => f.Directory)
            .AsQueryable();

        if (directoryId.HasValue)
            query = query.Where(f => f.DirectoryId == directoryId.Value);

        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(f => EF.Functions.ILike(f.OriginalName, $"%{name.Trim()}%"));

        if (!string.IsNullOrWhiteSpace(owner))
            query = query.Where(f => f.Owner != null && EF.Functions.ILike(f.Owner.Username, $"%{owner.Trim()}%"));

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(f => f.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new
            {
                f.Id,
                f.OriginalName,
                f.ContentType,
                f.SizeBytes,
                f.DirectoryId,
                DirectoryName = f.Directory != null ? f.Directory.Name : "",
                f.OwnerId,
                OwnerUsername = f.Owner != null ? f.Owner.Username : "Desconocido",
                f.CreatedAtUtc,
                f.UpdatedAtUtc
            })
            .ToListAsync();

        return Ok(new { items, total, page, pageSize });
    }

    [HttpPost("files/upload")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<IActionResult> Upload([FromForm] Guid directoryId, [FromForm] IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Debes seleccionar un archivo válido." });

        if (file.Length > MaxFileSizeBytes)
            return BadRequest(new { message = "El archivo excede el límite máximo de 10 MB." });

        var dir = await _db.Directories.FirstOrDefaultAsync(d => d.Id == directoryId);
        if (dir is null)
            return NotFound(new { message = "Debes seleccionar un directorio existente para subir el archivo." });

        var originalName = Path.GetFileName(file.FileName);
        var ext = Path.GetExtension(originalName);
        var storedName = $"{Guid.NewGuid()}{ext}";
        var fullPath = Path.Combine(_storageRoot, storedName);

        await using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var (userId, _) = GetCurrentUser();
        var entity = new StoredFile
        {
            OriginalName = originalName,
            StoredFileName = storedName,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            SizeBytes = file.Length,
            DirectoryId = directoryId,
            OwnerId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.Files.Add(entity);
        await _db.SaveChangesAsync();

        return StatusCode(201, new
        {
            entity.Id,
            entity.OriginalName,
            entity.ContentType,
            entity.SizeBytes,
            entity.DirectoryId,
            entity.OwnerId,
            entity.CreatedAtUtc
        });
    }

    [HttpGet("files/{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id)
    {
        var file = await _db.Files.FirstOrDefaultAsync(f => f.Id == id);
        if (file is null) return NotFound(new { message = "Archivo no encontrado." });

        var fullPath = Path.Combine(_storageRoot, file.StoredFileName);
        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { message = "El archivo físico no existe en el almacenamiento." });

        return PhysicalFile(fullPath, file.ContentType, file.OriginalName);
    }

    [HttpPut("files/{id:guid}/rename")]
    public async Task<IActionResult> RenameFile(Guid id, [FromBody] RenameRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NewName))
            return BadRequest(new { message = "El nuevo nombre es obligatorio." });

        var file = await _db.Files.FirstOrDefaultAsync(f => f.Id == id);
        if (file is null) return NotFound(new { message = "Archivo no encontrado." });

        var (userId, isAdmin) = GetCurrentUser();
        if (!isAdmin && file.OwnerId != userId)
            return StatusCode(403, new { message = "Solo el propietario o un administrador puede renombrar este archivo." });

        // Requerimiento estricto: preservar la extensión original
        var originalExt = Path.GetExtension(file.OriginalName);
        var baseNewName = Path.GetFileNameWithoutExtension(req.NewName.Trim());
        if (string.IsNullOrWhiteSpace(baseNewName))
            return BadRequest(new { message = "Nombre de archivo inválido." });

        file.OriginalName = $"{baseNewName}{originalExt}";
        file.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { file.Id, file.OriginalName, file.UpdatedAtUtc });
    }

    [HttpDelete("files/{id:guid}")]
    public async Task<IActionResult> DeleteFile(Guid id)
    {
        var file = await _db.Files.FirstOrDefaultAsync(f => f.Id == id);
        if (file is null) return NotFound(new { message = "Archivo no encontrado." });

        var (userId, isAdmin) = GetCurrentUser();
        if (!isAdmin && file.OwnerId != userId)
            return StatusCode(403, new { message = "Solo el propietario o un administrador puede eliminar este archivo." });

        var fullPath = Path.Combine(_storageRoot, file.StoredFileName);
        if (System.IO.File.Exists(fullPath))
            System.IO.File.Delete(fullPath);

        _db.Files.Remove(file);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Archivo eliminado." });
    }
}