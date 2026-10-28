using Domain.Entities;
using Domain.Services;
using Xunit;

namespace Tests;

public class ExerciseRestrictionCheckerTests
{
    private readonly ExerciseRestrictionChecker _checker = new();

    [Fact]
    public void Check_SentadillaRestringidaYPressNo()
    {
        var sentadilla = Exercise.Create("Sentadilla Trasera", "Piernas");
        var prensa = Exercise.Create("Prensa de piernas", "Piernas");
        var bandas = Exercise.Create("Sentadillas con bandas elásticas", "Piernas");
        var pressDeBanca = Exercise.Create("Press de banca plano", "Pecho");

        var restrictionSentadilla = ExerciseRestriction.Create("cuadriceps", sentadilla.Id, "Sobrecarga patelofemoral", minPainLevel: 1);
        restrictionSentadilla.AddSubstitute(prensa, 1);
        restrictionSentadilla.AddSubstitute(bandas, 2);

        var restrictions = new[] { restrictionSentadilla };
        var exerciseIds = new[] { sentadilla.Id, pressDeBanca.Id };

        // Act
        var results = _checker.Check(exerciseIds, "cuadriceps:2", restrictions);

        // Assert
        Assert.Equal(2, results.Count);

        var sentadillaResult = results.First(r => r.ExerciseId == sentadilla.Id);
        Assert.True(sentadillaResult.Restricted);
        Assert.Equal("cuadriceps", sentadillaResult.Zone);
        Assert.Equal(2, sentadillaResult.PainLevel);
        Assert.Equal("Sobrecarga patelofemoral", sentadillaResult.Reason);
        Assert.Equal(2, sentadillaResult.Substitutes.Count);
        Assert.Equal(prensa.Id, sentadillaResult.Substitutes[0].ExerciseId);
        Assert.Equal("Prensa de piernas", sentadillaResult.Substitutes[0].Name);
        Assert.Equal(bandas.Id, sentadillaResult.Substitutes[1].ExerciseId);
        Assert.Equal("Sentadillas con bandas elásticas", sentadillaResult.Substitutes[1].Name);

        var pressResult = results.First(r => r.ExerciseId == pressDeBanca.Id);
        Assert.False(pressResult.Restricted);
        Assert.Null(pressResult.Zone);
        Assert.Null(pressResult.PainLevel);
        Assert.Null(pressResult.Reason);
        Assert.Empty(pressResult.Substitutes);
    }

    [Fact]
    public void Check_DolorMenorAMinPainLevel_NoRestringe()
    {
        var sentadilla = Exercise.Create("Sentadilla Trasera", "Piernas");
        var prensa = Exercise.Create("Prensa de piernas", "Piernas");

        var restriction = ExerciseRestriction.Create("cuadriceps", sentadilla.Id, "Dolor articular moderado a severo", minPainLevel: 2);
        restriction.AddSubstitute(prensa, 1);

        var restrictions = new[] { restriction };
        var exerciseIds = new[] { sentadilla.Id };

        // Act
        var results = _checker.Check(exerciseIds, "cuadriceps:1", restrictions);

        // Assert
        Assert.Single(results);
        var result = results[0];
        Assert.False(result.Restricted);
        Assert.Null(result.Zone);
        Assert.Null(result.PainLevel);
        Assert.Null(result.Reason);
        Assert.Empty(result.Substitutes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Check_SinDolor_NoRestringeNada(string? painZones)
    {
        var sentadilla = Exercise.Create("Sentadilla Trasera", "Piernas");
        var press = Exercise.Create("Press de banca", "Pecho");

        var restriction = ExerciseRestriction.Create("cuadriceps", sentadilla.Id, "Sobrecarga patelar", minPainLevel: 1);

        var restrictions = new[] { restriction };
        var exerciseIds = new[] { sentadilla.Id, press.Id };

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

    [Fact]
    public void Check_EjercicioConMultiplesZonasRestringidas_PriorizaLaDeMayorDolor()
    {
        var pesoMuerto = Exercise.Create("Peso Muerto", "Espalda");
        var sub1 = Exercise.Create("Puente de glúteos", "Glúteos");
        var sub2 = Exercise.Create("Remo con mancuernas", "Espalda");

        var restEspalda = ExerciseRestriction.Create("espalda_baja", pesoMuerto.Id, "Carga lumbar", minPainLevel: 1);
        restEspalda.AddSubstitute(sub2, 1);

        var restIsquios = ExerciseRestriction.Create("isquiotibiales", pesoMuerto.Id, "Tensión isquiosural", minPainLevel: 1);
        restIsquios.AddSubstitute(sub1, 1);

        var restrictions = new[] { restEspalda, restIsquios };

        // Dolor en espalda_baja es 3, isquiotibiales es 1 -> debe priorizar espalda_baja
        var results = _checker.Check(new[] { pesoMuerto.Id }, "isquiotibiales:1,espalda_baja:3", restrictions);

        Assert.Single(results);
        var result = results[0];
        Assert.True(result.Restricted);
        Assert.Equal("espalda_baja", result.Zone);
        Assert.Equal(3, result.PainLevel);
        Assert.Equal("Carga lumbar", result.Reason);
        Assert.Single(result.Substitutes);
        Assert.Equal(sub2.Id, result.Substitutes[0].ExerciseId);
    }
}
