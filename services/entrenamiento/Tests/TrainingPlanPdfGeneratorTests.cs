using System.Diagnostics;
using System.Text;
using Domain.Entities;
using Infrastructure.Services;
using Xunit;

namespace Tests;

public class TrainingPlanPdfGeneratorTests
{
    private readonly TrainingPlanPdfGenerator _generator = new();

    [Fact]
    public void Generate_DadoPlanCompleto_CuandoProcesa_EntoncesRetornaPdfValidoMayorA10KbSinExcepciones()
    {
        // ARRANGE: Planificación completa con 2 semanas, sesiones y múltiples ejercicios
        var plan = TrainingPlan.Create("Fuerza y Potencia Pretemporada 2026", "Desarrollar fuerza máxima y explosiva");
        
        var exercise1 = Exercise.Create("Sentadilla con barra", "Piernas", "barra con pesas");
        var exercise2 = Exercise.Create("Press de banca plano", "Pecho", "barra con pesas");
        var exercise3 = Exercise.Create("Peso muerto convencional", "Espalda", "barra con pesas");

        for (int w = 1; w <= 2; w++)
        {
            var week = TrainingWeek.Create(w, plan.Id);

            var session1 = TrainingSession.Create(
                week.Id,
                "Fuerza Tren Inferior",
                1,
                DayOfWeek.Monday,
                "Mantener técnica estricta y descansos de 3 min.",
                new TimeOnly(9, 30));

            var ex1 = PrescribedExercise.Create(1, 4, 6, 82.5m, session1.Id, exercise1.Id);
            typeof(PrescribedExercise).GetProperty(nameof(PrescribedExercise.Exercise))!.SetValue(ex1, exercise1);
            session1.AddPrescribedExercise(ex1);
            week.AddSession(session1);

            var session2 = TrainingSession.Create(
                week.Id,
                "Fuerza Tren Superior",
                2,
                DayOfWeek.Wednesday,
                "Controlar fase excéntrica en 2 segundos.",
                new TimeOnly(10, 0));

            var ex2 = PrescribedExercise.Create(1, 4, 8, 77.5m, session2.Id, exercise2.Id);
            typeof(PrescribedExercise).GetProperty(nameof(PrescribedExercise.Exercise))!.SetValue(ex2, exercise2);
            session2.AddPrescribedExercise(ex2);
            week.AddSession(session2);

            var session3 = TrainingSession.Create(
                week.Id,
                "Potencia e Isquios",
                3,
                DayOfWeek.Friday,
                "Cuidar postura lumbar.",
                new TimeOnly(9, 30));

            var ex3 = PrescribedExercise.Create(1, 3, 5, 85m, session3.Id, exercise3.Id);
            typeof(PrescribedExercise).GetProperty(nameof(PrescribedExercise.Exercise))!.SetValue(ex3, exercise3);
            session3.AddPrescribedExercise(ex3);
            week.AddSession(session3);

            plan.AddWeek(week);
        }

        // ACT
        var stopwatch = Stopwatch.StartNew();
        var pdfBytes = _generator.Generate(plan);
        stopwatch.Stop();

        // ASSERT
        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 10 * 1024, $"El PDF generado debería ser mayor a 10 KB, pero midió {pdfBytes.Length} bytes.");

        // Cabecera estándar de PDF: comienza con "%PDF"
        var header = Encoding.ASCII.GetString(pdfBytes.Take(4).ToArray());
        Assert.Equal("%PDF", header);

        // Rendimiento: criterio estricto de menos de 800 ms
        Assert.True(stopwatch.ElapsedMilliseconds < 800, $"La generación del PDF tardó {stopwatch.ElapsedMilliseconds} ms, excediendo el límite de 800 ms.");
    }

    [Fact]
    public void Generate_DadoPlanVacioOSinEjercicios_CuandoProcesa_EntoncesGeneraPdfConAvisoSinExcepciones()
    {
        // ARRANGE: Plan sin semanas ni ejercicios
        var plan = TrainingPlan.Create("Planificación en Borrador", "Plan recién creado sin sesiones");

        // ACT
        var pdfBytes = _generator.Generate(plan);

        // ASSERT
        Assert.NotNull(pdfBytes);
        Assert.NotEmpty(pdfBytes);

        var header = Encoding.ASCII.GetString(pdfBytes.Take(4).ToArray());
        Assert.Equal("%PDF", header);
    }
}
