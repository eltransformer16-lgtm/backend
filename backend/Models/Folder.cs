namespace backend.Models;

// Directorio del repositorio. Si ParentId es null, está en la raíz.
public class Folder
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    public Guid? ParentId { get; set; }
    public Folder? Parent { get; set; }

    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
