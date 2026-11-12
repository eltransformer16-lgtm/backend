using backend.Data;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

// Directorios y consulta del contenido del repositorio. Requiere haber iniciado sesión.
[ApiController]
[Route("api/folders")]
[Authorize]
public class FoldersController(AppDbContext db, FileStorageService storage) : RepositoryControllerBase
{
    private ContentItemDto ToItem(Folder folder, string ownerName) => new(
        "folder", folder.Id, folder.Name, null, null, null,
        folder.OwnerId, ownerName, folder.CreatedAtUtc, folder.UpdatedAtUtc, CanModify(folder.OwnerId));

    // Consulta por directorio (folderId vacío = raíz): primero los subdirectorios y luego los archivos,
    // paginado. Los filtros de texto buscan subcadenas.
    [HttpGet("contents")]
    public async Task<ActionResult<ContentsDto>> Contents(
        [FromQuery] Guid? folderId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? name = null,
        [FromQuery] string? type = null,
        [FromQuery] string? owner = null,
        [FromQuery] DateOnly? uploadedFrom = null,
        [FromQuery] DateOnly? uploadedTo = null)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Ruta de navegación (raíz > ... > directorio actual).
        var breadcrumb = new List<BreadcrumbDto>();
        if (folderId is { } currentId)
        {
            Guid? cursor = currentId;
            var guard = 0;
            while (cursor is { } cursorId && guard++ < 64)
            {
                var row = await db.Folders.AsNoTracking()
                    .Where(f => f.Id == cursorId)
                    .Select(f => new { f.Id, f.Name, f.ParentId })
                    .FirstOrDefaultAsync();

                if (row is null)
                {
                    if (breadcrumb.Count == 0)
                        return NotFound(new { message = "El directorio no existe." });
                    break;
                }

                breadcrumb.Insert(0, new BreadcrumbDto(row.Id, row.Name));
                cursor = row.ParentId;
            }
        }

        IQueryable<Folder> folders = db.Folders.AsNoTracking().Where(f => f.ParentId == folderId);

        // Los archivos solo existen dentro de directorios, nunca en la raíz.
        IQueryable<StoredFile> files = folderId is null
            ? db.Files.AsNoTracking().Where(f => false)
            : db.Files.AsNoTracking().Where(f => f.FolderId == folderId);

        if (!string.IsNullOrWhiteSpace(name))
        {
            var pattern = LikePattern(name);
            folders = folders.Where(f => EF.Functions.ILike(f.Name, pattern));
            files = files.Where(f => EF.Functions.ILike(f.Name + f.Extension, pattern));
        }

        if (!string.IsNullOrWhiteSpace(owner))
        {
            var pattern = LikePattern(owner);
            folders = folders.Where(f => EF.Functions.ILike(f.Owner.Username, pattern));
            files = files.Where(f => EF.Functions.ILike(f.Owner.Username, pattern));
        }

