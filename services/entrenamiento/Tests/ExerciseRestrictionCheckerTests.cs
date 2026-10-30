using Domain.Entities;
using Domain.Services;
using Xunit;

namespace Tests;

public class ExerciseRestrictionCheckerTests
{
    private readonly ExerciseRestrictionChecker _checker = new();

    [Fact]
    public void Check_DadoAtletaConDolorDeHombrosNivel2_CuandoSeEvaluaPressMilitarTrasNuca_EntoncesLoMarcaContraindicadoYConSustitutosEnOrden()
    {
        // Arrange
        var pressMilitar = Exercise.Create("Press militar tras nuca", "Hombros");
        var sub1 = Exercise.Create("Press militar con mancuernas", "Hombros");
        var sub2 = Exercise.Create("Elevaciones laterales", "Hombros");

        var restriction = ExerciseRestriction.Create(
            zone: "hombros",
            exerciseId: pressMilitar.Id,
            reason: "Pinzamiento subacromial y fricción articular",
            minPainLevel: 1);

        // Agregamos sustitutos desordenados para verificar que se ordenen por Order
        restriction.AddSubstitute(sub2, order: 2);
        restriction.AddSubstitute(sub1, order: 1);

        var restrictions = new[] { restriction };
        var exerciseIds = new[] { pressMilitar.Id };

        // Act
        var results = _checker.Check(exerciseIds, "hombros:2", restrictions);

        // Assert
        Assert.Single(results);
        var result = results[0];
        Assert.Equal(pressMilitar.Id, result.ExerciseId);
        Assert.True(result.Restricted, "El ejercicio debe estar marcado contraindicado");
        Assert.Equal("hombros", result.Zone);
        Assert.Equal(2, result.PainLevel);
        Assert.Equal("Pinzamiento subacromial y fricción articular", result.Reason);
        Assert.NotEmpty(result.Substitutes);
        Assert.Equal(2, result.Substitutes.Count);
        Assert.Equal(sub1.Id, result.Substitutes[0].ExerciseId);
        Assert.Equal("Press militar con mancuernas", result.Substitutes[0].Name);
        Assert.Equal(sub2.Id, result.Substitutes[1].ExerciseId);
        Assert.Equal("Elevaciones laterales", result.Substitutes[1].Name);
    }

    [Fact]
    public void Check_NivelDeDolorMenorQueMinPainLevel_NoRestringido()
    {
        // Arrange
        var pressMilitar = Exercise.Create("Press militar tras nuca", "Hombros");
        var sub = Exercise.Create("Press militar con mancuernas", "Hombros");

        var restriction = ExerciseRestriction.Create("hombros", pressMilitar.Id, "Dolor articular moderado a severo", minPainLevel: 2);
        restriction.AddSubstitute(sub, 1);

        var restrictions = new[] { restriction };
        var exerciseIds = new[] { pressMilitar.Id };

        // Act: Nivel 1 menor que minPainLevel 2
        var results = _checker.Check(exerciseIds, "hombros:1", restrictions);

        // Assert
        Assert.Single(results);
        var result = results[0];
        Assert.False(result.Restricted);
        Assert.Null(result.Zone);
        Assert.Null(result.PainLevel);
        Assert.Null(result.Reason);
        Assert.Empty(result.Substitutes);
    }

