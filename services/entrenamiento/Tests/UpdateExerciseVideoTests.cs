using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.UseCases.Exercise.UpdateVideoUrl;
using Domain.Entities;
using Domain.Exceptions;
using Moq;
using Xunit;

namespace Tests;

public class UpdateExerciseVideoTests
{
    private readonly Mock<IExerciseRepository> _exerciseRepositoryMock = new();
    private readonly Mock<IExerciseVideoRepository> _exerciseVideoRepositoryMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();

    private UpdateExerciseVideoCommandHandler CreateHandler() =>
        new(_exerciseRepositoryMock.Object, _exerciseVideoRepositoryMock.Object, _unitOfWorkMock.Object);

    [Fact]
    public async Task Given_ExistingExercise_When_PFAddsVideo_Then_CreatesNormalizedExerciseVideoAndSaves()
    {
        // Arrange
        var exerciseId = Guid.NewGuid();
        var exercise = Exercise.Create(
            name: "Sentadilla Trasera",
            muscleGroup: "Piernas",
            videoUrl: null
        );

        _exerciseRepositoryMock
            .Setup(r => r.GetByIdAsync(exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(exercise);

        _exerciseVideoRepositoryMock
            .Setup(r => r.GetByExerciseIdAsync(exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExerciseVideo?)null);

        _unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = CreateHandler();
        var command = new UpdateExerciseVideoCommand(exerciseId, "https://youtu.be/aclHkVaku9U");

        // Act
        var result = await handler.Handle(command);

        // Assert
        Assert.Equal("https://www.youtube.com/watch?v=aclHkVaku9U", result.VideoUrl);
        Assert.Equal("Pf", result.VideoSource);
        _exerciseVideoRepositoryMock.Verify(r => r.AddAsync(It.Is<ExerciseVideo>(v =>
            v.ExerciseId == exerciseId && v.VideoUrl == "https://www.youtube.com/watch?v=aclHkVaku9U"), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_ExistingExerciseAndPFVideo_When_PFUpdatesVideo_Then_UpdatesExistingVideo()
    {
        // Arrange
        var exerciseId = Guid.NewGuid();
        var exercise = Exercise.Create("Sentadilla Trasera", "Piernas", null);
        var existingVideo = ExerciseVideo.Create(exerciseId, "https://www.youtube.com/watch?v=oldVideo111");

        _exerciseRepositoryMock
            .Setup(r => r.GetByIdAsync(exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(exercise);

        _exerciseVideoRepositoryMock
            .Setup(r => r.GetByExerciseIdAsync(exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingVideo);

        _unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = CreateHandler();
        var command = new UpdateExerciseVideoCommand(exerciseId, "https://www.youtube.com/watch?v=newVideo222");

        // Act
        var result = await handler.Handle(command);

        // Assert
        Assert.Equal("https://www.youtube.com/watch?v=newVideo222", result.VideoUrl);
        Assert.Equal("Pf", result.VideoSource);
        Assert.Equal("https://www.youtube.com/watch?v=newVideo222", existingVideo.VideoUrl);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_ExistingPFVideo_When_VideoUrlEmpty_Then_RemovesPFVideoAndFallsBackToCatalog()
    {
        // Arrange
        var exerciseId = Guid.NewGuid();
        var exercise = Exercise.Create(
            name: "Sentadilla Trasera",
            muscleGroup: "Piernas",
            videoUrl: "https://www.youtube.com/watch?v=catalogVid1"
        );
        var existingVideo = ExerciseVideo.Create(exerciseId, "https://www.youtube.com/watch?v=customVid11");

        _exerciseRepositoryMock
            .Setup(r => r.GetByIdAsync(exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(exercise);

        _exerciseVideoRepositoryMock
            .Setup(r => r.GetByExerciseIdAsync(exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingVideo);

        var handler = CreateHandler();
        var command = new UpdateExerciseVideoCommand(exerciseId, "   ");

        // Act
        var result = await handler.Handle(command);

        // Assert
        Assert.Equal("https://www.youtube.com/watch?v=catalogVid1", result.VideoUrl);
        Assert.Equal("Catalog", result.VideoSource);
        _exerciseVideoRepositoryMock.Verify(r => r.Remove(existingVideo), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_InvalidUrl_When_UpdateVideoUrl_Then_ThrowsDomainException()
    {
        // Arrange
        var exerciseId = Guid.NewGuid();
        var exercise = Exercise.Create("Sentadilla Trasera", "Piernas", null);

        _exerciseRepositoryMock
            .Setup(r => r.GetByIdAsync(exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(exercise);

        var handler = CreateHandler();
        var command = new UpdateExerciseVideoCommand(exerciseId, "https://vimeo.com/12345678");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.Handle(command));
        Assert.Equal("El link tiene que ser un video de YouTube.", ex.Message);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_NonExistentExercise_When_UpdateVideoUrl_Then_ThrowsNotFoundException()
    {
        // Arrange
        var exerciseId = Guid.NewGuid();
        _exerciseRepositoryMock
            .Setup(r => r.GetByIdAsync(exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Exercise?)null);

        var handler = CreateHandler();
        var command = new UpdateExerciseVideoCommand(exerciseId, "https://www.youtube.com/watch?v=aclHkVaku9U");

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command));
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_CoachCreatedExercise_When_PFUpdatesVideo_Then_WorksAndReturnsPf()
    {
        // KAN-391: Ejercicio propio del PF también funciona
        var exerciseId = Guid.NewGuid();
        var exercise = Exercise.Create(
            name: "Ejercicio Especial PF",
            muscleGroup: "Espalda",
            origin: Origin.Coach
        );

        _exerciseRepositoryMock
            .Setup(r => r.GetByIdAsync(exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(exercise);

        _exerciseVideoRepositoryMock
            .Setup(r => r.GetByExerciseIdAsync(exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExerciseVideo?)null);

        _unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = CreateHandler();
        var command = new UpdateExerciseVideoCommand(exerciseId, "https://www.youtube.com/watch?v=customPfEx1");

        var result = await handler.Handle(command);

        Assert.Equal("https://www.youtube.com/watch?v=customPfEx1", result.VideoUrl);
        Assert.Equal("Pf", result.VideoSource);
    }
}
