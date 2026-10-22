using Application.DTOs.FitnessTests;
using Application.Interfaces.Persistence;

namespace Application.UseCases.FitnessTests.ListFitnessTests;

// Caso de uso de lectura (KAN-333): solo consulta y mapea a DTO, nunca guarda cambios.
public sealed class ListFitnessTestsQueryHandler(ICatalogTestRepository catalogTestRepository)
{
    public async Task<List<FitnessTestDto>> HandleAsync(
        ListFitnessTestsQuery query,
        CancellationToken cancellationToken = default)
    {
        var tests = await catalogTestRepository.ListAsync(query.MotorCapacity, cancellationToken);

        return tests
            .Select(test => new FitnessTestDto(
                test.Id,
                test.Name,
                test.MotorCapacity.ToString(),
                test.MeasurementUnit,
                test.DifficultyLevel,
                test.Active))
            .ToList();
    }
}
