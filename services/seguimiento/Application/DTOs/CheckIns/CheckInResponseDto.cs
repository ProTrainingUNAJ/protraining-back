namespace Application.DTOs.CheckIns;

// Respuesta del check-in diario: mensaje, color del semáforo y el registro guardado (contrato: CheckInResponse).
public sealed record CheckInResponseDto(
    string Message,
    string TrafficLight,
    string? TrafficLightReason,
    CheckInDto CheckIn);
