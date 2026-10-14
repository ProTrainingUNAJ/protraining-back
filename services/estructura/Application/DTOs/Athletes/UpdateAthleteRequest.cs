namespace Application.DTOs.Athletes;

/// <summary>
/// Actualiza los datos de contacto del atleta. Nombre, apellido y correo son inmutables
/// post-creación por diseño del dominio (#015); si se necesita modificarlos, se deberá
/// agregar un método en la entidad Athlete en una tarea futura.
/// </summary>
public sealed record UpdateAthleteRequest(
    string? Phone,
    string? EmergencyContact,
    string? MedicalConditions);