        if (uploadedFrom is { } fromDate)
        {
            var start = DateTime.SpecifyKind(fromDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            folders = folders.Where(f => f.CreatedAtUtc >= start);
            files = files.Where(f => f.CreatedAtUtc >= start);
        }

        if (uploadedTo is { } toDate)
        {
            var end = DateTime.SpecifyKind(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            folders = folders.Where(f => f.CreatedAtUtc < end);
            files = files.Where(f => f.CreatedAtUtc < end);
        }

        // El tipo solo aplica a archivos (extensión o tipo de contenido): al filtrar por tipo no se muestran directorios.
        if (!string.IsNullOrWhiteSpace(type))
        {
            var pattern = LikePattern(type);
            files = files.Where(f => EF.Functions.ILike(f.Extension, pattern) || EF.Functions.ILike(f.ContentType, pattern));
            folders = folders.Where(f => false);
        }

        var folderTotal = await folders.CountAsync();
        var fileTotal = await files.CountAsync();
        var skip = (page - 1) * pageSize;
        var items = new List<ContentItemDto>();

        if (skip < folderTotal)
        {
            var take = Math.Min(pageSize, folderTotal - skip);
            var folderRows = await folders
                .OrderBy(f => f.Name)
                .Skip(skip)
                .Take(take)
                .Select(f => new { f.Id, f.Name, f.OwnerId, OwnerName = f.Owner.Username, f.CreatedAtUtc, f.UpdatedAtUtc })
                .ToListAsync();

            items.AddRange(folderRows.Select(r => new ContentItemDto(
                "folder", r.Id, r.Name, null, null, null,
                r.OwnerId, r.OwnerName, r.CreatedAtUtc, r.UpdatedAtUtc, CanModify(r.OwnerId))));
        }

        var remaining = pageSize - items.Count;
        if (remaining > 0 && fileTotal > 0)
        {
            var fileSkip = Math.Max(0, skip - folderTotal);
            var fileRows = await files
                .OrderBy(f => f.Name)
                .ThenBy(f => f.Extension)
                .Skip(fileSkip)
                .Take(remaining)
                .Select(f => new
                {
                    f.Id, f.Name, f.Extension, f.ContentType, f.SizeBytes, f.OwnerId,
                    OwnerName = f.Owner.Username, f.CreatedAtUtc, f.UpdatedAtUtc
                })
                .ToListAsync();

            items.AddRange(fileRows.Select(r => new ContentItemDto(
                "file", r.Id, r.Name, r.Extension, r.ContentType, r.SizeBytes,
                r.OwnerId, r.OwnerName, r.CreatedAtUtc, r.UpdatedAtUtc, CanModify(r.OwnerId))));
        }

        return new ContentsDto(folderId, breadcrumb, items, folderTotal + fileTotal, page, pageSize);
    }

    // Crear directorio: solo hace falta el nombre (y, opcionalmente, dentro de cuál).
    [HttpPost]
    public async Task<IActionResult> Create(CreateFolderRequest request)
    {
        var error = NameRules.Validate(request.Name, 100, out var name);
        if (error is not null)
            return BadRequest(new { message = error });

        if (request.ParentId is { } parentId && !await db.Folders.AnyAsync(f => f.Id == parentId))
            return NotFound(new { message = "El directorio padre no existe." });

        var lowerName = name.ToLowerInvariant();
        var duplicated = await db.Folders.AnyAsync(f =>
            f.ParentId == request.ParentId && f.Name.ToLower() == lowerName);
        if (duplicated)
            return Conflict(new { message = "Ya existe un directorio con ese nombre en esta ubicación." });

        var now = DateTime.UtcNow;
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            Name = name,
            ParentId = request.ParentId,
            OwnerId = CurrentUserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Folders.Add(folder);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Conflict(new { message = "Ya existe un directorio con ese nombre en esta ubicación." });
        }

        return StatusCode(StatusCodes.Status201Created, ToItem(folder, CurrentUsername));
    }

    // Renombrar: solo quien lo creó o un administrador.
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Rename(Guid id, RenameFolderRequest request)
    {
        var error = NameRules.Validate(request.Name, 100, out var name);
        if (error is not null)
            return BadRequest(new { message = error });

        var folder = await db.Folders.Include(f => f.Owner).FirstOrDefaultAsync(f => f.Id == id);
        if (folder is null)
            return NotFound(new { message = "El directorio no existe." });

        if (!CanModify(folder.OwnerId))
            return Forbidden();

        var lowerName = name.ToLowerInvariant();
        var duplicated = await db.Folders.AnyAsync(f =>
            f.Id != id && f.ParentId == folder.ParentId && f.Name.ToLower() == lowerName);
        if (duplicated)
            return Conflict(new { message = "Ya existe un directorio con ese nombre en esta ubicación." });

        folder.Name = name;
        folder.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Conflict(new { message = "Ya existe un directorio con ese nombre en esta ubicación." });
        }

        return Ok(ToItem(folder, folder.Owner.Username));
    }

    // Eliminar: el dueño solo si está vacío; un administrador puede borrarlo con todo su contenido.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == id);
        if (folder is null)
            return NotFound(new { message = "El directorio no existe." });

        if (!CanModify(folder.OwnerId))
            return Forbidden();

        if (!IsAdmin)
        {
            var hasContent = await db.Folders.AnyAsync(f => f.ParentId == id)
                             || await db.Files.AnyAsync(f => f.FolderId == id);
            if (hasContent)
                return Conflict(new
                {
                    message = "El directorio no está vacío. Solo un administrador puede eliminar directorios con contenido."
                });
        }

        // Se reúnen este directorio y todos sus subdirectorios para borrar también los archivos físicos.
        var folderIds = new List<Guid> { id };
        var frontier = new List<Guid> { id };
        while (frontier.Count > 0)
        {
            var current = frontier;
            frontier = await db.Folders
                .Where(f => f.ParentId != null && current.Contains(f.ParentId.Value))
                .Select(f => f.Id)
                .ToListAsync();
            folderIds.AddRange(frontier);
        }

        var storagePaths = await db.Files
            .Where(f => folderIds.Contains(f.FolderId))
            .Select(f => f.StoragePath)
            .ToListAsync();

        // La base de datos borra en cascada los subdirectorios y los registros de archivos.
        db.Folders.Remove(folder);
        await db.SaveChangesAsync();

        foreach (var path in storagePaths)
            storage.Delete(path);

        return NoContent();
    }
}
