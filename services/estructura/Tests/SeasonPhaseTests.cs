using Domain.Entities;
using Domain.Exceptions;

namespace Estructura.Tests;

public sealed class SeasonPhaseTests
{
    private static readonly Guid CategoryId = Guid.NewGuid();

    [Fact]
    public void Given_ValidDates_When_EndDateIsAfterStartDate_Then_CreatesSeasonPhaseSuccessfully()
    {
        // arrange
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);

        // act
        var phase = SeasonPhase.Create(
            CategoryId,
            "Pretemporada 2026",
            SeasonPhaseType.Preseason,
            start,
            end);

        // assert
        Assert.NotNull(phase);
        Assert.Equal(CategoryId, phase.CategoryId);
        Assert.Equal("Pretemporada 2026", phase.Name);
        Assert.Equal(SeasonPhaseType.Preseason, phase.Type);
        Assert.Equal(start, phase.StartDate);
        Assert.Equal(end, phase.EndDate);
    }

    [Fact]
    public void Given_ValidDates_When_EndDateEqualsStartDate_Then_CreatesSeasonPhaseSuccessfully()
    {
        // arrange
        var singleDate = new DateOnly(2026, 6, 1);

        // act
        var phase = SeasonPhase.Create(
            CategoryId,
            "Fase Transición Un Día",
            SeasonPhaseType.Transition,
            singleDate,
            singleDate);

        // assert
        Assert.NotNull(phase);
        Assert.Equal(singleDate, phase.StartDate);
        Assert.Equal(singleDate, phase.EndDate);
    }

    [Fact]
    public void Given_InvalidDates_When_EndDatePrecedesStartDate_Then_ThrowsDomainExceptionWithNeutralSpanishMessage()
    {
        // arrange
        var start = new DateOnly(2026, 5, 1);
        var end = new DateOnly(2026, 4, 30);

        // act & assert
        var ex = Assert.Throws<DomainException>(() =>
            SeasonPhase.Create(
                CategoryId,
                "Fase Inválida",
                SeasonPhaseType.Competitive,
                start,
                end));

        Assert.Equal("La fecha de finalización no puede ser anterior a la fecha de inicio.", ex.Message);
    }

    [Fact]
    public void Given_EmptyCategoryId_When_CreatingPhase_Then_ThrowsDomainException()
    {
        // arrange
        var emptyCat = Guid.Empty;

        // act & assert
        var ex = Assert.Throws<DomainException>(() =>
            SeasonPhase.Create(
                emptyCat,
                "Pretemporada",
                SeasonPhaseType.Preseason,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 2, 1)));

        Assert.Equal("El identificador de la categoría es inválido.", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Given_InvalidName_When_CreatingPhase_Then_ThrowsDomainException(string? invalidName)
    {
        // act & assert
        var ex = Assert.Throws<DomainException>(() =>
            SeasonPhase.Create(
                CategoryId,
                invalidName,
                SeasonPhaseType.Preseason,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 2, 1)));

        Assert.Contains("El nombre de la fase es obligatorio", ex.Message);
    }

    [Fact]
    public void Given_NameExceedingMaxLength_When_CreatingPhase_Then_ThrowsDomainException()
    {
        // arrange
        var longName = new string('A', SeasonPhase.MaxNameLength + 1);

        // act & assert
        var ex = Assert.Throws<DomainException>(() =>
            SeasonPhase.Create(
                CategoryId,
                longName,
                SeasonPhaseType.Preseason,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 2, 1)));

        Assert.Contains("El nombre de la fase es obligatorio", ex.Message);
    }

    [Fact]
    public void Given_InvalidTypeEnum_When_CreatingPhase_Then_ThrowsDomainException()
    {
        // arrange
        var invalidType = (SeasonPhaseType)999;

        // act & assert
        var ex = Assert.Throws<DomainException>(() =>
            SeasonPhase.Create(
                CategoryId,
                "Fase Tipo Raro",
                invalidType,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 2, 1)));

        Assert.Contains("El tipo de fase es inválido", ex.Message);
    }

    [Fact]
    public void Given_ExistingPhase_When_UpdatingDateRangeToInvalid_Then_ThrowsDomainException()
    {
        // arrange
        var phase = SeasonPhase.Create(
            CategoryId,
            "Pretemporada",
            SeasonPhaseType.Preseason,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 2, 1));

        // act & assert
        var ex = Assert.Throws<DomainException>(() =>
            phase.UpdateDateRange(new DateOnly(2026, 3, 1), new DateOnly(2026, 2, 28)));

        Assert.Equal("La fecha de finalización no puede ser anterior a la fecha de inicio.", ex.Message);
    }

    [Fact]
    public void Given_TwoPhases_When_DatesOverlap_Then_OverlapsWithReturnsTrue()
    {
        // arrange
        var phase1 = SeasonPhase.Create(
            CategoryId,
            "Fase 1",
            SeasonPhaseType.Preseason,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 31));

        var phase2 = SeasonPhase.Create(
            CategoryId,
            "Fase 2",
            SeasonPhaseType.Competitive,
            new DateOnly(2026, 3, 15),
            new DateOnly(2026, 5, 1));

        // act & assert
        Assert.True(phase1.OverlapsWith(phase2));
        Assert.True(phase2.OverlapsWith(phase1));
        Assert.True(phase1.OverlapsWith(new DateOnly(2026, 3, 15), new DateOnly(2026, 5, 1)));
    }

    [Fact]
    public void Given_TwoAdjacentPhases_When_NoOverlap_Then_OverlapsWithReturnsFalse()
    {
        // arrange
        var phase1 = SeasonPhase.Create(
            CategoryId,
            "Pretemporada",
            SeasonPhaseType.Preseason,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 31));

        var phase2 = SeasonPhase.Create(
            CategoryId,
            "Competitiva",
            SeasonPhaseType.Competitive,
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 6, 30));

        // act & assert
        Assert.False(phase1.OverlapsWith(phase2));
        Assert.False(phase2.OverlapsWith(phase1));
    }
}
