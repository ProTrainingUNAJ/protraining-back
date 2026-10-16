using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Athletes.GetMe;
using Domain.Entities;
using Moq;

namespace Estructura.Tests;

public class GetMeQueryHandlerTests
{
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IAthleteRepository> _athleteRepository = new();
    private readonly Mock<IAthleteCategoryRepository> _athleteCategoryRepository = new();
    private readonly GetMeQueryHandler _handler;

    public GetMeQueryHandlerTests()
    {
        _handler = new GetMeQueryHandler(
            _currentUser.Object,
            _athleteRepository.Object,
            _athleteCategoryRepository.Object);
    }

    [Fact]
    public async Task Given_AthleteLinkedToAccount_When_GetMe_Then_Returns_The_Athlete_With_Their_Categories()
    {
        // arrange
        var accountId = Guid.NewGuid();
        var athlete = Athlete.CreateAdult("Lucas", "Gomez", "atleta@protraining.com", "+5491100000000");
        athlete.LinkAccount(accountId);
        var organizationId = Guid.NewGuid();
        var categories = new List<Category> { Category.Create(organizationId, "Primera") };

        _currentUser.Setup(u => u.UserId).Returns(accountId);
        _athleteRepository.Setup(r => r.GetByAccountIdAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(athlete);
        _athleteCategoryRepository.Setup(r => r.ListCategoriesByAthleteAsync(athlete.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(categories);

        // act
        var result = await _handler.HandleAsync(new GetMeQuery());

        // assert
        Assert.Equal(athlete.Id, result.AthleteId);
        Assert.Equal("Lucas Gomez", result.FullName);
        Assert.True(result.Active);
        var category = Assert.Single(result.Categories);
        Assert.Equal("Primera", category.Name);
    }

    [Fact]
    public async Task Given_InactiveAthlete_When_GetMe_Then_Returns_The_Athlete_Without_Categories()
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
        var result = await _handler.HandleAsync(new GetMeQuery());

        // assert
        Assert.Equal(athlete.Id, result.AthleteId);
        Assert.False(result.Active);
        Assert.Empty(result.Categories);
        _athleteCategoryRepository.Verify(
            r => r.ListCategoriesByAthleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_AccountWithoutLinkedAthlete_When_GetMe_Then_Throws_AthleteNotFound()
    {
        // arrange
        var accountId = Guid.NewGuid();
        _currentUser.Setup(u => u.UserId).Returns(accountId);
        _athleteRepository.Setup(r => r.GetByAccountIdAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Athlete?)null);

        // act & assert
        await Assert.ThrowsAsync<AthleteNotFoundException>(() => _handler.HandleAsync(new GetMeQuery()));
    }

    [Fact]
    public async Task Given_NoUserInToken_When_GetMe_Then_Throws_Unauthorized()
    {
        // arrange
        _currentUser.Setup(u => u.UserId).Returns((Guid?)null);

        // act & assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _handler.HandleAsync(new GetMeQuery()));
    }
}
