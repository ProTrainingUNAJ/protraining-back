namespace Application.UseCases.FieldTests.CreateFieldTestResult;

// Carga rápida de un resultado (KAN-425): la sesión viene en la ruta y el resto en el
// cuerpo del pedido del móvil.
public sealed record CreateFieldTestResultCommand(
    Guid SessionId,
    Guid CatalogTestId,
    Guid AthleteId,
    decimal Value,
    int Attempt);
