using Application.DTOs.Categories;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Categories.Invitations.AcceptCategoryInvitation;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;
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
            Phone: "+5491112345678",
            BirthDate: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20)));

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Lautaro", result.FirstName);
        Assert.Equal("Martínez", result.LastName);
        _athleteRepoMock.Verify(r => r.AddAsync(It.Is<Athlete>(a => a.AccountId == athleteAccountId && a.Email == command.Email && !a.IsMinor), It.IsAny<CancellationToken>()), Times.Once);
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
        var command = new AcceptCategoryInvitationCommand(
            token, "Lautaro", "Martínez", "lautaro@protraining.com", "+5491112345678",
            BirthDate: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20)));

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
        var command = new AcceptCategoryInvitationCommand(
            token, "Lautaro", "Martínez", "lautaro@protraining.com", "+5491112345678",
            BirthDate: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20)));

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
        var command = new AcceptCategoryInvitationCommand(
            "inexistente", "Lautaro", "Martínez", "lautaro@protraining.com", "+5491112345678",
            BirthDate: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20)));

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
        var command = new AcceptCategoryInvitationCommand(
            token, "Lautaro", "Martínez", "lautaro@protraining.com", "+5491112345678",
            BirthDate: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20)));

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
        var command = new AcceptCategoryInvitationCommand(
            "token", "Lautaro", "Martínez", "lautaro@protraining.com", "+5491112345678",
            BirthDate: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20)));

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.HandleAsync(command));
    }

    // --- Tests KAN-401 (Consentimiento de Menores) ---

    [Fact]
    public async Task Given_AdultAthlete20YearsOld_When_AcceptInvitation_Then_CreatesAdultWithoutRequiringGuardian()
    {
        // Arrange
        var athleteAccountId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        const string token = "token-adulto";

        _currentUserMock.Setup(u => u.UserId).Returns(athleteAccountId);
        _currentUserMock.Setup(u => u.PfId).Returns(pfId);

        var invitation = CategoryInvitation.Create(categoryId, _fixedNow.UtcDateTime.AddDays(5), token);
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!.SetValue(invitation, pfId);
        _invitationRepoMock.Setup(r => r.GetDetailsByTokenAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryInvitationDetails(invitation, "Sub-20"));

        _athleteRepoMock.Setup(r => r.GetByAccountIdAsync(athleteAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Athlete?)null);

        var handler = CreateHandler();
        var command = new AcceptCategoryInvitationCommand(
            Token: token,
            FirstName: "Julián",
            LastName: "Álvarez",
            Email: "julian@protraining.com",
            Phone: "+5491122223333",
            BirthDate: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20)), // 20 años
            GuardianName: null,
            GuardianEmail: null,
            GuardianPhone: null);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert: Creado como mayor (inicia sin consentimiento firmado hasta firmarlo en /me/consent, KAN-326)
        Assert.NotNull(result);
        _athleteRepoMock.Verify(r => r.AddAsync(It.Is<Athlete>(a => !a.IsMinor && !a.HasSignedConsent), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_MinorAthlete16YearsOld_When_MissingGuardianName_Then_ThrowsDomainException_AndDoesNotCreateAthlete()
    {
        // Arrange
        var athleteAccountId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        const string token = "token-menor-sin-nombre";

        _currentUserMock.Setup(u => u.UserId).Returns(athleteAccountId);
        _currentUserMock.Setup(u => u.PfId).Returns(pfId);

        var invitation = CategoryInvitation.Create(categoryId, _fixedNow.UtcDateTime.AddDays(5), token);
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!.SetValue(invitation, pfId);
        _invitationRepoMock.Setup(r => r.GetDetailsByTokenAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryInvitationDetails(invitation, "Sub-16"));

        var handler = CreateHandler();
        var command = new AcceptCategoryInvitationCommand(
            Token: token,
            FirstName: "Mateo",
            LastName: "Messi",
            Email: "mateo@protraining.com",
            Phone: "+5491133334444",
            BirthDate: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-16)), // 16 años
            GuardianName: null, // Falta tutor
            GuardianEmail: "padre@protraining.com",
            GuardianPhone: "+5491155556666",
            AllowsPhysicalTraining: true);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.HandleAsync(command));
        Assert.Contains("nombre del tutor es obligatorio", ex.Message);
        _athleteRepoMock.Verify(r => r.AddAsync(It.IsAny<Athlete>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_MinorAthlete16YearsOld_When_MissingBothEmailAndPhoneOfGuardian_Then_ThrowsDomainException()
    {
        // Arrange
        var athleteAccountId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        const string token = "token-menor-sin-contacto";

        _currentUserMock.Setup(u => u.UserId).Returns(athleteAccountId);
        _currentUserMock.Setup(u => u.PfId).Returns(pfId);

        var invitation = CategoryInvitation.Create(categoryId, _fixedNow.UtcDateTime.AddDays(5), token);
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!.SetValue(invitation, pfId);
        _invitationRepoMock.Setup(r => r.GetDetailsByTokenAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryInvitationDetails(invitation, "Sub-16"));

        var handler = CreateHandler();
        var command = new AcceptCategoryInvitationCommand(
            Token: token,
            FirstName: "Mateo",
            LastName: "Messi",
            Email: "mateo@protraining.com",
            Phone: "+5491133334444",
            BirthDate: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-16)),
            GuardianName: "Jorge Messi",
            GuardianEmail: null,
            GuardianPhone: null, // Ni email ni teléfono
            AllowsPhysicalTraining: true);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.HandleAsync(command));
        Assert.Contains("medio de contacto del tutor", ex.Message);
        _athleteRepoMock.Verify(r => r.AddAsync(It.IsAny<Athlete>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_MinorAthlete16YearsOld_When_DoesNotAllowPhysicalTraining_Then_ThrowsDomainException()
    {
        // Arrange
        var athleteAccountId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        const string token = "token-menor-sin-permiso";

        _currentUserMock.Setup(u => u.UserId).Returns(athleteAccountId);
        _currentUserMock.Setup(u => u.PfId).Returns(pfId);

        var invitation = CategoryInvitation.Create(categoryId, _fixedNow.UtcDateTime.AddDays(5), token);
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!.SetValue(invitation, pfId);
        _invitationRepoMock.Setup(r => r.GetDetailsByTokenAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryInvitationDetails(invitation, "Sub-16"));

        var handler = CreateHandler();
        var command = new AcceptCategoryInvitationCommand(
            Token: token,
            FirstName: "Mateo",
            LastName: "Messi",
            Email: "mateo@protraining.com",
            Phone: "+5491133334444",
            BirthDate: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-16)),
            GuardianName: "Jorge Messi",
            GuardianPhone: "+5491155556666",
            AllowsPhysicalTraining: false); // Rechaza preparación física

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.HandleAsync(command));
        Assert.Contains("autorizar obligatoriamente la preparación física", ex.Message);
        _athleteRepoMock.Verify(r => r.AddAsync(It.IsAny<Athlete>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_MinorAthlete16YearsOld_When_ValidGuardianDataProvided_Then_CreatesMinorAthleteWithConsent()
    {
        // Arrange
        var athleteAccountId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        const string token = "token-menor-valido";

        _currentUserMock.Setup(u => u.UserId).Returns(athleteAccountId);
        _currentUserMock.Setup(u => u.PfId).Returns(pfId);

        var invitation = CategoryInvitation.Create(categoryId, _fixedNow.UtcDateTime.AddDays(5), token);
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!.SetValue(invitation, pfId);
        _invitationRepoMock.Setup(r => r.GetDetailsByTokenAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryInvitationDetails(invitation, "Sub-16"));

        _athleteRepoMock.Setup(r => r.GetByAccountIdAsync(athleteAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Athlete?)null);

        var handler = CreateHandler();
        var command = new AcceptCategoryInvitationCommand(
            Token: token,
            FirstName: "Mateo",
            LastName: "Messi",
            Email: "mateo@protraining.com",
            Phone: "+5491133334444",
            BirthDate: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-16)), // 16 años
            GuardianName: "Jorge Messi",
            GuardianEmail: "jorge@protraining.com",
            GuardianPhone: "+5491155556666",
            AllowsPhysicalTraining: true,
            AllowsHabitsRecommendations: true);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert: Se crea como menor de edad con consentimiento digital de tutor
        Assert.NotNull(result);
        _athleteRepoMock.Verify(r => r.AddAsync(It.Is<Athlete>(a => a.IsMinor && a.HasSignedConsent), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task Given_BodyEmailDifferentFromAccountEmail_When_AcceptInvitation_Then_UsesAccountEmail()
    {
        // Arrange
        var athleteAccountId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        const string token = "token-email-test";
        const string accountEmail = "cuenta.login@protraining.com";
        const string bodyEmail = "otro.email@distinto.com";

        _currentUserMock.Setup(u => u.UserId).Returns(athleteAccountId);
        _currentUserMock.Setup(u => u.PfId).Returns(pfId);
        _currentUserMock.Setup(u => u.Email).Returns(accountEmail);

        var invitation = CategoryInvitation.Create(categoryId, _fixedNow.UtcDateTime.AddDays(5), token);
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!.SetValue(invitation, pfId);
        _invitationRepoMock.Setup(r => r.GetDetailsByTokenAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryInvitationDetails(invitation, "Sub-20"));

        _athleteRepoMock.Setup(r => r.GetByAccountIdAsync(athleteAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Athlete?)null);

        var handler = CreateHandler();
        var command = new AcceptCategoryInvitationCommand(
            Token: token,
            FirstName: "Lautaro",
            LastName: "Martinez",
            Email: bodyEmail,
            Phone: "+5491133334444",
            BirthDate: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20)));

        // Act
        var result = await handler.HandleAsync(command);

        // Assert: El atleta debe crearse con el email de la cuenta autenticada y no con el del body
        Assert.NotNull(result);
        _athleteRepoMock.Verify(r => r.AddAsync(
            It.Is<Athlete>(a => a.Email == accountEmail && a.AccountId == athleteAccountId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_FutureBirthDate_When_AcceptInvitation_Then_ThrowsDomainException_AndDoesNotCreateAthlete()
    {
        // Arrange
        var athleteAccountId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        const string token = "token-fecha-futura";

        _currentUserMock.Setup(u => u.UserId).Returns(athleteAccountId);
        _currentUserMock.Setup(u => u.PfId).Returns(pfId);

        var invitation = CategoryInvitation.Create(categoryId, _fixedNow.UtcDateTime.AddDays(5), token);
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!.SetValue(invitation, pfId);
        _invitationRepoMock.Setup(r => r.GetDetailsByTokenAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryInvitationDetails(invitation, "Sub-15"));

        _athleteRepoMock.Setup(r => r.GetByAccountIdAsync(athleteAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Athlete?)null);

        var handler = CreateHandler();
        var futureBirthDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var command = new AcceptCategoryInvitationCommand(
            Token: token,
            FirstName: "Mateo",
            LastName: "Menor",
            Email: "mateo@protraining.com",
            Phone: "+5491133334444",
            BirthDate: futureBirthDate);

        // Act & Assert: lanza DomainException con mensaje claro y no crea el atleta
        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.HandleAsync(command));
        Assert.Contains("no puede ser futura", ex.Message, StringComparison.OrdinalIgnoreCase);
        _athleteRepoMock.Verify(r => r.AddAsync(It.IsAny<Athlete>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(16, true)]   // 16 años -> menor
    [InlineData(17, true)]   // 17 años -> menor
    [InlineData(18, false)]  // 18 años -> mayor
    [InlineData(25, false)]  // 25 años -> mayor
    public void Given_ReferenceDate_When_IsMinorOn_Then_CalculatesCorrectly(int yearsAgo, bool expectedMinor)
    {
        var today = new DateOnly(2026, 11, 6);
        var birthDate = today.AddYears(-yearsAgo);

        var isMinor = Athlete.IsMinorOn(birthDate, today);

        Assert.Equal(expectedMinor, isMinor);
    }

    [Fact]
    public void Given_BirthdayTomorrow_When_IsMinorOn_Then_StillMinor()
    {
        // 17 años y 364 días (cumple 18 mañana)
        var today = new DateOnly(2026, 11, 6);
        var birthDate = new DateOnly(2008, 11, 7);

        var isMinor = Athlete.IsMinorOn(birthDate, today);

        Assert.True(isMinor);
    }

    [Fact]
    public void Given_FutureBirthDate_When_IsMinorOn_Then_ThrowsDomainException()
    {
        var today = new DateOnly(2026, 11, 6);
        var futureDate = today.AddDays(1);

        var ex = Assert.Throws<DomainException>(() => Athlete.IsMinorOn(futureDate, today));
        Assert.Contains("no puede ser futura", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}