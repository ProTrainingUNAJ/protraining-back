namespace Application.DTOs.Auth;

/// <summary>
/// Respuesta devuelta tras el reseteo exitoso de la contraseña de un atleta por parte de su Preparador Físico.
/// </summary>
public record ResetAthletePasswordResponseDto(
    Guid AthleteId,
    string TemporaryPassword,
    bool MustChangePassword = true);
