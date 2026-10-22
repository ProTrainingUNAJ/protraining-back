namespace Application.DTOs.Auth;

/// <summary>
/// DTO con la informacion basica del usuario autenticado
/// </summary>
public record UserDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    Guid? PfId);
