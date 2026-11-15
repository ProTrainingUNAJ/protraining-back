using Application.Common;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.UseCases.Users.DeleteUserPhoto;
using Application.UseCases.Users.GetPfPhoto;
using Application.UseCases.Users.GetUserPhoto;
using Application.UseCases.Users.UploadUserPhoto;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;

namespace Identidad.Tests;

public class UserPhotoTests
{
    private static byte[] CreateValidWebpBytes(int length = 64)
    {
        var bytes = new byte[Math.Max(length, 12)];
        bytes[0] = (byte)'R';
        bytes[1] = (byte)'I';
        bytes[2] = (byte)'F';
        bytes[3] = (byte)'F';
        bytes[8] = (byte)'W';
        bytes[9] = (byte)'E';
        bytes[10] = (byte)'B';
        bytes[11] = (byte)'P';
        return bytes;
    }

    private static Mock<UserManager<User>> CreateMockUserManager()
    {
        var store = new Mock<IUserStore<User>>();
        return new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    [Fact]
    public void WebpImageValidator_ValidWebp_DoesNotThrow()
    {
        var data = CreateValidWebpBytes(100);
        var exception = Record.Exception(() => WebpImageValidator.Validate(data));
        Assert.Null(exception);
    }

    [Fact]
    public void WebpImageValidator_FileTooLarge_ThrowsDomainException()
    {
        var largeData = CreateValidWebpBytes(WebpImageValidator.MaxSizeBytes + 10);
        var ex = Assert.Throws<DomainException>(() => WebpImageValidator.Validate(largeData));
        Assert.Contains("200 KB", ex.Message);
    }

    [Fact]
    public async Task UploadUserPhoto_WhenUserExists_SavesPhotoAndUpdatesUser()
    {
        var user = new User { Id = Guid.NewGuid(), UserName = "coach@test.com" };
        var mockUserManager = CreateMockUserManager();
        mockUserManager.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        mockUserManager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        var mockPhotoRepo = new Mock<IUserPhotoRepository>();
        var mockUnitOfWork = new Mock<IUnitOfWork>();

        var handler = new UploadUserPhotoCommandHandler(mockUserManager.Object, mockPhotoRepo.Object, mockUnitOfWork.Object);
        var command = new UploadUserPhotoCommand(user.Id, CreateValidWebpBytes(80), "image/webp", 80);

        await handler.HandleAsync(command);

        Assert.True(user.HasPhoto);
        Assert.NotNull(user.PhotoUpdatedAt);
        mockPhotoRepo.Verify(r => r.AddAsync(It.IsAny<UserPhoto>(), It.IsAny<CancellationToken>()), Times.Once);
        mockUnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetUserPhoto_WhenNotFound_ThrowsPhotoNotFoundException()
    {
        var mockPhotoRepo = new Mock<IUserPhotoRepository>();
        mockPhotoRepo.Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserPhoto?)null);

        var handler = new GetUserPhotoQueryHandler(mockPhotoRepo.Object);
        await Assert.ThrowsAsync<PhotoNotFoundException>(() => handler.HandleAsync(new GetUserPhotoQuery(Guid.NewGuid())));
    }

    [Fact]
    public async Task GetPfPhoto_WhenExists_ReturnsPhotoDto()
    {
        var pfId = Guid.NewGuid();
        var bytes = CreateValidWebpBytes(90);
        var photo = UserPhoto.Create(pfId, bytes, "image/webp", 90);

        var mockPhotoRepo = new Mock<IUserPhotoRepository>();
        mockPhotoRepo.Setup(r => r.GetByUserIdAsync(pfId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(photo);

        var handler = new GetPfPhotoQueryHandler(mockPhotoRepo.Object);
        var result = await handler.HandleAsync(new GetPfPhotoQuery(pfId));

        Assert.Equal("image/webp", result.ContentType);
        Assert.Equal(90, result.Data.Length);
    }

    [Fact]
    public async Task DeleteUserPhoto_RemovesPhotoAndClearsUserFlags()
    {
        var user = new User { Id = Guid.NewGuid(), HasPhoto = true, PhotoUpdatedAt = DateTime.UtcNow };
        var mockUserManager = CreateMockUserManager();
        mockUserManager.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        mockUserManager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        var existingPhoto = UserPhoto.Create(user.Id, CreateValidWebpBytes(), "image/webp", 64);
        var mockPhotoRepo = new Mock<IUserPhotoRepository>();
        mockPhotoRepo.Setup(r => r.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPhoto);

        var mockUnitOfWork = new Mock<IUnitOfWork>();

        var handler = new DeleteUserPhotoCommandHandler(mockUserManager.Object, mockPhotoRepo.Object, mockUnitOfWork.Object);
        await handler.HandleAsync(new DeleteUserPhotoCommand(user.Id));

        Assert.False(user.HasPhoto);
        Assert.Null(user.PhotoUpdatedAt);
        mockPhotoRepo.Verify(r => r.DeleteAsync(existingPhoto, It.IsAny<CancellationToken>()), Times.Once);
        mockUnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

