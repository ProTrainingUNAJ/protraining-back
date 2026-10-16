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
        // ARRANGE: Criterio para cerrarla: DADO un PF CUANDO hace POST /api/v1/training-plans con 1 semana, 2 sesiones y sus ejercicios ENTONCES responde 201, y el GET del plan devuelve el mismo árbol con sus ids.
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

        // ACT
        var result = await handler.Handle(new CreateTrainingPlanCommand(request));

        // ASSERT
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
    public async Task UpdatePlan_DadoPlanExistente_DeberiaActualizarArbolCompleto()
    {
        var planId = Guid.NewGuid();
        var existingPlan = TrainingPlan.Create("Plan Viejo", "Objetivo Viejo");
        _planRepoMock.Setup(r => r.GetWithDetailsByIdAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPlan);
        _planRepoMock.Setup(r => r.AllExercisesExistAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new SaveTrainingPlanRequest(
            "Plan Nuevo",
            "Objetivo Nuevo",
            new List<TrainingWeekRequest>
            {
                new(1, new List<TrainingSessionRequest>
                {
                    new("Lunes", 1, DayOfWeek.Monday, "Nota", null)
                })
            }
        );

        var handler = new UpdateTrainingPlanCommandHandler(_planRepoMock.Object, _unitOfWorkMock.Object);

        var result = await handler.Handle(new UpdateTrainingPlanCommand(planId, request));

        Assert.Equal("Plan Nuevo", result.Name);
        Assert.Equal("Objetivo Nuevo", result.Objective);
        Assert.Single(result.Weeks);
        Assert.Equal("Lunes", result.Weeks[0].Sessions[0].Name);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
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
