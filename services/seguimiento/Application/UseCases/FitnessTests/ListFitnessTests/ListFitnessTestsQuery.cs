using Domain.Entities;

namespace Application.UseCases.FitnessTests.ListFitnessTests;

// Consulta del catálogo de tests físicos (KAN-333).
// El filtro por capacidad motora es opcional: sin parámetro se pide el catálogo completo.
public sealed record ListFitnessTestsQuery(MotorCapacity? MotorCapacity = null);
