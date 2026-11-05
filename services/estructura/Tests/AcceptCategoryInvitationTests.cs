using Application.DTOs.Categories;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Categories.Invitations.AcceptCategoryInvitation;
using Domain.Common;
using Domain.Entities;
using Moq;
using Xunit;

namespace Estructura.Tests;

public sealed class AcceptCategoryInvitationTests
{
    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<ICategoryInvitationRepository> _invitationRepoMock = new();
    private readonly Mock<IAthleteRepository> _athleteRepoMock = new();
    private readonly Mock<IAthleteCategoryRepository> _athleteCategoryRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly DateTimeOffset _fixedNow = DateTimeOffset.UtcNow;

    private AcceptCategoryInvitationCommandHandler CreateHandler(TimeProvider? timeProvider = null) =>
        new(
            _currentUserMock.Object,
            _invitationRepoMock.Object,
            _athleteRepoMock.Object,
            _athleteCategoryRepoMock.Object,
            _unitOfWorkMock.Object,
            timeProvider ?? new TestTimeProvider(_fixedNow));

    [Fact]
    public async Task Given_ValidTokenAndMatchingPfId_When_AcceptInvitation_Then_CreatesAthleteAndAssignsCategory()
    {
        // Arrange
        var athleteAccountId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        const string token = "invitation-token-123";

        _currentUserMock.Setup(u => u.UserId).Returns(athleteAccountId);
        _currentUserMock.Setup(u => u.PfId).Returns(pfId);

        var invitation = CategoryInvitation.Create(categoryId, _fixedNow.UtcDateTime.AddDays(5), token);
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!
            .SetValue(invitation, pfId);

        var details = new CategoryInvitationDetails(invitation, "Primera División");
        _invitationRepoMock.Setup(r => r.GetDetailsByTokenAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(details);

        _athleteRepoMock.Setup(r => r.GetByAccountIdAsync(athleteAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Athlete?)null);

        _athleteCategoryRepoMock.Setup(r => r.GetAsync(It.IsAny<Guid>(), categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthleteCategory?)null);

        var handler = CreateHandler();
        var command = new AcceptCategoryInvitationCommand(
            Token: token,
            FirstName: "Lautaro",
            LastName: "Martínez",
            Email: "lautaro@protraining.com",
            Phone: "+5491112345678");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Lautaro", result.FirstName);
        Assert.Equal("Martínez", result.LastName);
        _athleteRepoMock.Verify(r => r.AddAsync(It.Is<Athlete>(a => a.AccountId == athleteAccountId && a.Email == command.Email), It.IsAny<CancellationToken>()), Times.Once);
        _athleteCategoryRepoMock.Verify(r => r.AddAsync(It.Is<AthleteCategory>(ac => ac.CategoryId == categoryId), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_AthleteAlreadyExistsAndAssigned_When_AcceptInvitationAgain_Then_IsIdempotentAndDoesNotDuplicate()
    {
        // Arrange
        var athleteAccountId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        const string token = "invitation-token-123";

        _currentUserMock.Setup(u => u.UserId).Returns(athleteAccountId);
        _currentUserMock.Setup(u => u.PfId).Returns(pfId);

        var invitation = CategoryInvitation.Create(categoryId, _fixedNow.UtcDateTime.AddDays(5), token);
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!
            .SetValue(invitation, pfId);

        var details = new CategoryInvitationDetails(invitation, "Primera División");
        _invitationRepoMock.Setup(r => r.GetDetailsByTokenAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(details);

        var existingAthlete = Athlete.CreateAdult("Lautaro", "Martínez", "lautaro@protraining.com", "+5491112345678");
        existingAthlete.LinkAccount(athleteAccountId);

        _athleteRepoMock.Setup(r => r.GetByAccountIdAsync(athleteAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAthlete);

        var existingAssignment = AthleteCategory.Assign(existingAthlete.Id, categoryId);
        _athleteCategoryRepoMock.Setup(r => r.GetAsync(existingAthlete.Id, categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAssignment);

        var handler = CreateHandler();
        var command = new AcceptCategoryInvitationCommand(token, "Lautaro", "Martínez", "lautaro@protraining.com", "+5491112345678");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(existingAthlete.Id, result.Id);
        _athleteRepoMock.Verify(r => r.AddAsync(It.IsAny<Athlete>(), It.IsAny<CancellationToken>()), Times.Never);
        _athleteCategoryRepoMock.Verify(r => r.AddAsync(It.IsAny<AthleteCategory>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_ExpiredToken_When_AcceptInvitation_Then_ThrowsInvitationExpiredException()
    {
        // Arrange
        var athleteAccountId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        const string token = "token-expirado";

        _currentUserMock.Setup(u => u.UserId).Returns(athleteAccountId);
        _currentUserMock.Setup(u => u.PfId).Returns(pfId);

        var invitation = CategoryInvitation.Create(categoryId, _fixedNow.UtcDateTime.AddDays(1), token);
        typeof(CategoryInvitation).GetProperty(nameof(CategoryInvitation.ExpiresAt))!
            .SetValue(invitation, _fixedNow.UtcDateTime.AddDays(-1));
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!
            .SetValue(invitation, pfId);

        var details = new CategoryInvitationDetails(invitation, "Primera División");
        _invitationRepoMock.Setup(r => r.GetDetailsByTokenAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(details);

        var handler = CreateHandler();
        var command = new AcceptCategoryInvitationCommand(token, "Lautaro", "Martínez", "lautaro@protraining.com", "+5491112345678");

        // Act & Assert
        await Assert.ThrowsAsync<InvitationExpiredException>(() => handler.HandleAsync(command));
        _athleteRepoMock.Verify(r => r.AddAsync(It.IsAny<Athlete>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_InvalidOrNonExistingToken_When_AcceptInvitation_Then_ThrowsInvitationNotFoundException()
    {
        // Arrange
        _currentUserMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        _currentUserMock.Setup(u => u.PfId).Returns(Guid.NewGuid());

        _invitationRepoMock.Setup(r => r.GetDetailsByTokenAsync("inexistente", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CategoryInvitationDetails?)null);

        var handler = CreateHandler();
        var command = new AcceptCategoryInvitationCommand("inexistente", "Lautaro", "Martínez", "lautaro@protraining.com", "+5491112345678");

        // Act & Assert
        await Assert.ThrowsAsync<InvitationNotFoundException>(() => handler.HandleAsync(command));
    }

    [Fact]
    public async Task Given_MismatchingPfIdInToken_When_AcceptInvitation_Then_ThrowsForbiddenException()
    {
        // Arrange
        var athleteAccountId = Guid.NewGuid();
        var invitationPfId = Guid.NewGuid();
        var differentPfIdInToken = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        const string token = "invitation-token-123";

        _currentUserMock.Setup(u => u.UserId).Returns(athleteAccountId);
        _currentUserMock.Setup(u => u.PfId).Returns(differentPfIdInToken); // No coincide con la invitación

        var invitation = CategoryInvitation.Create(categoryId, _fixedNow.UtcDateTime.AddDays(5), token);
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!
            .SetValue(invitation, invitationPfId);

        var details = new CategoryInvitationDetails(invitation, "Primera División");
        _invitationRepoMock.Setup(r => r.GetDetailsByTokenAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(details);

        var handler = CreateHandler();
        var command = new AcceptCategoryInvitationCommand(token, "Lautaro", "Martínez", "lautaro@protraining.com", "+5491112345678");

        // Act & Assert (Criterio: rechazo si el pf_id del token no coincide con el de la invitación -> 403 Forbidden)
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.HandleAsync(command));
        _athleteRepoMock.Verify(r => r.AddAsync(It.IsAny<Athlete>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_NoAuthenticatedUser_When_AcceptInvitation_Then_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        _currentUserMock.Setup(u => u.UserId).Returns((Guid?)null);

        var handler = CreateHandler();
        var command = new AcceptCategoryInvitationCommand("token", "Lautaro", "Martínez", "lautaro@protraining.com", "+5491112345678");

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.HandleAsync(command));
    }
}