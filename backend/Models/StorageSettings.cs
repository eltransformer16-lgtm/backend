namespace backend.Models;

public class StorageSettings
{
    // Carpeta donde se guardan los archivos subidos. Si está vacía se usa una por defecto
    // (en Docker: /app/uploads, que es el volumen "uploads").
    public string Path { get; set; } = "";
}
