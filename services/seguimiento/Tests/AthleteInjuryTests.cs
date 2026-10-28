using Domain.Entities;
using Domain.Exceptions;

namespace Seguimiento.Tests;

// Reglas de negocio de KAN-419: toda lesión nace en Active, con descripción obligatoria
// y una fecha de retorno estimada posterior a la de la lesión. La zona debe pertenecer
// a las 19 zonas del check-in (revisión de Enzo).
// El rechazo es del dominio (DomainException → 400 con mensaje en español).
public class AthleteInjuryTests
{
    private static readonly Guid AthleteId = Guid.NewGuid();
    private static readonly DateTime InjuryDate = new(2026, 10, 20, 0, 0, 0, DateTimeKind.Unspecified);

    private static AthleteInjury Crear(DateTime? estimatedReturnDate, string zone = "isquiotibiales", string description = "Gradación 3 tras sprint") =>
        AthleteInjury.Create(AthleteId, zone, description, InjuryDate, estimatedReturnDate);

    [Fact]
    public void Create_WithValidData_StartsInActiveWithEstimatedReturnDate()
    {
        var injury = Crear(new DateTime(2026, 11, 5));

        Assert.Equal(InjuryStatus.Active, injury.Status);
        Assert.Equal(AthleteId, injury.AthleteId);
        Assert.Equal(InjuryDate, injury.InjuryDate);
        Assert.Equal(new DateTime(2026, 11, 5), injury.EstimatedReturnDate);
        Assert.Null(injury.ClearedAt);
    }

    [Fact]
    public void Create_WithValidData_TrimsTextAndNormalizesDatesToDay()
    {
        var injury = AthleteInjury.Create(
            AthleteId,
            "  isquiotibiales  ",
            "  Molestia al correr  ",
            InjuryDate.AddHours(18),
            InjuryDate.AddDays(10).AddHours(23));

        Assert.Equal("isquiotibiales", injury.Zone);
        Assert.Equal("Molestia al correr", injury.Description);
        Assert.Equal(InjuryDate, injury.InjuryDate);
        Assert.Equal(InjuryDate.AddDays(10), injury.EstimatedReturnDate);
    }

    [Fact]
    public void Create_WithoutEstimatedReturnDate_ThrowsDomainExceptionWithSpanishMessage()
    {
        var exception = Assert.Throws<DomainException>(() => Crear(estimatedReturnDate: null));

        Assert.Equal("La fecha de retorno estimada es obligatoria.", exception.Message);
    }

    [Fact]
    public void Create_WithEstimatedReturnDateBeforeInjuryDate_ThrowsDomainExceptionWithSpanishMessage()
    {
        var exception = Assert.Throws<DomainException>(
            () => Crear(InjuryDate.AddDays(-1)));

        Assert.Equal("La fecha de retorno estimada debe ser posterior a la fecha de la lesión.", exception.Message);
    }

    [Fact]
    public void Create_WithEstimatedReturnDateSameAsInjuryDate_ThrowsDomainExceptionWithSpanishMessage()
    {
        var exception = Assert.Throws<DomainException>(() => Crear(InjuryDate));

        Assert.Equal("La fecha de retorno estimada debe ser posterior a la fecha de la lesión.", exception.Message);
    }

    [Fact]
    public void Create_WithEmptyZone_ThrowsDomainExceptionWithSpanishMessage()
    {
        var exception = Assert.Throws<DomainException>(
            () => Crear(InjuryDate.AddDays(15), zone: "   "));

        Assert.Equal("La zona de la lesión es obligatoria.", exception.Message);
    }

    [Fact]
    public void Create_WithEmptyDescription_ThrowsDomainExceptionWithSpanishMessage()
    {
        var exception = Assert.Throws<DomainException>(
            () => Crear(InjuryDate.AddDays(15), description: string.Empty));

        Assert.Equal("La descripción de la lesión es obligatoria.", exception.Message);
    }

    [Fact]
    public void Create_WithEmptyAthleteId_ThrowsDomainExceptionWithSpanishMessage()
    {
        var exception = Assert.Throws<DomainException>(
            () => AthleteInjury.Create(Guid.Empty, "isquiotibiales", "Dolor", InjuryDate, InjuryDate.AddDays(15)));

        Assert.Equal("El identificador del atleta es inválido.", exception.Message);
    }

    // ---------- Vocabulario de zonas del check-in (revisión de Enzo) ----------

    // Las 19 zonas del check-in (KAN-385): el mismo vocabulario que ofrece el mapa de
    // dolor del atleta. Cada una debe poder registrarse como lesión.
    [Theory]
    [InlineData("pecho")]
    [InlineData("hombros")]
    [InlineData("biceps")]
    [InlineData("abdominales")]
    [InlineData("cuadriceps")]
    [InlineData("espalda_alta")]
    [InlineData("espalda_media")]
    [InlineData("espalda_baja")]
    [InlineData("triceps")]
    [InlineData("gluteos")]
    [InlineData("isquiotibiales")]
    [InlineData("gemelos")]
    [InlineData("trapecios")]
    [InlineData("dorsales")]
    [InlineData("oblicuos")]
    [InlineData("antebrazos")]
    [InlineData("manos")]
    [InlineData("pies")]
    [InlineData("cuello_trapecios")]
    public void Create_WithAnyCheckInZone_IsAccepted(string zone)
    {
        var injury = Crear(InjuryDate.AddDays(15), zone: zone);

        Assert.Equal(zone, injury.Zone);
    }

    // La zona puede viajar con espacios y en mayúsculas (el front es texto libre):
    // el dominio normaliza y compara sin distinguir mayúsculas.
    [Theory]
    [InlineData("  cuadriceps  ")]
    [InlineData("ISQUIOTIBIALES")]
    public void Create_WithZoneVariants_IsNormalizedAndAccepted(string zone)
    {
        var injury = Crear(InjuryDate.AddDays(15), zone: zone);

        Assert.Equal(zone.Trim().ToLowerInvariant(), injury.Zone.ToLowerInvariant());
    }

    [Fact]
    public void Create_WithUnknownZone_ThrowsDomainExceptionWithSpanishMessage()
    {
        var exception = Assert.Throws<DomainException>(
            () => Crear(InjuryDate.AddDays(15), zone: "femoral"));

        Assert.Equal(
            "La zona de la lesión no es válida: debe ser una de las 19 zonas del check-in (ej.: isquiotibiales).",
            exception.Message);
    }

    // ---------- Fila inicial del historial (revisión de Enzo) ----------

    [Fact]
    public void InitialStatusChange_WithValidPf_ReturnsNullToActiveRowTiedToTheInjury()
    {
        var injury = Crear(InjuryDate.AddDays(15));
        var changedAt = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        var pfId = Guid.NewGuid();

        var row = injury.InitialStatusChange(pfId, changedAt);

        Assert.Equal(injury.Id, row.InjuryId);
        Assert.Null(row.FromStatus);
        Assert.Equal(InjuryStatus.Active, row.ToStatus);
        Assert.Equal(pfId, row.ChangedBy);
        Assert.Equal(changedAt, row.ChangedAt);
        Assert.False(string.IsNullOrWhiteSpace(row.Note));
    }

    [Fact]
    public void InitialStatusChange_WithEmptyPf_ThrowsDomainExceptionWithSpanishMessage()
    {
        var injury = Crear(InjuryDate.AddDays(15));

        var exception = Assert.Throws<DomainException>(
            () => injury.InitialStatusChange(Guid.Empty, DateTime.UtcNow));

        Assert.Equal("El usuario que realiza el cambio es inválido.", exception.Message);
    }
}
