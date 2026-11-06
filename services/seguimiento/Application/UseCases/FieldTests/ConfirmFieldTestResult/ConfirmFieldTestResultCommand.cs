namespace Application.UseCases.FieldTests.ConfirmFieldTestResult;

// Confirmación de un resultado de campo (KAN-426): la sesión y el resultado vienen en
// la ruta. No persiste estado de confirmación (re-confirmar re-envía el mismo valor,
// inocuo porque Entrenamiento desactiva el récord anterior en su transacción).
public sealed record ConfirmFieldTestResultCommand(
    Guid SessionId,
    Guid ResultId);
