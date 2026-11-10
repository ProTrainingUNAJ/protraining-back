using Application.DTOs.FieldTests;

namespace Application.UseCases.FieldTests.CreateFieldTestSession;

// Alta de una sesión de test de campo: la categoría, la fecha, el nombre y las
// pruebas (con su orden) vienen en el cuerpo del pedido. Mismo criterio que
// CompleteOnboardingCommand: el comando reutiliza el tipo de entrada del request.
public sealed record CreateFieldTestSessionCommand(
    Guid CategoryId,
    DateTime Date,
    string Name,
    List<FieldTestSessionTestInput> Tests);
