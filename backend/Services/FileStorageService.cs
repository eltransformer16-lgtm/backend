using backend.Models;
using Microsoft.Extensions.Options;

namespace backend.Services;

// Guarda, lee y borra los archivos físicos en la carpeta del volumen de Docker.
// Cada archivo se guarda con un nombre aleatorio (nunca con el nombre que puso el usuario),
// así que el nombre original solo vive en la base de datos.
public class FileStorageService
{
    private readonly string _root;

    public FileStorageService(IOptions<StorageSettings> options, IHostEnvironment environment)
    {
        var configured = options.Value.Path;

        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true"
                ? "/app/uploads"
                : Path.Combine(environment.ContentRootPath, "uploads");
        }

        _root = Path.GetFullPath(configured);
        Directory.CreateDirectory(_root);
    }

    // Guarda el archivo y devuelve su ruta relativa dentro de la carpeta de almacenamiento.
    public async Task<string> SaveAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        var relativePath = $"{id[..2]}/{id}";
        var fullPath = Resolve(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var stream = new FileStream(
            fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await file.CopyToAsync(stream, cancellationToken);

        return relativePath;
    }

    public bool Exists(string relativePath) => File.Exists(Resolve(relativePath));

    public string GetFullPath(string relativePath) => Resolve(relativePath);

    // Borra el archivo físico. Si no existe o no se puede borrar, no falla.
    public void Delete(string relativePath)
    {
        try
        {
            var fullPath = Resolve(relativePath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // Convierte una ruta relativa en completa y evita salirse de la carpeta de almacenamiento.
    private string Resolve(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));

        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Ruta de archivo no válida.");

        return fullPath;
    }
}
