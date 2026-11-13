namespace Application.UseCases.FieldTests.UpdateFieldTestResult;

// Corrección de un resultado ya cargado (KAN-425): la sesión y el resultado vienen en
// la ruta y el valor corregido en el cuerpo.
public sealed record UpdateFieldTestResultCommand(
    Guid SessionId,
    Guid ResultId,
    decimal Value,
    int Attempt);
