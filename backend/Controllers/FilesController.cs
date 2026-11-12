using backend.Data;
using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace backend.Controllers;

// Subir, descargar, renombrar y eliminar archivos. Requiere haber iniciado sesión.
[ApiController]
[Route("api/files")]
[Authorize]
public class FilesController(AppDbContext db, FileStorageService storage) : RepositoryControllerBase
{
    private const long MaxFileBytes = 10L * 1024 * 1024; // 10 MB
    private const long MaxRequestBytes = MaxFileBytes + 1024 * 1024; // margen para el resto del formulario

    private ContentItemDto ToItem(StoredFile file, string ownerName) => new(
        "file", file.Id, file.Name, file.Extension, file.ContentType, file.SizeBytes,
        file.OwnerId, ownerName, file.CreatedAtUtc, file.UpdatedAtUtc, CanModify(file.OwnerId));

    // Subir: formulario multipart con "folderId" y "file". Solo dentro de un directorio y hasta 10 MB.
    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<IActionResult> Upload([FromForm] UploadFileRequest request, CancellationToken cancellationToken)
    {
        if (request.FolderId is not { } folderId || request.File is not { } upload)
            return BadRequest(new { message = "Indica el directorio y el archivo." });

        if (upload.Length == 0)
            return BadRequest(new { message = "El archivo está vacío." });

        if (upload.Length > MaxFileBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { message = "El archivo supera el límite de 10 MB." });

        if (!await db.Folders.AnyAsync(f => f.Id == folderId, cancellationToken))
            return NotFound(new { message = "El directorio no existe." });

        // Solo se conserva el nombre del archivo, sin rutas del equipo del usuario.
        var fileName = upload.FileName.Replace('\\', '/');
        fileName = fileName[(fileName.LastIndexOf('/') + 1)..];

        var extension = Path.GetExtension(fileName);
        var rawName = Path.GetFileNameWithoutExtension(fileName);

        if (extension.Length > 20)
            return BadRequest(new { message = "La extensión del archivo es demasiado larga." });

        var nameError = NameRules.Validate(rawName, 200, out var name);
        if (nameError is not null)
            return BadRequest(new { message = nameError });

        var contentType = upload.ContentType;
        if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > 150
            || !MediaTypeHeaderValue.TryParse(contentType, out _))
        {
            contentType = "application/octet-stream";
        }

        var lowerName = name.ToLowerInvariant();
        var lowerExtension = extension.ToLowerInvariant();
        var duplicated = await db.Files.AnyAsync(f =>
            f.FolderId == folderId
            && f.Name.ToLower() == lowerName
            && f.Extension.ToLower() == lowerExtension, cancellationToken);
        if (duplicated)
            return Conflict(new { message = "Ya existe un archivo con ese nombre en este directorio." });

        var storagePath = await storage.SaveAsync(upload, cancellationToken);

        var now = DateTime.UtcNow;
        var entity = new StoredFile
        {
            Id = Guid.NewGuid(),
            Name = name,
            Extension = extension,
            ContentType = contentType,
            SizeBytes = upload.Length,
            StoragePath = storagePath,
            FolderId = folderId,
            OwnerId = CurrentUserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Files.Add(entity);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Si no se pudo registrar, no se deja el archivo físico huérfano.
            storage.Delete(storagePath);
            throw;
        }

        return StatusCode(StatusCodes.Status201Created, ToItem(entity, CurrentUsername));
    }

    // Descargar: cualquier usuario autenticado puede descargar cualquier archivo.
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id)
    {
        var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);
        if (file is null)
            return NotFound(new { message = "El archivo no existe." });

        if (!storage.Exists(file.StoragePath))
            return NotFound(new { message = "El archivo no se encontró en el almacenamiento." });

        // Siempre se entrega como descarga (attachment), nunca para abrirse en el navegador.
        return PhysicalFile(storage.GetFullPath(file.StoragePath), file.ContentType, file.Name + file.Extension);
    }

    // Renombrar: solo el nombre; la extensión no cambia. Solo quien lo subió o un administrador.
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Rename(Guid id, RenameFileRequest request)
    {
        var error = NameRules.Validate(request.Name, 200, out var name);
        if (error is not null)
            return BadRequest(new { message = error });

        var file = await db.Files.Include(f => f.Owner).FirstOrDefaultAsync(f => f.Id == id);
        if (file is null)
            return NotFound(new { message = "El archivo no existe." });

        if (!CanModify(file.OwnerId))
            return Forbidden();

        // Si el usuario escribió la misma extensión al final, se ignora: la extensión no se puede cambiar.
        if (file.Extension.Length > 0
            && name.Length > file.Extension.Length
            && name.EndsWith(file.Extension, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^file.Extension.Length].TrimEnd();
        }

        if (name.Length == 0)
            return BadRequest(new { message = "El nombre es obligatorio." });

        var lowerName = name.ToLowerInvariant();
        var lowerExtension = file.Extension.ToLowerInvariant();
        var duplicated = await db.Files.AnyAsync(f =>
            f.Id != id
            && f.FolderId == file.FolderId
            && f.Name.ToLower() == lowerName
            && f.Extension.ToLower() == lowerExtension);
        if (duplicated)
            return Conflict(new { message = "Ya existe un archivo con ese nombre en este directorio." });

        file.Name = name;
        file.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(ToItem(file, file.Owner.Username));
    }

    // Eliminar: solo quien lo subió o un administrador. Borra el registro y el archivo físico.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id);
        if (file is null)
            return NotFound(new { message = "El archivo no existe." });

        if (!CanModify(file.OwnerId))
            return Forbidden();

        db.Files.Remove(file);
        await db.SaveChangesAsync();

        storage.Delete(file.StoragePath);
        return NoContent();
    }
}
