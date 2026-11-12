namespace backend.Services;

public static class NameRules
{
    // Devuelve un mensaje de error, o null si el nombre es válido.
    // "clean" es el nombre sin espacios sobrantes.
    public static string? Validate(string? name, int maxLength, out string clean)
    {
        clean = (name ?? "").Trim();

        if (clean.Length == 0)
            return "El nombre es obligatorio.";

        if (clean.Length > maxLength)
            return $"El nombre no puede tener más de {maxLength} caracteres.";

        if (clean is "." or "..")
            return "Ese nombre no es válido.";

        if (clean.IndexOfAny(['/', '\\']) >= 0 || clean.Any(char.IsControl))
            return "El nombre no puede contener / ni \\ ni caracteres de control.";

        return null;
    }
}
