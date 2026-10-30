using Domain.Entities;
using Domain.Services;
using Xunit;

namespace Tests;

public class ExerciseRestrictionCheckerTests
{
    private readonly ExerciseRestrictionChecker _checker = new();

    // Caso 1: Dolor en hombros nivel 2 y "Press militar tras nuca" restringido para hombros -> restringido, con sus sustitutos en orden.
    // Criterio: DADO un atleta con dolor de hombro CUANDO se evalúa "Press militar tras nuca" ENTONCES el validador lo marca contraindicado y no lo deja pasar sin sustituto.
    [Fact]
    public void Check_ShoulderPainLevel2AndBehindNeckPressRestricted_ReturnsContraindicatedWithSubstitutesInOrder()
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

    // Caso 2: Nivel de dolor menor que min_pain_level -> no restringido.
    [Fact]
    public void Check_PainLevelBelowMinPainLevel_ReturnsNotRestricted()
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

    // Caso 3: Dos zonas con dolor y un ejercicio restringido en las dos -> aparece una vez, con la zona de mayor nivel.
    [Fact]
    public void Check_TwoPainZonesAndExerciseRestrictedInBoth_AppearsOnceWithHigherPainLevelZone()
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

    // Caso 4: Zona con dolor sin restricciones cargadas -> nada restringido.
    [Fact]
    public void Check_PainZoneWithoutConfiguredRestrictions_ReturnsNothingRestricted()
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

    // Caso 5: Ejercicio que no está en la tabla -> permitido.
    [Fact]
    public void Check_ExerciseNotInRestrictionsTable_ReturnsAllowed()
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

    // Caso 6: painZones mal formado (por ejemplo cuadriceps sin nivel, o xyz:abc) -> se ignora esa parte, sin excepción.
    [Theory]
    [InlineData("cuadriceps")]
    [InlineData("xyz:abc")]
    [InlineData("cuadriceps,xyz:abc")]
    [InlineData("cuadriceps:")]
    [InlineData(":2")]
    [InlineData("cuadriceps::2")]
    [InlineData("hombros:0")]
    [InlineData("hombros:-1")]
    public void ParsePainZones_MalformedInput_IgnoresInvalidPartWithoutException(string malformed)
    {
        // Act: no debe lanzar ninguna excepción y debe ignorar las partes malformadas
        var parsed = _checker.ParsePainZones(malformed);

        // Assert
        Assert.Empty(parsed);
    }

    [Fact]
    public void Check_MalformedPainZones_IgnoresInvalidPartsWithoutExceptionAndAppliesValidParts()
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

    // Caso 7: painZones vacío o nulo -> nada restringido.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Check_NullOrEmptyPainZones_ReturnsNothingRestricted(string? painZones)
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
    public void GetSubstitutesForZone_ValidExerciseAndZone_ReturnsSubstitutesInOrder()
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
    public void ParsePainZones_MultipleZonesAndWhitespaces_ParsesCorrectly()
    {
        var raw = " cuadriceps:2 , espalda_baja:3 , isquiotibiales: 1 ";

        var parsed = _checker.ParsePainZones(raw);

        Assert.Equal(3, parsed.Count);
        Assert.Equal(2, parsed["cuadriceps"]);
        Assert.Equal(3, parsed["espalda_baja"]);
        Assert.Equal(1, parsed["isquiotibiales"]);
    }

    [Fact]
    public void ParsePainZones_DuplicateZone_KeepsHighestPainLevel()
    {
        var raw = "cuadriceps:1,cuadriceps:3";

        var parsed = _checker.ParsePainZones(raw);

        Assert.Single(parsed);
        Assert.Equal(3, parsed["cuadriceps"]);
    }
}
