using Domain.Entities;
using Domain.Exceptions;

namespace Estructura.Tests;

public sealed class CompetitiveEventTests
{
    private static readonly Guid CategoryId = Guid.NewGuid();

    [Fact]
    public void Given_ValidParameters_When_CreatingEvent_Then_InstantiatesCorrectly()
    {
        // arrange
        var date = new DateOnly(2026, 4, 15);
        var time = new TimeOnly(16, 30);

        // act
        var ev = CompetitiveEvent.Create(
            CategoryId,
            date,
            "Rival F.C.",
            CompetitiveEventVenue.Home,
            CompetitiveEventType.Official,
            time,
            "Partido fecha 1 del torneo");

        // assert
        Assert.NotNull(ev);
        Assert.Equal(CategoryId, ev.CategoryId);
        Assert.Equal(date, ev.Date);
        Assert.Equal(time, ev.StartTime);
        Assert.Equal("Rival F.C.", ev.Opponent);
        Assert.Equal(CompetitiveEventVenue.Home, ev.Venue);
        Assert.Equal(CompetitiveEventType.Official, ev.Type);
        Assert.Equal("Partido fecha 1 del torneo", ev.Notes);
    }

    [Fact]
    public void Given_NullStartTimeAndNotes_When_CreatingEvent_Then_AllowsOptionalValues()
    {
        // act
        var ev = CompetitiveEvent.Create(
            CategoryId,
            new DateOnly(2026, 4, 20),
            "Club Deportivo",
            CompetitiveEventVenue.Away,
            CompetitiveEventType.Friendly);

        // assert
        Assert.Null(ev.StartTime);
        Assert.Null(ev.Notes);
    }

    [Theory]
    [InlineData(CompetitiveEventVenue.Home)]
    [InlineData(CompetitiveEventVenue.Away)]
    [InlineData(CompetitiveEventVenue.Neutral)]
    public void Given_ValidVenues_When_CreatingEvent_Then_AcceptsAllAllowedValues(CompetitiveEventVenue venue)
    {
        // act
        var ev = CompetitiveEvent.Create(
            CategoryId,
            new DateOnly(2026, 5, 10),
            "Club Atletico",
            venue,
            CompetitiveEventType.Tournament);

        // assert
        Assert.Equal(venue, ev.Venue);
    }

    [Fact]
    public void Given_InvalidVenue_When_CreatingEvent_Then_ThrowsDomainException()
    {
        // arrange
        var invalidVenue = (CompetitiveEventVenue)999;

        // act & assert
        var ex = Assert.Throws<DomainException>(() =>
            CompetitiveEvent.Create(
                CategoryId,
                new DateOnly(2026, 5, 10),
                "Club Atletico",
                invalidVenue,
                CompetitiveEventType.Official));

        Assert.Contains("La condición de localía es inválida", ex.Message);
    }

    [Theory]
    [InlineData(CompetitiveEventType.Official)]
    [InlineData(CompetitiveEventType.Friendly)]
    [InlineData(CompetitiveEventType.Tournament)]
    public void Given_ValidTypes_When_CreatingEvent_Then_AcceptsAllAllowedValues(CompetitiveEventType type)
    {
        // act
        var ev = CompetitiveEvent.Create(
            CategoryId,
            new DateOnly(2026, 5, 10),
            "Club Atletico",
            CompetitiveEventVenue.Home,
            type);

        // assert
        Assert.Equal(type, ev.Type);
    }

    [Fact]
    public void Given_InvalidType_When_CreatingEvent_Then_ThrowsDomainException()
    {
        // arrange
        var invalidType = (CompetitiveEventType)999;

        // act & assert
        var ex = Assert.Throws<DomainException>(() =>
            CompetitiveEvent.Create(
                CategoryId,
                new DateOnly(2026, 5, 10),
                "Club Atletico",
                CompetitiveEventVenue.Home,
                invalidType));

        Assert.Contains("El tipo de evento es inválido", ex.Message);
    }

    [Fact]
    public void Given_EmptyCategoryId_When_CreatingEvent_Then_ThrowsDomainException()
    {
        // act & assert
        var ex = Assert.Throws<DomainException>(() =>
            CompetitiveEvent.Create(
                Guid.Empty,
                new DateOnly(2026, 5, 10),
                "Club Atletico",
                CompetitiveEventVenue.Home,
                CompetitiveEventType.Official));

        Assert.Equal("El identificador de la categoría es inválido.", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Given_InvalidOpponent_When_CreatingEvent_Then_ThrowsDomainException(string? invalidOpponent)
    {
        // act & assert
        var ex = Assert.Throws<DomainException>(() =>
            CompetitiveEvent.Create(
                CategoryId,
                new DateOnly(2026, 5, 10),
                invalidOpponent,
                CompetitiveEventVenue.Home,
                CompetitiveEventType.Official));

        Assert.Contains("El nombre del rival es obligatorio", ex.Message);
    }

    [Fact]
    public void Given_OpponentExceedingMaxLength_When_CreatingEvent_Then_ThrowsDomainException()
    {
        // arrange
        var longOpponent = new string('A', CompetitiveEvent.MaxOpponentLength + 1);

        // act & assert
        var ex = Assert.Throws<DomainException>(() =>
            CompetitiveEvent.Create(
                CategoryId,
                new DateOnly(2026, 5, 10),
                longOpponent,
                CompetitiveEventVenue.Home,
                CompetitiveEventType.Official));

        Assert.Contains("El nombre del rival es obligatorio", ex.Message);
    }

    [Fact]
    public void Given_NotesExceedingMaxLength_When_CreatingEvent_Then_ThrowsDomainException()
    {
        // arrange
        var longNotes = new string('N', CompetitiveEvent.MaxNotesLength + 1);

        // act & assert
        var ex = Assert.Throws<DomainException>(() =>
            CompetitiveEvent.Create(
                CategoryId,
                new DateOnly(2026, 5, 10),
                "Club Atletico",
                CompetitiveEventVenue.Home,
                CompetitiveEventType.Official,
                notes: longNotes));

        Assert.Contains("Las notas del evento no pueden superar", ex.Message);
    }

    [Fact]
    public void Given_ExistingEvent_When_Rescheduled_Then_DateAndStartTimeAreUpdated()
    {
        // arrange
        var ev = CompetitiveEvent.Create(
            CategoryId,
            new DateOnly(2026, 5, 10),
            "Club Atletico",
            CompetitiveEventVenue.Home,
            CompetitiveEventType.Official);

        var newDate = new DateOnly(2026, 5, 12);
        var newTime = new TimeOnly(18, 0);

        // act
        ev.Reschedule(newDate, newTime);

        // assert
        Assert.Equal(newDate, ev.Date);
        Assert.Equal(newTime, ev.StartTime);
    }
}
