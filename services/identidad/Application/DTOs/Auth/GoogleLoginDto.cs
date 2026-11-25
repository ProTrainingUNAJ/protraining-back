namespace Application.DTOs.Auth;

/// <summary>
/// Modelo de entrada para el inicio de sesión con Google.
/// </summary>
public record GoogleLoginRequestDto(
    string IdToken,
    string? DeviceId = null
);

/// <summary>
/// Modelo de entrada para vincular explícitamente una cuenta de Google desde una sesión autenticada.
/// Requiere la contraseña actual como reautenticación.
/// </summary>
public record LinkGoogleAccountRequestDto(
    string IdToken,
    string CurrentPassword
);

/// <summary>
/// Modelo de entrada para desvincular una cuenta de Google desde una sesión autenticada.
/// </summary>
public record UnlinkGoogleAccountRequestDto(
    string CurrentPassword
);

/// <summary>
/// Estado de vinculación de Google para el usuario autenticado (no expone identificadores externos).
/// </summary>
public record GoogleLinkStatusResponseDto(
    bool GoogleLinked
);
