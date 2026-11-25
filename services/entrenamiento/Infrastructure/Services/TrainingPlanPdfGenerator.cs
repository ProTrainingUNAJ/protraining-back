using Application.Interfaces.Services;
using Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Infrastructure.Services;

public sealed class TrainingPlanPdfGenerator : ITrainingPlanPdfGenerator
{
    static TrainingPlanPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(TrainingPlan plan)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Darken3));

                page.Header().Column(header =>
                {
                    header.Spacing(4);
                    header.Item().Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("PROTRAINING").FontSize(11).Bold().FontColor(Colors.Blue.Darken2);
                            col.Item().Text(plan.Name).FontSize(16).Bold().FontColor(Colors.Black);
                            if (!string.IsNullOrWhiteSpace(plan.Objective))
                            {
                                col.Item().Text($"Objetivo: {plan.Objective}").FontSize(10).Italic().FontColor(Colors.Grey.Darken1);
                            }
                        });

                        row.ConstantItem(160).AlignRight().Column(col =>
                        {
                            col.Item().Text("PLANILLA DE RUTINA").FontSize(9).Bold().FontColor(Colors.Grey.Darken2);
                            col.Item().Text($"Semanas: {plan.WeekCount}").FontSize(9);
                            var argTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "America/Argentina/Buenos_Aires");
                            col.Item().Text($"Fecha: {argTime:dd/MM/yyyy}").FontSize(8).FontColor(Colors.Grey.Darken1);
                        });
                    });

                    header.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().PaddingVertical(10).Column(content =>
                {
                    content.Spacing(12);

                    var orderedWeeks = plan.Weeks.OrderBy(w => w.WeekNumber).ToList();
                    var totalExercises = orderedWeeks.SelectMany(w => w.Sessions).SelectMany(s => s.PrescribedExercises).Count();

                    if (orderedWeeks.Count == 0 || totalExercises == 0)
                    {
                        content.Item().Background(Colors.Grey.Lighten4).Padding(16).Column(alert =>
                        {
                            alert.Spacing(6);
                            alert.Item().Text("Aviso de Planificación").FontSize(12).Bold().FontColor(Colors.Orange.Darken2);
                            alert.Item().Text("Esta planificación no contiene ejercicios programados actualmente. Podés consultar y editar la rutina desde la plataforma ProTraining.")
                                .FontSize(10).FontColor(Colors.Grey.Darken2);
                        });
                        return;
                    }

                    foreach (var week in orderedWeeks)
                    {
                        content.Item().Background(Colors.Blue.Lighten5).Padding(6).Row(row =>
                        {
                            row.RelativeItem().Text($"SEMANA {week.WeekNumber}").FontSize(11).Bold().FontColor(Colors.Blue.Darken3);
                            row.AutoItem().Text($"{week.Sessions.Count} sesiones programadas").FontSize(9).FontColor(Colors.Blue.Darken2);
                        });

                        var orderedSessions = week.Sessions.OrderBy(s => s.Order).ToList();
                        foreach (var session in orderedSessions)
                        {
                            content.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(sessionCol =>
                            {
                                sessionCol.Spacing(6);

                                sessionCol.Item().Row(sRow =>
                                {
                                    sRow.RelativeItem().Column(sTitle =>
                                    {
                                        sTitle.Item().Text($"Sesión {session.Order}: {session.Name}").FontSize(10).Bold().FontColor(Colors.Black);
                                        var dayName = TranslateDayOfWeek(session.DayOfWeek);
                                        var timeStr = session.StartTime.HasValue ? $" · {session.StartTime.Value:HH:mm} hs" : string.Empty;
                                        sTitle.Item().Text($"Día: {dayName}{timeStr}").FontSize(8).FontColor(Colors.Grey.Darken1);
                                    });

                                    if (!string.IsNullOrWhiteSpace(session.CoachNotes))
                                    {
                                        sRow.RelativeItem().AlignRight().Text($"Nota PF: {session.CoachNotes}").FontSize(8).Italic().FontColor(Colors.Grey.Darken2);
                                    }
                                });

                                var exercises = session.PrescribedExercises.OrderBy(e => e.Order).ToList();
                                if (exercises.Count == 0)
                                {
                                    sessionCol.Item().Padding(4).Text("Sin ejercicios prescritos para esta sesión.").FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
                                }
                                else
                                {
                                    sessionCol.Item().Table(table =>
                                    {
                                        table.ColumnsDefinition(columns =>
                                        {
                                            columns.ConstantColumn(24);
                                            columns.RelativeColumn(3);
                                            columns.ConstantColumn(45);
                                            columns.ConstantColumn(45);
                                            columns.ConstantColumn(55);
                                            columns.ConstantColumn(65);
                                            columns.ConstantColumn(75);
                                        });

                                        table.Header(header =>
                                        {
                                            header.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text("#").FontSize(8).Bold();
                                            header.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text("Ejercicio").FontSize(8).Bold();
                                            header.Cell().Background(Colors.Grey.Lighten3).Padding(3).AlignCenter().Text("Series").FontSize(8).Bold();
                                            header.Cell().Background(Colors.Grey.Lighten3).Padding(3).AlignCenter().Text("Reps").FontSize(8).Bold();
                                            header.Cell().Background(Colors.Grey.Lighten3).Padding(3).AlignCenter().Text("% 1RM").FontSize(8).Bold();
                                            header.Cell().Background(Colors.Grey.Lighten3).Padding(3).AlignCenter().Text("Dist / Tiempo").FontSize(8).Bold();
                                            header.Cell().Background(Colors.Grey.Lighten3).Padding(3).AlignCenter().Text("Anotación").FontSize(8).Bold();
                                        });

                                        for (var i = 0; i < exercises.Count; i++)
                                        {
                                            var ex = exercises[i];
                                            var bgColor = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten5;

                                            table.Cell().Background(bgColor).Padding(3).Text(ex.Order.ToString()).FontSize(8);
                                            table.Cell().Background(bgColor).Padding(3).Column(col =>
                                            {
                                                var name = ex.Exercise?.Name ?? "Ejercicio";
                                                col.Item().Text(name).FontSize(8).Bold();
                                                if (!string.IsNullOrWhiteSpace(ex.Exercise?.MuscleGroup))
                                                {
                                                    col.Item().Text(ex.Exercise.MuscleGroup).FontSize(7).FontColor(Colors.Grey.Darken1);
                                                }
                                            });
                                            table.Cell().Background(bgColor).Padding(3).AlignCenter().Text(ex.Sets.ToString()).FontSize(8);
                                            table.Cell().Background(bgColor).Padding(3).AlignCenter().Text(ex.Reps.ToString()).FontSize(8);
                                            table.Cell().Background(bgColor).Padding(3).AlignCenter().Text(ex.TargetPercentage.HasValue ? $"{ex.TargetPercentage.Value:0.#}%" : "-").FontSize(8);

                                            var extra = "-";
                                            if (ex.TargetDistanceMeters.HasValue && ex.TargetTimeSeconds.HasValue)
                                                extra = $"{ex.TargetDistanceMeters}m / {ex.TargetTimeSeconds}s";
                                            else if (ex.TargetDistanceMeters.HasValue)
                                                extra = $"{ex.TargetDistanceMeters}m";
                                            else if (ex.TargetTimeSeconds.HasValue)
                                                extra = $"{ex.TargetTimeSeconds}s";

                                            table.Cell().Background(bgColor).Padding(3).AlignCenter().Text(extra).FontSize(8);
                                            table.Cell().Background(bgColor).Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(2).AlignCenter().Text("[    kg]").FontSize(7).FontColor(Colors.Grey.Darken1);
                                        }
                                    });
                                }
                            });
                        }
                    }
                });

                page.Footer().Column(footer =>
                {
                    footer.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                    footer.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text("ProTraining · Planificación deportiva para campo y gimnasio").FontSize(7).FontColor(Colors.Grey.Darken1);
                        row.AutoItem().Text(text =>
                        {
                            text.Span("Página ").FontSize(7).FontColor(Colors.Grey.Darken1);
                            text.CurrentPageNumber().FontSize(7).FontColor(Colors.Grey.Darken1);
                            text.Span(" de ").FontSize(7).FontColor(Colors.Grey.Darken1);
                            text.TotalPages().FontSize(7).FontColor(Colors.Grey.Darken1);
                        });
                    });
                });
            });
        });

        return document.GeneratePdf();
    }

    private static string TranslateDayOfWeek(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Lunes",
        DayOfWeek.Tuesday => "Martes",
        DayOfWeek.Wednesday => "Miércoles",
        DayOfWeek.Thursday => "Jueves",
        DayOfWeek.Friday => "Viernes",
        DayOfWeek.Saturday => "Sábado",
        DayOfWeek.Sunday => "Domingo",
        _ => day.ToString()
    };
}
