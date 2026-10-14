using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Athletes.ListMyCategories;
using Domain.Entities;
using Moq;

namespace Estructura.Tests;

public class ListMyCategoriesQueryHandlerTests
{
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IAthleteRepository> _athleteRepository = new();
    private readonly Mock<IAthleteCategoryRepository> _athleteCategoryRepository = new();
    private readonly ListMyCategoriesQueryHandler _handler;

    public ListMyCategoriesQueryHandlerTests()
    {
        _handler = new ListMyCategoriesQueryHandler(
            _currentUser.Object,
            _athleteRepository.Object,
            _athleteCategoryRepository.Object);
    }

    [Fact]
    public async Task Given_AthleteLinkedToAccount_When_ListMyCategories_Then_Returns_Their_Categories()
    {
        // arrange
        var accountId = Guid.NewGuid();
        var athlete = Athlete.CreateAdult("Lucas", "Gomez", "atleta@protraining.com", "+5491100000000");
        athlete.LinkAccount(accountId);
        var organizationId = Guid.NewGuid();
        var categories = new List<Category>
        {
            Category.Create(organizationId, "Primera"),
            Category.Create(organizationId, "Sub-20")
        };

        _currentUser.Setup(u => u.UserId).Returns(accountId);
        _athleteRepository.Setup(r => r.GetByAccountIdAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(athlete);
        _athleteCategoryRepository.Setup(r => r.ListCategoriesByAthleteAsync(athlete.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(categories);

        // act
        var result = await _handler.HandleAsync(new ListMyCategoriesQuery());

        // assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.Name == "Primera");
        Assert.Contains(result, c => c.Name == "Sub-20");
    }

    [Fact]
    public async Task Given_InactiveAthlete_When_ListMyCategories_Then_Returns_Empty_Without_Querying_Memberships()
    {
        // arrange
        var accountId = Guid.NewGuid();
        var athlete = Athlete.CreateAdult("Lucas", "Gomez", "atleta@protraining.com", "+5491100000000");
        athlete.LinkAccount(accountId);
        athlete.SetActive(false);

        _currentUser.Setup(u => u.UserId).Returns(accountId);
        _athleteRepository.Setup(r => r.GetByAccountIdAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(athlete);

        // act
        var result = await _handler.HandleAsync(new ListMyCategoriesQuery());

        // assert
        Assert.Empty(result);
        _athleteCategoryRepository.Verify(
            r => r.ListCategoriesByAthleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_AccountWithoutLinkedAthlete_When_ListMyCategories_Then_Throws_AthleteNotFound()
    {
        // arrange
        var accountId = Guid.NewGuid();
        _currentUser.Setup(u => u.UserId).Returns(accountId);
        _athleteRepository.Setup(r => r.GetByAccountIdAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Athlete?)null);

        // act & assert
        await Assert.ThrowsAsync<AthleteNotFoundException>(() => _handler.HandleAsync(new ListMyCategoriesQuery()));
    }

    [Fact]
    public async Task Given_NoUserInToken_When_ListMyCategories_Then_Throws_Unauthorized()
    {
        // arrange
        _currentUser.Setup(u => u.UserId).Returns((Guid?)null);

        // act & assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _handler.HandleAsync(new ListMyCategoriesQuery()));
    }
}
