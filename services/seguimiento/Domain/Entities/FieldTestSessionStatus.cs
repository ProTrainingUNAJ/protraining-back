namespace Domain.Entities;

/// <summary>
/// Estado de una sesión de test de campo (KAN-424). Se define con valores explícitos para
/// garantizar un contrato estable y limpio con el front-end, igual que MotorCapacity.
/// </summary>
public enum FieldTestSessionStatus
{
    Planned = 1,
    InProgress = 2,
    Closed = 3
}
