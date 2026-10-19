using Application.DTOs.TrainingPlans;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.UseCases.TrainingPlans.CreatePlan;
using Application.UseCases.TrainingPlans.GetPlan;
using Application.UseCases.TrainingPlans.ListPlans;
using Application.UseCases.TrainingPlans.UpdatePlan;
using Domain.Entities;
using Domain.Exceptions;
using Moq;
using Xunit;

namespace Tests;

public class TrainingPlanUseCasesTests
{
    private readonly Mock<ITrainingPlanRepository> _planRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();

    [Fact]
    public async Task CreatePlan_DadoPlanConSemanaYSesiones_CuandoSeCrea_EntoncesRetornaArbolCompleto()
    {
        var exerciseId = Guid.NewGuid();
        _planRepoMock.Setup(r => r.AllExercisesExistAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new SaveTrainingPlanRequest(
            "Fuerza base",
            "Pretemporada",
            new List<TrainingWeekRequest>
            {
                new(
                    1,
                    new List<TrainingSessionRequest>
                    {
                        new(
                            "Miércoles",
                            1,
                            DayOfWeek.Wednesday,
                            "Controlar técnica en descenso",
                            new List<PrescribedExerciseRequest>
                            {
                                new(exerciseId, 1, 4, 5, 80m)
                            }
                        ),
                        new(
                            "Viernes",
                            2,
                            DayOfWeek.Friday,
                            null,
                            new List<PrescribedExerciseRequest>
                            {
                                new(exerciseId, 1, 3, 8, 70m)
                            }
                        )
                    }
                )
            }
        );

        var handler = new CreateTrainingPlanCommandHandler(_planRepoMock.Object, _unitOfWorkMock.Object);

        var result = await handler.Handle(new CreateTrainingPlanCommand(request));

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Fuerza base", result.Name);
        Assert.Equal("Pretemporada", result.Objective);
        Assert.Single(result.Weeks);

        var week = result.Weeks[0];
        Assert.Equal(1, week.WeekNumber);
        Assert.Equal(2, week.Sessions.Count);

        var session1 = week.Sessions[0];
        Assert.Equal("Miércoles", session1.Name);
        Assert.Equal(1, session1.Order);
        Assert.Equal(DayOfWeek.Wednesday, session1.DayOfWeek);
        Assert.Equal("Controlar técnica en descenso", session1.CoachNotes);
        Assert.Single(session1.Exercises);
        Assert.Equal(4, session1.Exercises[0].Sets);
        Assert.Equal(5, session1.Exercises[0].Reps);

        var session2 = week.Sessions[1];
        Assert.Equal("Viernes", session2.Name);
        Assert.Equal(2, session2.Order);
        Assert.Equal(DayOfWeek.Friday, session2.DayOfWeek);
        Assert.Null(session2.CoachNotes);
        Assert.Single(session2.Exercises);
        Assert.Equal(3, session2.Exercises[0].Sets);
        Assert.Equal(8, session2.Exercises[0].Reps);

        _planRepoMock.Verify(r => r.AddAsync(It.IsAny<TrainingPlan>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreatePlan_DadoEjercicioInexistente_DeberiaLanzarNotFoundException()
    {
        var exerciseId = Guid.NewGuid();
        _planRepoMock.Setup(r => r.AllExercisesExistAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var request = new SaveTrainingPlanRequest(
            "Fuerza base",
            "Pretemporada",
            new List<TrainingWeekRequest>
            {
                new(
                    1,
                    new List<TrainingSessionRequest>
                    {
                        new("Miércoles", 1, DayOfWeek.Wednesday, null, new List<PrescribedExerciseRequest>
                        {
                            new(exerciseId, 1, 4, 5, 80m)
                        })
                    }
                )
            }
        );

        var handler = new CreateTrainingPlanCommandHandler(_planRepoMock.Object, _unitOfWorkMock.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new CreateTrainingPlanCommand(request)));
    }

    [Fact]
    public async Task UpdatePlan_DadoPlanExistente_DeberiaReemplazarArbolCompletoYGetDevolverArbolNuevo()
    {
        // ARRANGE: Crear plan inicial con 1 semana, 2 sesiones y ejercicios
        var planId = Guid.NewGuid();
        var existingPlan = TrainingPlan.Create("Fuerza base", "Pretemporada");
        var initialWeek = TrainingWeek.Create(1, planId);
        var initialSession1 = TrainingSession.Create(initialWeek.Id, "Miércoles", 1, DayOfWeek.Wednesday, "Nota inicial");
        initialSession1.AddPrescribedExercise(PrescribedExercise.Create(1, 4, 5, 80m, initialSession1.Id, Guid.NewGuid()));
        var initialSession2 = TrainingSession.Create(initialWeek.Id, "Viernes", 2, DayOfWeek.Friday);
        initialSession2.AddPrescribedExercise(PrescribedExercise.Create(1, 3, 8, 70m, initialSession2.Id, Guid.NewGuid()));
        initialWeek.AddSession(initialSession1);
        initialWeek.AddSession(initialSession2);
        existingPlan.AddWeek(initialWeek);

        _planRepoMock.Setup(r => r.GetWithDetailsByIdAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPlan);
        _planRepoMock.Setup(r => r.AllExercisesExistAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Nuevo árbol para reemplazar el anterior (usando las mismas semanas y órdenes de sesión para verificar el reemplazo sin colisión)
        var newExerciseId = Guid.NewGuid();
        var updateRequest = new SaveTrainingPlanRequest(
            "Fuerza Hipertrofia",
            "Fase 2",
            new List<TrainingWeekRequest>
            {
                new(
                    1,
                    new List<TrainingSessionRequest>
                    {
                        new(
                            "Lunes",
                            1,
                            DayOfWeek.Monday,
                            "Controlar tempo",
                            new List<PrescribedExerciseRequest>
                            {
                                new(newExerciseId, 1, 4, 10, 75m)
                            }
                        )
                    }
                )
            }
        );

        var updateHandler = new UpdateTrainingPlanCommandHandler(_planRepoMock.Object, _unitOfWorkMock.Object);
        var getHandler = new GetTrainingPlanQueryHandler(_planRepoMock.Object);

        // ACT: Ejecutar PUT (Update) y luego GET
        var updatedResult = await updateHandler.Handle(new UpdateTrainingPlanCommand(planId, updateRequest));
        var getResult = await getHandler.Handle(new GetTrainingPlanQuery(planId));

        // ASSERT: Verificar que se guardó y que el GET refleja exactamente el árbol nuevo
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal("Fuerza Hipertrofia", updatedResult.Name);
        Assert.Equal("Fase 2", updatedResult.Objective);
        Assert.Single(updatedResult.Weeks);
        Assert.Equal(1, updatedResult.Weeks[0].WeekNumber);
        Assert.Single(updatedResult.Weeks[0].Sessions);
        Assert.Equal("Lunes", updatedResult.Weeks[0].Sessions[0].Name);
        Assert.Equal(1, updatedResult.Weeks[0].Sessions[0].Order);
        Assert.Equal(DayOfWeek.Monday, updatedResult.Weeks[0].Sessions[0].DayOfWeek);
        Assert.Equal("Controlar tempo", updatedResult.Weeks[0].Sessions[0].CoachNotes);
        Assert.Single(updatedResult.Weeks[0].Sessions[0].Exercises);
        Assert.Equal(10, updatedResult.Weeks[0].Sessions[0].Exercises[0].Reps);

        Assert.Equal("Fuerza Hipertrofia", getResult.Name);
        Assert.Equal("Fase 2", getResult.Objective);
        Assert.Single(getResult.Weeks);
        Assert.Equal(1, getResult.Weeks[0].WeekNumber);
        Assert.Single(getResult.Weeks[0].Sessions);
        Assert.Equal("Lunes", getResult.Weeks[0].Sessions[0].Name);
        Assert.Equal(1, getResult.Weeks[0].Sessions[0].Order);
        Assert.Equal(DayOfWeek.Monday, getResult.Weeks[0].Sessions[0].DayOfWeek);
        Assert.Equal("Controlar tempo", getResult.Weeks[0].Sessions[0].CoachNotes);
    }

    [Fact]
    public async Task UpdatePlan_DadoPlanExistente_CuandoSeEdita_DeberiaPreservarIdsDeSemanasSesionesYEjercicios()
    {
        // ARRANGE: Crear plan inicial con 1 semana, 1 sesión y 1 ejercicio
        var planId = Guid.NewGuid();
        var existingPlan = TrainingPlan.Create("Fuerza base", "Pretemporada");
        var initialWeek = TrainingWeek.Create(1, planId);
        var initialSession = TrainingSession.Create(initialWeek.Id, "Miércoles", 1, DayOfWeek.Wednesday, "Nota inicial");
        var initialExercise = PrescribedExercise.Create(1, 4, 5, 80m, initialSession.Id, Guid.NewGuid());
        initialSession.AddPrescribedExercise(initialExercise);
        initialWeek.AddSession(initialSession);
        existingPlan.AddWeek(initialWeek);

        var originalWeekId = initialWeek.Id;
        var originalSessionId = initialSession.Id;
        var originalExerciseId = initialExercise.Id;

        _planRepoMock.Setup(r => r.GetWithDetailsByIdAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPlan);
        _planRepoMock.Setup(r => r.AllExercisesExistAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // ACT: PF edita el plan cambiando notas y repeticiones, y agregando una segunda sesión
        var newCatalogExerciseId = Guid.NewGuid();
        var updateRequest = new SaveTrainingPlanRequest(
            "Fuerza base editada",
            "Nuevo objetivo",
            new List<TrainingWeekRequest>
            {
                new(
                    1,
                    new List<TrainingSessionRequest>
                    {
                        // Sesión existente modificada (mismo orden 1)
                        new(
                            "Miércoles Renombrado",
                            1,
                            DayOfWeek.Wednesday,
                            "Nota modificada",
                            new List<PrescribedExerciseRequest>
                            {
                                new(initialExercise.ExerciseId, 1, 5, 8, 85m)
                            }
                        ),
                        // Nueva sesión agregada (orden 2)
                        new(
                            "Viernes",
                            2,
                            DayOfWeek.Friday,
                            null,
                            new List<PrescribedExerciseRequest>
                            {
                                new(newCatalogExerciseId, 1, 3, 10, 70m)
                            }
                        )
                    }
                )
            }
        );

        var handler = new UpdateTrainingPlanCommandHandler(_planRepoMock.Object, _unitOfWorkMock.Object);
        var result = await handler.Handle(new UpdateTrainingPlanCommand(planId, updateRequest));

        // ASSERT: Los IDs originales de semana, sesión y ejercicio deben mantenerse idénticos
        Assert.Single(result.Weeks);
        Assert.Equal(originalWeekId, result.Weeks[0].Id);

        Assert.Equal(2, result.Weeks[0].Sessions.Count);

        // Sesión 1: Mismo ID que antes
        var session1 = result.Weeks[0].Sessions[0];
        Assert.Equal(originalSessionId, session1.Id);
        Assert.Equal("Miércoles Renombrado", session1.Name);
        Assert.Equal("Nota modificada", session1.CoachNotes);
        Assert.Single(session1.Exercises);
        Assert.Equal(originalExerciseId, session1.Exercises[0].Id);
        Assert.Equal(5, session1.Exercises[0].Sets);
        Assert.Equal(8, session1.Exercises[0].Reps);

        // Sesión 2: Es nueva, tiene un ID nuevo diferente a la sesión 1
        var session2 = result.Weeks[0].Sessions[1];
        Assert.NotEqual(Guid.Empty, session2.Id);
        Assert.NotEqual(originalSessionId, session2.Id);
        Assert.Single(session2.Exercises);
        Assert.NotEqual(Guid.Empty, session2.Exercises[0].Id);
        Assert.NotEqual(originalExerciseId, session2.Exercises[0].Id);
    }

    [Fact]
    public async Task UpdatePlan_DadoPlanExistente_CuandoSeEnvianIdsExplicitos_DeberiaEmparejarPorId()
    {
        var planId = Guid.NewGuid();
        var existingPlan = TrainingPlan.Create("Fuerza base", "Pretemporada");
        var initialWeek = TrainingWeek.Create(1, planId);
        var initialSession = TrainingSession.Create(initialWeek.Id, "Miércoles", 1, DayOfWeek.Wednesday);
        var initialExercise = PrescribedExercise.Create(1, 4, 5, 80m, initialSession.Id, Guid.NewGuid());
        initialSession.AddPrescribedExercise(initialExercise);
        initialWeek.AddSession(initialSession);
        existingPlan.AddWeek(initialWeek);

        _planRepoMock.Setup(r => r.GetWithDetailsByIdAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPlan);
        _planRepoMock.Setup(r => r.AllExercisesExistAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Enviar request con Id explícito
        var updateRequest = new SaveTrainingPlanRequest(
            "Plan con Id",
            null,
            new List<TrainingWeekRequest>
            {
                new(
                    1,
                    new List<TrainingSessionRequest>
                    {
                        new(
                            "Miércoles",
                            1,
                            DayOfWeek.Wednesday,
                            "Notas con Id",
                            new List<PrescribedExerciseRequest>
                            {
                                new(initialExercise.ExerciseId, 1, 3, 12, 60m, initialExercise.Id)
                            },
                            initialSession.Id
                        )
                    },
                    initialWeek.Id
                )
            }
        );

        var handler = new UpdateTrainingPlanCommandHandler(_planRepoMock.Object, _unitOfWorkMock.Object);
        var result = await handler.Handle(new UpdateTrainingPlanCommand(planId, updateRequest));

        Assert.Equal(initialWeek.Id, result.Weeks[0].Id);
        Assert.Equal(initialSession.Id, result.Weeks[0].Sessions[0].Id);
        Assert.Equal(initialExercise.Id, result.Weeks[0].Sessions[0].Exercises[0].Id);
        Assert.Equal(12, result.Weeks[0].Sessions[0].Exercises[0].Reps);
    }

    [Fact]
    public async Task UpdatePlan_DadoPlanInexistente_DeberiaLanzarNotFoundException()
    {
        var planId = Guid.NewGuid();
        _planRepoMock.Setup(r => r.GetWithDetailsByIdAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrainingPlan?)null);

        var handler = new UpdateTrainingPlanCommandHandler(_planRepoMock.Object, _unitOfWorkMock.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new UpdateTrainingPlanCommand(planId, new SaveTrainingPlanRequest("Plan"))));
    }

    [Fact]
    public async Task GetPlan_DadoPlanInexistente_DeberiaLanzarNotFoundException()
    {
        var planId = Guid.NewGuid();
        _planRepoMock.Setup(r => r.GetWithDetailsByIdAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrainingPlan?)null);

        var handler = new GetTrainingPlanQueryHandler(_planRepoMock.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetTrainingPlanQuery(planId)));
    }

    [Fact]
    public async Task ListPlans_DeberiaDevolverListaDePlanesDelPf()
    {
        var plans = new List<TrainingPlan>
        {
            TrainingPlan.Create("Plan 1", "Obj 1"),
            TrainingPlan.Create("Plan 2", "Obj 2")
        };

        _planRepoMock.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(plans);

        var handler = new ListTrainingPlansQueryHandler(_planRepoMock.Object);

        var result = await handler.Handle(new ListTrainingPlansQuery());

        Assert.Equal(2, result.Count);
        Assert.Equal("Plan 1", result[0].Name);
        Assert.Equal("Plan 2", result[1].Name);
    }

    [Fact]
    public void ValidacionesDominio_NombreVacioOSemanasRepetidas_DeberiaLanzarDomainException()
    {
        Assert.Throws<DomainException>(() => TrainingPlan.Create("   "));
        Assert.Throws<DomainException>(() => TrainingWeek.Create(0, Guid.NewGuid()));
        Assert.Throws<DomainException>(() => TrainingSession.Create(Guid.NewGuid(), "", 1, DayOfWeek.Monday));
        Assert.Throws<DomainException>(() => TrainingSession.Create(Guid.NewGuid(), "Sesion", 0, DayOfWeek.Monday));
        Assert.Throws<DomainException>(() => PrescribedExercise.Create(0, 4, 10, 75m, Guid.NewGuid(), Guid.NewGuid()));
        Assert.Throws<DomainException>(() => PrescribedExercise.Create(1, 0, 10, 75m, Guid.NewGuid(), Guid.NewGuid()));
        Assert.Throws<DomainException>(() => PrescribedExercise.Create(1, 4, 0, 75m, Guid.NewGuid(), Guid.NewGuid()));

        var plan = TrainingPlan.Create("Plan");
        plan.AddWeek(TrainingWeek.Create(1, plan.Id));
        Assert.Throws<DomainException>(() => plan.AddWeek(TrainingWeek.Create(1, plan.Id)));

        var week = TrainingWeek.Create(1, plan.Id);
        week.AddSession(TrainingSession.Create(week.Id, "S1", 1, DayOfWeek.Monday));
        Assert.Throws<DomainException>(() => week.AddSession(TrainingSession.Create(week.Id, "S2", 1, DayOfWeek.Wednesday)));

        var session = TrainingSession.Create(week.Id, "S1", 1, DayOfWeek.Monday);
        session.AddPrescribedExercise(PrescribedExercise.Create(1, 4, 8, null, session.Id, Guid.NewGuid()));
        Assert.Throws<DomainException>(() => session.AddPrescribedExercise(PrescribedExercise.Create(1, 3, 10, null, session.Id, Guid.NewGuid())));
    }
}
