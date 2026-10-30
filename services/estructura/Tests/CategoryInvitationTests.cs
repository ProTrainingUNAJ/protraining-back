using API.Middleware;
using Application.DTOs.Categories;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.UseCases.Categories.Invitations.GenerateCategoryInvitationToken;
using Application.UseCases.Categories.Invitations.ValidateCategoryInvitationToken;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Estructura.Tests;

public sealed class CategoryInvitationTests
{
    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Fact]
    public async Task Given_AuthenticatedPfAndExistingCategory_When_GenerateToken_Then_ReturnsUniqueTokenWith7DaysExpiration()
    {
        // Criterio 1: DADO un PF autenticado CUANDO solicita un token de invitación para la categoria "Primera División"
        // ENTONCES recibe un token unico con fecha de expiración a 7 días
        var now = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(now);

        var organizationId = Guid.NewGuid();
        var category = Category.Create(organizationId, "Primera División");

        var categoryRepoMock = new Mock<ICategoryRepository>();
        categoryRepoMock
            .Setup(r => r.GetByIdAsync(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var invitationRepoMock = new Mock<ICategoryInvitationRepository>();
        var unitOfWorkMock = new Mock<IUnitOfWork>();

        var handler = new GenerateCategoryInvitationTokenCommandHandler(
            categoryRepoMock.Object,
            invitationRepoMock.Object,
            unitOfWorkMock.Object,
            timeProvider);

        var result = await handler.HandleAsync(
            new GenerateCategoryInvitationTokenCommand(category.Id),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal(category.Id, result.CategoryId);
        Assert.Equal("Primera División", result.CategoryName);
        Assert.Equal(now.UtcDateTime.AddDays(7), result.ExpiresAt);

        invitationRepoMock.Verify(r => r.AddAsync(
            It.Is<CategoryInvitation>(i => i.CategoryId == category.Id && i.ExpiresAt == now.UtcDateTime.AddDays(7)),
            It.IsAny<CancellationToken>()), Times.Once);

        unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_NonExistingCategory_When_GenerateToken_Then_ThrowsCategoryNotFoundException()
    {
        var categoryRepoMock = new Mock<ICategoryRepository>();
        categoryRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        var handler = new GenerateCategoryInvitationTokenCommandHandler(
            categoryRepoMock.Object,
            new Mock<ICategoryInvitationRepository>().Object,
            new Mock<IUnitOfWork>().Object,
            TimeProvider.System);

        await Assert.ThrowsAsync<CategoryNotFoundException>(() =>
            handler.HandleAsync(new GenerateCategoryInvitationTokenCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Given_ExpiredToken_When_Validate_Then_ThrowsInvitationExpiredException()
    {
        // Criterio 2: DADO un token de invitacion que ya superó su fecha limite
        // CUANDO un usuario intenta validarlo ENTONCES lanza excepción que mapea a HTTP 410 Gone
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(now);

        var categoryId = Guid.NewGuid();
        var expiredDate = now.UtcDateTime.AddDays(-1); // Expiró ayer
        var invitation = CategoryInvitation.Create(categoryId, DateTime.UtcNow.AddMinutes(1), "token-vencido");

        // Usamos reflection para fijar la fecha expirada en el test
        typeof(CategoryInvitation).GetProperty(nameof(CategoryInvitation.ExpiresAt))!
            .SetValue(invitation, expiredDate);

        var details = new CategoryInvitationDetails(invitation, "Primera División");

        var invitationRepoMock = new Mock<ICategoryInvitationRepository>();
        invitationRepoMock
            .Setup(r => r.GetDetailsByTokenAsync("token-vencido", It.IsAny<CancellationToken>()))
            .ReturnsAsync(details);

        var handler = new ValidateCategoryInvitationTokenQueryHandler(invitationRepoMock.Object, timeProvider);

        await Assert.ThrowsAsync<InvitationExpiredException>(() =>
            handler.HandleAsync(new ValidateCategoryInvitationTokenQuery("token-vencido"), CancellationToken.None));
    }

    [Fact]
    public async Task Given_ValidToken_When_Validate_Then_ReturnsCategoryInvitationDtoWithCategoryName()
    {
        var now = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(now);

        var categoryId = Guid.NewGuid();
        var futureDate = now.UtcDateTime.AddDays(5);
        var invitation = CategoryInvitation.Create(categoryId, futureDate, "token-activo");

        var details = new CategoryInvitationDetails(invitation, "Primera División");

        var invitationRepoMock = new Mock<ICategoryInvitationRepository>();
        invitationRepoMock
            .Setup(r => r.GetDetailsByTokenAsync("token-activo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(details);

        var handler = new ValidateCategoryInvitationTokenQueryHandler(invitationRepoMock.Object, timeProvider);

        var result = await handler.HandleAsync(
            new ValidateCategoryInvitationTokenQuery("token-activo"),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("token-activo", result.Token);
        Assert.Equal(categoryId, result.CategoryId);
        Assert.Equal("Primera División", result.CategoryName);
        Assert.Equal(futureDate, result.ExpiresAt);
    }

    [Fact]
    public async Task Given_NonExistingToken_When_Validate_Then_ThrowsInvitationNotFoundException()
    {
        var invitationRepoMock = new Mock<ICategoryInvitationRepository>();
        invitationRepoMock
            .Setup(r => r.GetDetailsByTokenAsync("inexistente", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CategoryInvitationDetails?)null);

        var handler = new ValidateCategoryInvitationTokenQueryHandler(invitationRepoMock.Object, TimeProvider.System);

        await Assert.ThrowsAsync<InvitationNotFoundException>(() =>
            handler.HandleAsync(new ValidateCategoryInvitationTokenQuery("inexistente"), CancellationToken.None));
    }

    [Fact]
    public async Task Given_InvitationExpiredException_When_MiddlewareExecutes_Then_Returns410Gone()
    {
        // Criterio 2 en HTTP: Verificar que ExceptionMiddleware devuelve status 410 Gone
        var middleware = new ExceptionMiddleware(
            _ => throw new InvitationExpiredException(),
            NullLogger<ExceptionMiddleware>.Instance);

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        var serviceProvider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = serviceProvider };

        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status410Gone, context.Response.StatusCode);
    }
}