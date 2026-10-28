using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Interfaces.Services;

public interface ISeasonPhaseValidationService
{
    Task ValidatePhaseDatesAsync(
        Guid categoryId,
        DateOnly startDate,
        DateOnly endDate,
        Guid? excludePhaseId = null,
        CancellationToken cancellationToken = default);
}
