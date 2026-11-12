using System.ComponentModel.DataAnnotations;

namespace backend.Models;

public class CreateUserRequest
{
    [Required(ErrorMessage = "El nombre de usuario es obligatorio.")]
    [StringLength(30, MinimumLength = 3, ErrorMessage = "El nombre de usuario debe tener entre 3 y 30 caracteres.")]
    [RegularExpression(@"^[a-zA-Z0-9_.-]+$", ErrorMessage = "El nombre de usuario solo admite letras, números, punto, guion y guion bajo.")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "El correo es obligatorio.")]
    [EmailAddress(ErrorMessage = "El correo no tiene un formato válido.")]
    [StringLength(254)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    [StringLength(100, MinimumLength = 10, ErrorMessage = "La contraseña debe tener entre 10 y 100 caracteres.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "La contraseña debe incluir al menos una letra y un número.")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Debes confirmar la contraseña.")]
    [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden.")]
    public string ConfirmPassword { get; set; } = "";

    [Required(ErrorMessage = "La fecha de nacimiento es obligatoria.")]
    public DateOnly? BirthDate { get; set; }

    [Required(ErrorMessage = "El rol es obligatorio.")]
    [RegularExpression("^(Admin|User)$", ErrorMessage = "El rol debe ser Admin o User.")]
    public string Role { get; set; } = Roles.User;
}

public class UpdateUserRequest
{
    [Required(ErrorMessage = "El nombre de usuario es obligatorio.")]
    [StringLength(30, MinimumLength = 3, ErrorMessage = "El nombre de usuario debe tener entre 3 y 30 caracteres.")]
    [RegularExpression(@"^[a-zA-Z0-9_.-]+$", ErrorMessage = "El nombre de usuario solo admite letras, números, punto, guion y guion bajo.")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "El correo es obligatorio.")]
    [EmailAddress(ErrorMessage = "El correo no tiene un formato válido.")]
    [StringLength(254)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "La fecha de nacimiento es obligatoria.")]
    public DateOnly? BirthDate { get; set; }

    [Required(ErrorMessage = "El rol es obligatorio.")]
    [RegularExpression("^(Admin|User)$", ErrorMessage = "El rol debe ser Admin o User.")]
    public string Role { get; set; } = Roles.User;

    // Opcional: solo se cambia la contraseña si se envía.
    [StringLength(100, MinimumLength = 10, ErrorMessage = "La contraseña debe tener entre 10 y 100 caracteres.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "La contraseña debe incluir al menos una letra y un número.")]
    public string? Password { get; set; }

    [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden.")]
    public string? ConfirmPassword { get; set; }
}

public record UserDto(
    Guid Id,
    string Username,
    string Email,
    DateOnly BirthDate,
    string Role,
    bool IsActive,
    bool HasRefreshToken,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? LastLoginAtUtc);

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
