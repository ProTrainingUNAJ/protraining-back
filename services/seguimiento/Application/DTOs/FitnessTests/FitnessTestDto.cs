namespace Application.DTOs.FitnessTests;

// La capacidad motora viaja como texto (nombre del enum, p. ej. "Power") para que el
// contrato con el front-end no dependa de la representación numérica del enum.
public sealed record FitnessTestDto(
    Guid Id,
    string Name,
    string MotorCapacity,
    string MeasurementUnit,
    int DifficultyLevel,
    bool Active);
