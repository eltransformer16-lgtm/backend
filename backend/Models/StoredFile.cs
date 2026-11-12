namespace backend.Models;

// Archivo subido al repositorio. Siempre vive dentro de un directorio.
public class StoredFile
{
    public Guid Id { get; set; }

    // Nombre sin extensión. La extensión no se puede cambiar.
    public string Name { get; set; } = "";
    public string Extension { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }

    // Ruta relativa dentro del volumen de archivos de Docker.
    public string StoragePath { get; set; } = "";

    public Guid FolderId { get; set; }
    public Folder Folder { get; set; } = null!;

    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
