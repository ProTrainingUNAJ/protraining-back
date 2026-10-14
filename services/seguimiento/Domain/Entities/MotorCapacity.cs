namespace Domain.Entities;

/// <summary>
/// Representa las 7 capacidades motoras estándar del catálogo de pruebas físicas.
/// Se definen con valores explícitos para garantizar un contrato estable y limpio con el front-end 
/// y evitar depender de cadenas de texto libres en los filtros por query string.
/// </summary>
public enum MotorCapacity
{
    Strength = 1,
    Velocity = 2,
    Endurance = 3,
    Power = 4,
    Flexibility = 5,
    Agility = 6,
    Coordination = 7
}
