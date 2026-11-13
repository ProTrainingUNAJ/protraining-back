using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;

namespace Application.Services;

public sealed class CategoryAccessValidator(
    ICurrentUser currentUser,
    ICategoryRepository categoryRepository,
    IAthleteRepository athleteRepository,
    IAthleteCategoryRepository athleteCategoryRepository) : ICategoryAccessValidator
{
    public async Task<Category> ValidateReadAccessAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        if (currentUser.Role == "Atleta")
        {
            var accountId = currentUser.UserId ?? throw new UnauthorizedAccessException();
            var athlete = await athleteRepository.GetByAccountIdAsync(accountId, cancellationToken);
            if (athlete is null || !athlete.Active)
            {
                throw new CategoryNotFoundException();
            }

            var membership = await athleteCategoryRepository.GetAsync(athlete.Id, categoryId, cancellationToken);
            if (membership is null)
            {
                throw new CategoryNotFoundException();
            }

            var category = await categoryRepository.GetByIdAsync(categoryId, cancellationToken);
            if (category is null)
            {
                throw new CategoryNotFoundException();
            }

            return category;
        }

        // Default: PreparadorFisico (or service tokens acting as PF)
        var pfCategory = await categoryRepository.GetByIdAsync(categoryId, cancellationToken);
        if (pfCategory is null)
        {
            throw new CategoryNotFoundException();
        }

        return pfCategory;
    }

    public async Task<Category> ValidateWriteAccessAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        var category = await categoryRepository.GetByIdAsync(categoryId, cancellationToken);
        if (category is null)
        {
            throw new CategoryNotFoundException();
        }

        return category;
    }
}