    [Fact]
    public void Check_DosZonasConDolorYEjercicioRestringidoEnLasDos_ApareceUnaVezConZonaDeMayorNivel()
    {
        // Arrange
        var pressMilitar = Exercise.Create("Press militar tras nuca", "Hombros");
        var subHombros = Exercise.Create("Press militar con mancuernas", "Hombros");
        var subTrapecios = Exercise.Create("Press de pecho en máquina", "Pecho");

        var restHombros = ExerciseRestriction.Create("hombros", pressMilitar.Id, "Pinzamiento subacromial", minPainLevel: 1);
        restHombros.AddSubstitute(subHombros, 1);

        var restTrapecios = ExerciseRestriction.Create("trapecios", pressMilitar.Id, "Tensión cervical y trapecio", minPainLevel: 1);
        restTrapecios.AddSubstitute(subTrapecios, 1);

        var restrictions = new[] { restTrapecios, restHombros };
        var exerciseIds = new[] { pressMilitar.Id };

        // Act: trapecios nivel 1 y hombros nivel 3 -> debe priorizar hombros
        var results = _checker.Check(exerciseIds, "trapecios:1,hombros:3", restrictions);

        // Assert: aparece una sola vez con los datos de hombros
        Assert.Single(results);
        var result = results[0];
        Assert.Equal(pressMilitar.Id, result.ExerciseId);
        Assert.True(result.Restricted);
        Assert.Equal("hombros", result.Zone);
        Assert.Equal(3, result.PainLevel);
        Assert.Equal("Pinzamiento subacromial", result.Reason);
        Assert.Single(result.Substitutes);
        Assert.Equal(subHombros.Id, result.Substitutes[0].ExerciseId);
    }

    [Fact]
    public void Check_ZonaConDolorSinRestriccionesCargadas_NadaRestringido()
    {
        // Arrange
        var sentadilla = Exercise.Create("Sentadilla Trasera", "Piernas");
        var press = Exercise.Create("Press de banca plano", "Pecho");

        var restrictionHombros = ExerciseRestriction.Create("hombros", press.Id, "Molestia articular", minPainLevel: 1);
        var restrictions = new[] { restrictionHombros };

        // Act: dolor en zona sin restricciones ("gemelos:3")
        var results = _checker.Check(new[] { sentadilla.Id, press.Id }, "gemelos:3", restrictions);

        // Assert: ningún ejercicio se restringe
        Assert.Equal(2, results.Count);
        Assert.All(results, r =>
        {
            Assert.False(r.Restricted);
            Assert.Null(r.Zone);
            Assert.Null(r.PainLevel);
            Assert.Null(r.Reason);
            Assert.Empty(r.Substitutes);
        });
    }

    [Fact]
    public void Check_EjercicioQueNoEstaEnLaTabla_Permitido()
    {
        // Arrange
        var pressMilitar = Exercise.Create("Press militar tras nuca", "Hombros");
        var dominadas = Exercise.Create("Dominadas", "Espalda");

        var restriction = ExerciseRestriction.Create("hombros", pressMilitar.Id, "Pinzamiento subacromial", minPainLevel: 1);
        var restrictions = new[] { restriction };

        // Act: evaluamos dominadas (no está en la tabla de restricciones) ante dolor en hombros
        var results = _checker.Check(new[] { dominadas.Id }, "hombros:2", restrictions);

        // Assert
        Assert.Single(results);
        var result = results[0];
        Assert.Equal(dominadas.Id, result.ExerciseId);
        Assert.False(result.Restricted);
        Assert.Null(result.Zone);
        Assert.Null(result.PainLevel);
        Assert.Null(result.Reason);
        Assert.Empty(result.Substitutes);
    }

    [Theory]
    [InlineData("cuadriceps")]
    [InlineData("xyz:abc")]
    [InlineData("cuadriceps,xyz:abc")]
    [InlineData("cuadriceps:")]
    [InlineData(":2")]
    [InlineData("cuadriceps::2")]
    [InlineData("hombros:0")]
    [InlineData("hombros:-1")]
    public void ParsePainZones_MalFormado_SeIgnoraEsaParteSinExcepcion(string malformed)
    {
        // Act: no debe lanzar ninguna excepción y debe ignorar las partes malformadas
        var parsed = _checker.ParsePainZones(malformed);

        // Assert
        Assert.Empty(parsed);
    }

