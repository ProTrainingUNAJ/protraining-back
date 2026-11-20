using Application.DTOs.FitnessTests;
using Application.Interfaces.Persistence;

namespace Application.UseCases.FitnessTests.ListAthleteBestMarks
{
    public class ListAthleteBestMarksQueryHandler
    {
        private readonly IFieldTestResultRepository _repository;

        public ListAthleteBestMarksQueryHandler(IFieldTestResultRepository repository)
        {
            _repository = repository;
        }

        public async Task<IReadOnlyList<AthleteBestMarkDto>> HandleAsync(
            ListAthleteBestMarksQuery query,
            CancellationToken cancellationToken = default)
        {
            return await _repository.ListBestMarksByAthleteAsync(query.AthleteId, cancellationToken);
        }
    }
}
