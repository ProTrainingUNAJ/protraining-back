using Domain.Entities;

namespace Application.Interfaces.Services;

public interface ICategoryAccessValidator
{
    Task<Category> ValidateReadAccessAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task<Category> ValidateWriteAccessAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