    [Fact]
    public void Check_PainZonesMalFormado_SeIgnoraSinExcepcionYPermiteValidos()
    {
        // Arrange
        var pressMilitar = Exercise.Create("Press militar tras nuca", "Hombros");
        var sub = Exercise.Create("Press militar con mancuernas", "Hombros");
        var restriction = ExerciseRestriction.Create("hombros", pressMilitar.Id, "Pinzamiento", minPainLevel: 1);
        restriction.AddSubstitute(sub, 1);

        var restrictions = new[] { restriction };

        // Act A: painZones totalmente malformado -> no lanza excepción y nada es restringido
        var resultsA = _checker.Check(new[] { pressMilitar.Id }, "cuadriceps,xyz:abc,invalido:", restrictions);
        Assert.Single(resultsA);
        Assert.False(resultsA[0].Restricted);

        // Act B: painZones con partes inválidas y una válida ("cuadriceps,xyz:abc,hombros:2")
        var resultsB = _checker.Check(new[] { pressMilitar.Id }, "cuadriceps,xyz:abc,hombros:2", restrictions);
        Assert.Single(resultsB);
        Assert.True(resultsB[0].Restricted);
        Assert.Equal("hombros", resultsB[0].Zone);
        Assert.Equal(2, resultsB[0].PainLevel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Check_PainZonesVacioONulo_NadaRestringido(string? painZones)
    {
        // Arrange
        var pressMilitar = Exercise.Create("Press militar tras nuca", "Hombros");
        var sentadilla = Exercise.Create("Sentadilla Trasera", "Piernas");

        var restHombros = ExerciseRestriction.Create("hombros", pressMilitar.Id, "Pinzamiento", minPainLevel: 1);
        var restPiernas = ExerciseRestriction.Create("cuadriceps", sentadilla.Id, "Sobrecarga patelar", minPainLevel: 1);

        var restrictions = new[] { restHombros, restPiernas };
        var exerciseIds = new[] { pressMilitar.Id, sentadilla.Id };

        // Act
        var results = _checker.Check(exerciseIds, painZones, restrictions);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r =>
        {
            Assert.False(r.Restricted);
            Assert.Null(r.Zone);
            Assert.Null(r.PainLevel);
            Assert.Null(r.Reason);
            Assert.Empty(r.Substitutes);
        });
    }

    [Fact]
    public void GetSubstitutesForZone_DevuelveSustitutosEnOrden()
    {
        var sentadilla = Exercise.Create("Sentadilla Trasera", "Piernas");
        var sub1 = Exercise.Create("Prensa 45", "Piernas");
        var sub2 = Exercise.Create("Sillón de cuádriceps", "Piernas");

        var restriction = ExerciseRestriction.Create("cuadriceps", sentadilla.Id, "Compresión articular", minPainLevel: 1);
        restriction.AddSubstitute(sub2, order: 2);
        restriction.AddSubstitute(sub1, order: 1);

        var restrictions = new[] { restriction };

        // Act
        var substitutes = _checker.GetSubstitutesForZone(sentadilla.Id, "cuadriceps", restrictions);

        // Assert
        Assert.Equal(2, substitutes.Count);
        Assert.Equal(sub1.Id, substitutes[0].ExerciseId);
        Assert.Equal("Prensa 45", substitutes[0].Name);
        Assert.Equal(sub2.Id, substitutes[1].ExerciseId);
        Assert.Equal("Sillón de cuádriceps", substitutes[1].Name);
    }

    [Fact]
    public void ParsePainZones_MultiplesZonasYEspacios_ParseaCorrectamente()
    {
        var raw = " cuadriceps:2 , espalda_baja:3 , isquiotibiales: 1 ";

        var parsed = _checker.ParsePainZones(raw);

        Assert.Equal(3, parsed.Count);
        Assert.Equal(2, parsed["cuadriceps"]);
        Assert.Equal(3, parsed["espalda_baja"]);
        Assert.Equal(1, parsed["isquiotibiales"]);
    }

    [Fact]
    public void ParsePainZones_ZonaRepetida_MantieneElMayorNivelDeDolor()
    {
        var raw = "cuadriceps:1,cuadriceps:3";

        var parsed = _checker.ParsePainZones(raw);

        Assert.Single(parsed);
        Assert.Equal(3, parsed["cuadriceps"]);
    }
}
