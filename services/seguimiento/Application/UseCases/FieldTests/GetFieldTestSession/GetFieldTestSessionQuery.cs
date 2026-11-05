namespace Application.UseCases.FieldTests.GetFieldTestSession;

// Lectura de la sesión con sus tests y resultados para la pantalla del móvil (KAN-425).
public sealed record GetFieldTestSessionQuery(Guid SessionId);
