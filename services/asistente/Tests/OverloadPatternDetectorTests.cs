using Domain.Common;
using Domain.Entities;
using Xunit;
using System.Text.RegularExpressions;

namespace Asistente.Tests;

/// <summary>
/// Pruebas unitarias de dominio para el detector de patrones de sobrecarga, riesgo y correlación de fixture (#177 · KAN-472 / #178 · KAN-473).
/// </summary>
public sealed class OverloadPatternDetectorTests
{
    private readonly Guid _athleteId = Guid.NewGuid();

    [Fact]
    public void Given_EmptyRecords_When_Detect_Then_ReturnsNone()
    {
        var result = OverloadPatternDetector.Detect(_athleteId, []);

        Assert.False(result.HasPattern);
        Assert.Equal(string.Empty, result.Reason);
    }

    [Fact]
    public void Given_EmptyAthleteId_When_Detect_Then_ReturnsNone()
    {
        var record = new AthleteCheckInRecord(
            new DateOnly(2026, 11, 4), 5.0, 2, 4, 4, 3, 3, 9, false, null, false);

        var result = OverloadPatternDetector.Detect(Guid.Empty, [record]);

        Assert.False(result.HasPattern);
    }

    [Fact]
    public void Given_ActiveInjury_When_Detect_Then_ReturnsInjuryRiskHighSeverity()
    {
        var records = new List<AthleteCheckInRecord>
        {
            new(new DateOnly(2026, 11, 1), 7.5, 4, 2, 2, 4, 2, 7, false, null, false),
            new(new DateOnly(2026, 11, 2), 7.0, 3, 3, 3, 3, 2, 6, false, "Tendón rotuliano derecho", true)
        };

        var result = OverloadPatternDetector.Detect(_athleteId, records);

        Assert.True(result.HasPattern);
        Assert.Equal(AlertType.InjuryRisk, result.Type);
        Assert.Equal(AlertSeverity.High, result.Severity);
        Assert.Equal(SuggestedAdjustmentType.Rest, result.SuggestedAdjustmentType);
        Assert.Equal(50, result.SuggestedAdjustmentPercentage);
        Assert.Contains("lesión activa", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tendón rotuliano derecho", result.Reason);
    }

    [Fact]
    public void Given_ActiveInjuryWithTwoHighEffortSessionsAndUpcomingMatch_When_Detect_Then_PrioritizesInjuryRisk()
    {
        // KAN-477 · Prioridad 1: Lesión activa prevalece sobre partido y sobrecarga
        var matchDate = new DateOnly(2026, 11, 7);
        var records = new List<AthleteCheckInRecord>
        {
            new(new DateOnly(2026, 11, 2), 8.0, 4, 2, 2, 4, 2, 8, false, null, false),
            new(new DateOnly(2026, 11, 3), 7.0, 2, 4, 4, 3, 3, 9, false, "Isquiotibial izquierdo", true)
        };

        var result = OverloadPatternDetector.Detect(
            _athleteId,
            records,
            matchDate: matchDate,
            daysUntilMatch: 3,
            hasUpcomingMatchInProximity: true);

        Assert.True(result.HasPattern);
        Assert.Equal(AlertType.InjuryRisk, result.Type);
        Assert.Equal(AlertSeverity.High, result.Severity);
        Assert.Equal(SuggestedAdjustmentType.Rest, result.SuggestedAdjustmentType);
        Assert.Equal(50, result.SuggestedAdjustmentPercentage);
        Assert.Contains("lesión activa", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // KAN-477 (#182): Pruebas de umbral adaptativo por partido próximo
    // =========================================================================

    [Fact]
    public void K477_01_Given_TwoHighEffortSessionsAndMatchInD3_When_Detect_Then_GeneratesAlertWithReducedThreshold()
    {
        // DADO un atleta con 2 sesiones de esfuerzo alto y un partido en D+3 (ej. sábado con análisis miércoles)
        var matchDate = new DateOnly(2026, 11, 7);
        var records = new List<AthleteCheckInRecord>
        {
            new(new DateOnly(2026, 11, 2), 8.0, 4, 2, 2, 4, 2, 8, false, null, false), // Lunes: RPE 8
            new(new DateOnly(2026, 11, 3), 8.0, 4, 2, 2, 4, 2, 9, false, null, false)  // Martes: RPE 9
        };

        var result = OverloadPatternDetector.Detect(
            _athleteId,
            records,
            matchDate: matchDate,
            daysUntilMatch: 3,
            hasUpcomingMatchInProximity: true);

        Assert.True(result.HasPattern);
        Assert.Equal(AlertType.AcuteOverload, result.Type);
        Assert.Equal(AlertSeverity.High, result.Severity);
        Assert.Equal(SuggestedAdjustmentType.LoadReduction, result.SuggestedAdjustmentType);
        Assert.Equal(20, result.SuggestedAdjustmentPercentage);
        Assert.Contains("El atleta acumula 2 sesiones de esfuerzo alto y tiene un partido próximo", result.Reason);
        Assert.Contains("07/11", result.Reason);
    }

    [Fact]
    public void K477_02_Given_TwoHighEffortSessionsWithoutMatch_When_Detect_Then_DoesNotGenerateAlert()
    {
        // DADO un atleta con 2 sesiones duras y sin partido próximo
        var records = new List<AthleteCheckInRecord>
        {
            new(new DateOnly(2026, 11, 2), 8.0, 4, 2, 2, 4, 2, 8, false, null, false),
            new(new DateOnly(2026, 11, 3), 8.0, 4, 2, 2, 4, 2, 9, false, null, false)
        };

        var result = OverloadPatternDetector.Detect(
            _athleteId,
            records,
            hasMatchInTwoDays: false,
            matchDate: null,
            daysUntilMatch: null,
            hasUpcomingMatchInProximity: false);

        Assert.False(result.HasPattern);
    }

    [Fact]
    public void K477_03_Given_ThreeHighEffortSessionsWithoutMatch_When_Detect_Then_AppliesNormalRule()
    {
        // DADO un atleta con 3 sesiones duras y sin partido próximo
        var records = new List<AthleteCheckInRecord>
        {
            new(new DateOnly(2026, 11, 1), 8.0, 4, 2, 2, 4, 2, 8, false, null, false),
            new(new DateOnly(2026, 11, 2), 7.5, 4, 2, 2, 4, 2, 9, false, null, false),
            new(new DateOnly(2026, 11, 3), 8.0, 4, 3, 3, 4, 2, 8, false, null, false)
        };

        var result = OverloadPatternDetector.Detect(
            _athleteId,
            records,
            hasMatchInTwoDays: false,
            matchDate: null,
            daysUntilMatch: null,
            hasUpcomingMatchInProximity: false);

        Assert.True(result.HasPattern);
        Assert.Equal(AlertType.AcuteOverload, result.Type);
        Assert.Equal(AlertSeverity.High, result.Severity);
        Assert.Equal(SuggestedAdjustmentType.LoadReduction, result.SuggestedAdjustmentType);
        Assert.Equal(20, result.SuggestedAdjustmentPercentage);
        Assert.Contains("Sobrecarga aguda sostenida: 3 sesiones con RPE >= 8", result.Reason);
    }

    [Fact]
    public void K477_04_Given_TwoHighEffortSessionsAndMatchInD4_When_Detect_Then_DoesNotReduceThreshold()
    {
        // DADO un partido fuera de la ventana (D+4, ej. domingo si el análisis es miércoles)
        var matchDate = new DateOnly(2026, 11, 8);
        var records = new List<AthleteCheckInRecord>
        {
            new(new DateOnly(2026, 11, 2), 8.0, 4, 2, 2, 4, 2, 8, false, null, false),
            new(new DateOnly(2026, 11, 3), 8.0, 4, 2, 2, 4, 2, 9, false, null, false)
        };

        var result = OverloadPatternDetector.Detect(
            _athleteId,
            records,
            hasMatchInTwoDays: false,
            matchDate: matchDate,
            daysUntilMatch: 4,
            hasUpcomingMatchInProximity: false);

        Assert.False(result.HasPattern);
    }

    [Fact]
    public void K477_05_Given_TwoHighEffortSessionsAndMatchExactlyInD3_When_Detect_Then_ReducesThreshold()
    {
        // DADO un partido situado exactamente en el último día permitido (D+3)
        var matchDate = new DateOnly(2026, 11, 7);
        var records = new List<AthleteCheckInRecord>
        {
            new(new DateOnly(2026, 11, 2), 8.0, 4, 2, 2, 4, 2, 8, false, null, false),
            new(new DateOnly(2026, 11, 3), 8.0, 4, 2, 2, 4, 2, 8, false, null, false)
        };

        var result = OverloadPatternDetector.Detect(
            _athleteId,
            records,
            matchDate: matchDate,
            daysUntilMatch: 3,
            hasUpcomingMatchInProximity: true);

        Assert.True(result.HasPattern);
        Assert.Equal(AlertType.AcuteOverload, result.Type);
        Assert.Contains("El atleta acumula 2 sesiones de esfuerzo alto y tiene un partido próximo", result.Reason);
    }

    [Fact]
    public void K477_06_Given_OneHighEffortSessionAndMatchInD1OrD2_When_Detect_Then_PreservesKan473PreMatchAlert()
    {
        // KAN-473 Prioridad: partido en D+1 con 1 sola sesión de esfuerzo alto
        var matchDate = new DateOnly(2026, 11, 5);
        var records = new List<AthleteCheckInRecord>
        {
            new(new DateOnly(2026, 11, 2), 8.0, 4, 2, 2, 4, 2, 5, false, null, false),
            new(new DateOnly(2026, 11, 4), 7.5, 4, 2, 2, 4, 2, 9, false, null, false) // 1 sesión RPE 9
        };

        var result = OverloadPatternDetector.Detect(
            _athleteId,
            records,
            hasMatchInTwoDays: true,
            matchDate: matchDate,
            daysUntilMatch: 1,
            hasUpcomingMatchInProximity: true);

        Assert.True(result.HasPattern);
        Assert.Equal(AlertType.AcuteOverload, result.Type);
        Assert.Equal(AlertSeverity.High, result.Severity);
        Assert.Equal(SuggestedAdjustmentType.LoadReduction, result.SuggestedAdjustmentType);
        Assert.Equal(30, result.SuggestedAdjustmentPercentage); // 30% según KAN-473
        Assert.Contains("Alerta pre-partido (D-1)", result.Reason);
    }

    [Fact]
    public void K477_07_Given_NoHighEffortSessionsWithUpcomingMatch_When_Detect_Then_ReturnsNoPattern()
    {
        // DADO un atleta sin sesiones duras pero con partido próximo
        var matchDate = new DateOnly(2026, 11, 7);
        var records = new List<AthleteCheckInRecord>
        {
            new(new DateOnly(2026, 11, 2), 8.0, 4, 1, 1, 4, 1, 5, false, null, false),
            new(new DateOnly(2026, 11, 3), 8.0, 4, 2, 2, 4, 2, 6, false, null, false)
        };

        var result = OverloadPatternDetector.Detect(
            _athleteId,
            records,
            matchDate: matchDate,
            daysUntilMatch: 3,
            hasUpcomingMatchInProximity: true);

        Assert.False(result.HasPattern);
    }

    [Fact]
    public void Given_HighEffortAndMatchInTwoDays_When_Detect_Then_ReturnsPreMatchAlertHighSeverity()
    {
        // KAN-473 · Criterio de Aceptación:
        // DADO un atleta con esfuerzo alto y partido en dos días CUANDO corre el análisis ENTONCES queda marcado para aviso.
        var matchDate = new DateOnly(2026, 11, 6);
        var records = new List<AthleteCheckInRecord>
        {
            new(new DateOnly(2026, 11, 2), 8.0, 4, 2, 2, 4, 2, 5, false, null, false),
            new(new DateOnly(2026, 11, 4), 7.5, 4, 2, 2, 4, 2, 8, false, null, false) // RPE 8 reciente (D-2 del partido)
        };

        var result = OverloadPatternDetector.Detect(
            _athleteId,
            records,
            hasMatchInTwoDays: true,
            matchDate: matchDate);

        Assert.True(result.HasPattern);
        Assert.Equal(AlertType.AcuteOverload, result.Type);
        Assert.Equal(AlertSeverity.High, result.Severity);
        Assert.Equal(SuggestedAdjustmentType.LoadReduction, result.SuggestedAdjustmentType);
        Assert.Equal(30, result.SuggestedAdjustmentPercentage);
        Assert.Contains("Alerta pre-partido (D-2)", result.Reason);
        Assert.Contains("06/11", result.Reason);
        Assert.Contains("RPE 8", result.Reason);
    }

    [Fact]
    public void Given_HighEffortAndMatchTomorrow_When_Detect_Then_ReturnsPreMatchAlertHighSeverityD1()
    {
        // KAN-473 · Enzo revisión: ventana D-1 / D-2 (partido mañana)
        var matchDate = new DateOnly(2026, 11, 5);
        var records = new List<AthleteCheckInRecord>
        {
            new(new DateOnly(2026, 11, 2), 8.0, 4, 2, 2, 4, 2, 5, false, null, false),
            new(new DateOnly(2026, 11, 4), 7.5, 4, 2, 2, 4, 2, 9, false, null, false)
        };

        var result = OverloadPatternDetector.Detect(
            _athleteId,
            records,
            hasMatchInTwoDays: true,
            matchDate: matchDate,
            daysUntilMatch: 1);

        Assert.True(result.HasPattern);
        Assert.Equal(AlertType.AcuteOverload, result.Type);
        Assert.Equal(AlertSeverity.High, result.Severity);
        Assert.Equal(SuggestedAdjustmentType.LoadReduction, result.SuggestedAdjustmentType);
        Assert.Equal(30, result.SuggestedAdjustmentPercentage);
        Assert.Contains("Alerta pre-partido (D-1)", result.Reason);
        Assert.Contains("para mañana", result.Reason);
        Assert.Contains("05/11", result.Reason);
        Assert.Contains("RPE 9", result.Reason);
    }

    [Fact]
    public void Given_HighEffortWithoutMatchInTwoDaysAndNormalSleep_When_Detect_Then_ReturnsNoPattern()
    {
        var records = new List<AthleteCheckInRecord>
        {
            new(new DateOnly(2026, 11, 2), 8.0, 4, 1, 1, 4, 1, 5, false, null, false),
            new(new DateOnly(2026, 11, 4), 8.0, 4, 2, 2, 4, 2, 8, false, null, false) // RPE 8 pero con buen sueño y sin partido
        };

        var result = OverloadPatternDetector.Detect(
            _athleteId,
            records,
            hasMatchInTwoDays: false);

        Assert.False(result.HasPattern);
    }

    [Fact]
    public void Given_HighEffortAndLowSleep_When_Detect_Then_ReturnsAccumulatedFatigueHighSeverity()
    {
        var records = new List<AthleteCheckInRecord>
        {
            new(new DateOnly(2026, 11, 1), 7.5, 4, 2, 2, 4, 2, 6, false, null, false),
            new(new DateOnly(2026, 11, 2), 5.0, 2, 4, 3, 3, 3, 9, false, null, false) // RPE 9 + Sueño 5.0h
        };

        var result = OverloadPatternDetector.Detect(_athleteId, records);

        Assert.True(result.HasPattern);
        Assert.Equal(AlertType.AccumulatedFatigue, result.Type);
        Assert.Equal(AlertSeverity.High, result.Severity);
        Assert.Equal(SuggestedAdjustmentType.LoadReduction, result.SuggestedAdjustmentType);
        Assert.Equal(25, result.SuggestedAdjustmentPercentage);
        Assert.Contains("RPE 9", result.Reason);
        Assert.Contains("5 horas de sueño", result.Reason);
    }

    [Fact]
    public void Given_ThreeOrMoreHighEffortSessions_When_Detect_Then_ReturnsAcuteOverloadHighSeverity()
    {
        var records = new List<AthleteCheckInRecord>
        {
            new(new DateOnly(2026, 11, 1), 8.0, 4, 2, 2, 4, 2, 8, false, null, false),
            new(new DateOnly(2026, 11, 2), 7.5, 4, 2, 2, 4, 2, 9, false, null, false),
            new(new DateOnly(2026, 11, 3), 8.0, 4, 3, 3, 4, 2, 8, false, null, false)
        };

        var result = OverloadPatternDetector.Detect(_athleteId, records);

        Assert.True(result.HasPattern);
        Assert.Equal(AlertType.AcuteOverload, result.Type);
        Assert.Equal(AlertSeverity.High, result.Severity);
        Assert.Equal(SuggestedAdjustmentType.LoadReduction, result.SuggestedAdjustmentType);
        Assert.Equal(20, result.SuggestedAdjustmentPercentage);
        Assert.Contains("3 sesiones con RPE >= 8", result.Reason);
    }

    [Fact]
    public void Given_HighAverageFatigueOrSoreness_When_Detect_Then_ReturnsAccumulatedFatigueMediumSeverity()
    {
        var records = new List<AthleteCheckInRecord>
        {
            new(new DateOnly(2026, 11, 1), 7.0, 3, 4, 4, 3, 3, 6, false, null, false),
            new(new DateOnly(2026, 11, 2), 7.5, 3, 4, 4, 3, 3, 7, false, null, false),
            new(new DateOnly(2026, 11, 3), 7.0, 3, 4, 4, 3, 3, 6, false, null, false)
        };

        var result = OverloadPatternDetector.Detect(_athleteId, records);

        Assert.True(result.HasPattern);
        Assert.Equal(AlertType.AccumulatedFatigue, result.Type);
        Assert.Equal(AlertSeverity.Medium, result.Severity);
        Assert.Equal(SuggestedAdjustmentType.RoutineAdjustment, result.SuggestedAdjustmentType);
        Assert.Equal(15, result.SuggestedAdjustmentPercentage);
        Assert.Contains("Fatiga muscular acumulada", result.Reason);
    }

    [Fact]
    public void Given_HealthyAthleteWithNormalCheckIns_When_Detect_Then_ReturnsNoPattern()
    {
        var records = new List<AthleteCheckInRecord>
        {
            new(new DateOnly(2026, 11, 1), 8.0, 4, 1, 1, 4, 1, 6, false, null, false),
            new(new DateOnly(2026, 11, 2), 7.5, 4, 2, 2, 4, 2, 7, false, null, false),
            new(new DateOnly(2026, 11, 3), 8.5, 5, 1, 1, 5, 1, 5, false, null, false)
        };

        var result = OverloadPatternDetector.Detect(_athleteId, records);

        Assert.False(result.HasPattern);
    }

    [Fact]
    public void Given_ActiveInjuryWithSerializedPainZones_When_Detect_Then_FormatsZonesInReadableSpanish()
    {
        var records = new List<AthleteCheckInRecord>
        {
            new(
                new DateOnly(2026, 11, 2),
                7.0, 3, 3, 3, 3, 2, 6,
                false,
                "abdominales:1,biceps_izquierdo:6,pecho_derecho:5,rodilla_derecha:3",
                true)
        };

        var result = OverloadPatternDetector.Detect(_athleteId, records);

        Assert.True(result.HasPattern);
        Assert.Equal(AlertType.InjuryRisk, result.Type);
        Assert.Contains("Abdominales [Nivel 1]", result.Reason);
        Assert.Contains("Bíceps izquierdo [Lesión activa]", result.Reason);
        Assert.Contains("Pecho derecho [Nivel 5]", result.Reason);
        Assert.Contains("Rodilla derecha [Nivel 3]", result.Reason);
        Assert.DoesNotContain("Nivel 6", result.Reason);
        Assert.DoesNotContain("biceps_izquierdo:6", result.Reason);
    }

    [Fact]
    public void Given_ActiveInjuryWithClinicalZoneId_When_Detect_Then_FormatsClinicalZoneReadable()
    {
        var records = new List<AthleteCheckInRecord>
        {
            new(
                new DateOnly(2026, 11, 2),
                7.0, 3, 3, 3, 3, 2, 6,
                false,
                "isquiotibiales:6",
                true)
        };

        var result = OverloadPatternDetector.Detect(_athleteId, records);

        Assert.True(result.HasPattern);
        Assert.Contains("Isquiotibiales [Lesión activa]", result.Reason);
        Assert.DoesNotContain("Nivel 6", result.Reason);
    }

    [Fact]
    public void Given_ActiveInjuryWithManyPainZones_When_Detect_Then_DropsWholeZonesWithoutCuttingWords()
    {
        var manyZones = string.Join(
            ",",
            Enumerable.Range(1, 30).Select(i => $"cuello_trapecio_izquierdo_zona_{i}:6"));

        var records = new List<AthleteCheckInRecord>
        {
            new(new DateOnly(2026, 11, 2), 7.0, 3, 3, 3, 3, 2, 6, false, manyZones, true)
        };

        var result = OverloadPatternDetector.Detect(_athleteId, records);

        Assert.True(result.HasPattern);
        Assert.True(result.Reason.Length <= PreventiveAlert.MaxReasonLength);
        Assert.DoesNotContain("...", result.Reason);
        Assert.Matches(@"y \d+ zonas? más en sus check-ins recientes\.$", result.Reason);

        // Ninguna zona ni etiqueta [Lesión activa] queda cortada por la mitad
        var zonesPart = Regex.Split(result.Reason.Split(" en zona: ")[1],@" y \d+ zonas? más")[0];
        var shownZones = zonesPart.Split(", ");
        Assert.All(shownZones, zone => Assert.EndsWith(" [Lesión activa]", zone));
    }
}
