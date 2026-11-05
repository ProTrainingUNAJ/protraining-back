using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.UseCases.Exercise.UpdateVideoUrl;
using Domain.Entities;
using Moq;
using Xunit;

namespace Tests;

public class UpdateExerciseVideoTests
{
    private readonly Mock<IExerciseRepository> _exerciseRepositoryMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();

    [Fact]
    public async Task Given_ExistingExercise_When_UpdateVideoUrl_Then_UpdatesVideoAndSaves()
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

        _unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = new UpdateExerciseVideoCommandHandler(
            _exerciseRepositoryMock.Object,
            _unitOfWorkMock.Object
        );

        var command = new UpdateExerciseVideoCommand
        {
            ExerciseId = exerciseId,
            VideoUrl = "https://www.youtube.com/watch?v=aclHkVaku9U",
        };

        // Act
        var result = await handler.Handle(command);

        // Assert
        Assert.Equal("https://www.youtube.com/watch?v=aclHkVaku9U", result.VideoUrl);
        Assert.Equal("https://www.youtube.com/watch?v=aclHkVaku9U", exercise.VideoUrl);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_ExistingExercise_When_VideoUrlEmpty_Then_SetsVideoUrlToNull()
    {
        // Arrange
        var exerciseId = Guid.NewGuid();
        var exercise = Exercise.Create(
            name: "Sentadilla Trasera",
            muscleGroup: "Piernas",
            videoUrl: "https://www.youtube.com/watch?v=aclHkVaku9U"
        );

        _exerciseRepositoryMock
            .Setup(r => r.GetByIdAsync(exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(exercise);

        var handler = new UpdateExerciseVideoCommandHandler(
            _exerciseRepositoryMock.Object,
            _unitOfWorkMock.Object
        );

        var command = new UpdateExerciseVideoCommand
        {
            ExerciseId = exerciseId,
            VideoUrl = "   ", // Espacios en blanco
        };

        // Act
        var result = await handler.Handle(command);

        // Assert
        Assert.Null(result.VideoUrl);
        Assert.Null(exercise.VideoUrl);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_NonExistentExercise_When_UpdateVideoUrl_Then_ThrowsNotFoundException()
    {
        // Arrange
        var exerciseId = Guid.NewGuid();
        _exerciseRepositoryMock
            .Setup(r => r.GetByIdAsync(exerciseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Exercise?)null);

        var handler = new UpdateExerciseVideoCommandHandler(
            _exerciseRepositoryMock.Object,
            _unitOfWorkMock.Object
        );

        var command = new UpdateExerciseVideoCommand
        {
            ExerciseId = exerciseId,
            VideoUrl = "https://www.youtube.com/watch?v=aclHkVaku9U",
        };

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command));
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}

