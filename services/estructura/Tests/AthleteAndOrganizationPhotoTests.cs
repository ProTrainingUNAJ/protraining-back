using Application.Common;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.UseCases.Athletes.DeleteAthletePhoto;
using Application.UseCases.Athletes.GetAthletePhoto;
using Application.UseCases.Athletes.UploadAthletePhoto;
using Application.UseCases.Organizations.DeleteOrganizationPhoto;
using Application.UseCases.Organizations.GetOrganizationPhoto;
using Application.UseCases.Organizations.UploadOrganizationPhoto;
using Domain.Entities;
using Domain.Exceptions;
using Moq;
using Xunit;

namespace Estructura.Tests;

public class AthleteAndOrganizationPhotoTests
{
    private static byte[] CreateValidWebpBytes(int length = 64)
    {
        var bytes = new byte[Math.Max(length, 12)];
        // 'RIFF'
        bytes[0] = (byte)'R';
        bytes[1] = (byte)'I';
        bytes[2] = (byte)'F';
        bytes[3] = (byte)'F';
        // 'WEBP'
        bytes[8] = (byte)'W';
        bytes[9] = (byte)'E';
        bytes[10] = (byte)'B';
        bytes[11] = (byte)'P';
        return bytes;
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
    public void WebpImageValidator_EmptyOrNull_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() => WebpImageValidator.Validate(null));
        Assert.Throws<DomainException>(() => WebpImageValidator.Validate([]));
    }

    [Fact]
    public void WebpImageValidator_InvalidMagicBytes_ThrowsDomainException()
    {
        var invalidData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        var ex = Assert.Throws<DomainException>(() => WebpImageValidator.Validate(invalidData));
        Assert.Contains("WebP", ex.Message);
    }

    [Fact]
    public async Task MinorAthlete_WithoutProfilePhotoConsent_ThrowsOnUpload()
    {
        var athlete = Athlete.CreateMinor(
            "Juan", "Perez", "juan@test.com", "12345678",
            "Tutor", "tutor@test.com", "11223344",
            allowsPhysicalTraining: true,
            allowsHabitsRecommendations: false,
            emergencyContact: null,
            allowsProfilePhoto: false);

        var athleteRepo = new Mock<IAthleteRepository>();
        athleteRepo.Setup(r => r.GetByIdAsync(athlete.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(athlete);

        var photoRepo = new Mock<IAthletePhotoRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new UploadAthletePhotoCommandHandler(athleteRepo.Object, photoRepo.Object, unitOfWork.Object);
        var command = new UploadAthletePhotoCommand(athlete.Id, CreateValidWebpBytes(), "image/webp", 64);

        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.HandleAsync(command));
        Assert.NotNull(ex);
        Assert.Contains("tutor", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MinorAthlete_WithProfilePhotoConsent_UploadsSuccessfully()
    {
        var athlete = Athlete.CreateMinor(
            "Juan", "Perez", "juan@test.com", "12345678",
            "Tutor", "tutor@test.com", "11223344",
            allowsPhysicalTraining: true,
            allowsHabitsRecommendations: false,
            emergencyContact: null,
            allowsProfilePhoto: true);

        var athleteRepo = new Mock<IAthleteRepository>();
        athleteRepo.Setup(r => r.GetByIdAsync(athlete.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(athlete);

        var photoRepo = new Mock<IAthletePhotoRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new UploadAthletePhotoCommandHandler(athleteRepo.Object, photoRepo.Object, unitOfWork.Object);
        var command = new UploadAthletePhotoCommand(athlete.Id, CreateValidWebpBytes(), "image/webp", 64);

        await handler.HandleAsync(command);

        Assert.True(athlete.HasPhoto);
        Assert.NotNull(athlete.PhotoUpdatedAt);
        photoRepo.Verify(r => r.AddAsync(It.IsAny<AthletePhoto>(), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAthletePhoto_WhenNotFound_ThrowsPhotoNotFoundException()
    {
        var photoRepo = new Mock<IAthletePhotoRepository>();
        photoRepo.Setup(r => r.GetByAthleteIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthletePhoto?)null);

        var handler = new GetAthletePhotoQueryHandler(photoRepo.Object);
        await Assert.ThrowsAsync<PhotoNotFoundException>(() => handler.HandleAsync(new GetAthletePhotoQuery(Guid.NewGuid())));
    }

    [Fact]
    public async Task DeleteAthletePhoto_ClearsPhotoFlagsAndDeletesRecord()
    {
        var athlete = Athlete.CreateAdult("Carlos", "Gomez", "carlos@test.com", "11223344");
        athlete.SetPhotoUpdated(DateTime.UtcNow);

        var athleteRepo = new Mock<IAthleteRepository>();
        athleteRepo.Setup(r => r.GetByIdAsync(athlete.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(athlete);

        var existingPhoto = AthletePhoto.Create(athlete.Id, CreateValidWebpBytes(), "image/webp", 64);
        var photoRepo = new Mock<IAthletePhotoRepository>();
        photoRepo.Setup(r => r.GetByAthleteIdAsync(athlete.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPhoto);

        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new DeleteAthletePhotoCommandHandler(athleteRepo.Object, photoRepo.Object, unitOfWork.Object);
        await handler.HandleAsync(new DeleteAthletePhotoCommand(athlete.Id));

        Assert.False(athlete.HasPhoto);
        Assert.Null(athlete.PhotoUpdatedAt);
        photoRepo.Verify(r => r.DeleteAsync(existingPhoto, It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrganizationPhoto_Upload_Get_Delete_Lifecycle()
    {
        var organization = Organization.Create("Club Atletico Test", "Descripcion");
        var orgRepo = new Mock<IOrganizationRepository>();
        orgRepo.Setup(r => r.GetByIdAsync(organization.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        var photoRepo = new Mock<IOrganizationPhotoRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();

        // 1. Upload
        var uploadHandler = new UploadOrganizationPhotoCommandHandler(orgRepo.Object, photoRepo.Object, unitOfWork.Object);
        var bytes = CreateValidWebpBytes(128);
        await uploadHandler.HandleAsync(new UploadOrganizationPhotoCommand(organization.Id, bytes, "image/webp", 128));

        Assert.True(organization.HasPhoto);
        Assert.NotNull(organization.PhotoUpdatedAt);
        photoRepo.Verify(r => r.AddAsync(It.IsAny<OrganizationPhoto>(), It.IsAny<CancellationToken>()), Times.Once);

        // 2. Get
        var savedPhoto = OrganizationPhoto.Create(organization.Id, bytes, "image/webp", 128);
        photoRepo.Setup(r => r.GetByOrganizationIdAsync(organization.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(savedPhoto);

        var getHandler = new GetOrganizationPhotoQueryHandler(photoRepo.Object);
        var result = await getHandler.HandleAsync(new GetOrganizationPhotoQuery(organization.Id));
        Assert.Equal("image/webp", result.ContentType);
        Assert.Equal(128, result.Data.Length);

        // 3. Delete
        var deleteHandler = new DeleteOrganizationPhotoCommandHandler(orgRepo.Object, photoRepo.Object, unitOfWork.Object);
        await deleteHandler.HandleAsync(new DeleteOrganizationPhotoCommand(organization.Id));

        Assert.False(organization.HasPhoto);
        Assert.Null(organization.PhotoUpdatedAt);
        photoRepo.Verify(r => r.DeleteAsync(savedPhoto, It.IsAny<CancellationToken>()), Times.Once);
    }
}
