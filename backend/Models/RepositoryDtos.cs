using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace backend.Models;

public class CreateFolderRequest
{
    [Required(ErrorMessage = "El nombre del directorio es obligatorio.")]
    [StringLength(100, ErrorMessage = "El nombre no puede tener más de 100 caracteres.")]
    public string Name { get; set; } = "";

    // Directorio dentro del que se crea. Si es nulo, se crea en la raíz.
    public Guid? ParentId { get; set; }
}

public class RenameFolderRequest
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(100, ErrorMessage = "El nombre no puede tener más de 100 caracteres.")]
    public string Name { get; set; } = "";
}

public class RenameFileRequest
{
    // Solo el nombre: la extensión no se puede cambiar.
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(200, ErrorMessage = "El nombre no puede tener más de 200 caracteres.")]
    public string Name { get; set; } = "";
}

public class UploadFileRequest
{
    [Required(ErrorMessage = "Indica el directorio donde se guardará el archivo.")]
    public Guid? FolderId { get; set; }

    [Required(ErrorMessage = "Selecciona un archivo.")]
    public IFormFile? File { get; set; }
}

public record BreadcrumbDto(Guid Id, string Name);

// Un elemento de la lista: Kind es "folder" o "file".
// Extension, ContentType y SizeBytes solo tienen valor en los archivos.
// CanModify indica si el usuario actual puede renombrarlo o eliminarlo (dueño o administrador).
public record ContentItemDto(
    string Kind,
    Guid Id,
    string Name,
    string? Extension,
    string? ContentType,
    long? SizeBytes,
    Guid OwnerId,
    string OwnerName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    bool CanModify);

public record ContentsDto(
    Guid? FolderId,
    IReadOnlyList<BreadcrumbDto> Breadcrumb,
    IReadOnlyList<ContentItemDto> Items,
    int Total,
    int Page,
    int PageSize);
