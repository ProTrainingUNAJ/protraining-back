using System.Text.Json;
using Application.DTOs.Memberships;
using Application.DTOs.TrainingPlans;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Routines.GetTodayRoutine;
using Application.UseCases.TrainingPlans;
using Application.UseCases.TrainingPlans.AssignPlan;
using Application.UseCases.TrainingPlans.GetSchedule;
using Application.UseCases.TrainingPlans.ListAssignments;
using Domain.Entities;
using Domain.Services;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Entrenamiento.Tests;

// KAN-558: hora de la sesión y fecha de fin (con estado) de la asignación.
public class SessionStartTimeAndAssignmentEndTests
{
    private static readonly TimeOnly SixPm = new(18, 0);

    private sealed class TestCurrentUser(Guid pfId) : ICurrentUser
    {
        public Guid? PfId => pfId;
    }

    private static AppDbContext CreateContext(Guid pfId, string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options, new TestCurrentUser(pfId));

    // Plan de N semanas con una sesión el lunes de cada semana
    private static TrainingPlan CreatePlan(int weeks, TimeOnly? startTime = null)
    {
        var plan = TrainingPlan.Create("Fuerza base", null);
        for (var number = 1; number <= weeks; number++)
        {
            var week = TrainingWeek.Create(number, plan.Id);
            week.AddSession(TrainingSession.Create(week.Id, $"Lunes {number}", 1, DayOfWeek.Monday, startTime: startTime));
            plan.AddWeek(week);
        }
        return plan;
    }

    // --- Fecha de fin y estado ---

    [Fact]
    public void Given_FourWeekPlanFromDecemberFirst_When_CalculatingEndDate_Then_IsDecember28()
    {
        var endDate = PlanAssignment.CalculateEndDate(new DateOnly(2026, 12, 1), weekCount: 4);

        Assert.Equal(new DateOnly(2026, 12, 28), endDate);
    }

    [Fact]
    public void Given_EndDate_When_TodayIsTheLastDay_Then_IsActive_AndTheNextDayIsFinished()
    {
        var endDate = new DateOnly(2026, 12, 28);

        Assert.Equal(PlanAssignmentStatus.Active, PlanAssignment.CalculateStatus(endDate, new DateOnly(2026, 12, 28)));
        Assert.Equal(PlanAssignmentStatus.Finished, PlanAssignment.CalculateStatus(endDate, new DateOnly(2026, 12, 29)));
    }

    [Fact]
    public void Given_PlanWithWeeksOneAndThree_When_CountingWeeks_Then_UsesTheLastWeekNumber()
    {
        var plan = TrainingPlan.Create("Con hueco", null);
        plan.AddWeek(TrainingWeek.Create(1, plan.Id));
        plan.AddWeek(TrainingWeek.Create(3, plan.Id));

        Assert.Equal(3, plan.WeekCount);
    }

    [Theory]
    [InlineData(2020, 1, 6, PlanAssignmentStatus.Finished)]
    [InlineData(2099, 1, 5, PlanAssignmentStatus.Active)]
    public async Task Given_FourWeekPlan_When_Assigning_Then_ReturnsEndDateAndStatus(
        int year, int month, int day, PlanAssignmentStatus expectedStatus)
    {
        var plan = CreatePlan(weeks: 4);
        var categoryId = Guid.NewGuid();
        var startDate = new DateOnly(year, month, day);
        var plans = new Mock<ITrainingPlanRepository>();
        plans.Setup(repo => repo.ExistsAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        plans.Setup(repo => repo.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        var structure = new Mock<IStructureClient>();
        structure.Setup(client => client.IsOwnCategoryAsync(categoryId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var handler = new AssignPlanCommandHandler(plans.Object, Mock.Of<IUnitOfWork>(), structure.Object);

        var result = await handler.Handle(new AssignPlanCommand(plan.Id, startDate, categoryId, null));

        Assert.Equal(startDate, result.StartDate);
        Assert.Equal(startDate.AddDays(27), result.EndDate);
        Assert.Equal(expectedStatus, result.Status);
    }

    // --- Hora de la sesión ---

    [Fact]
    public async Task Given_SessionsWithAndWithoutTime_When_SavedAndReadBack_Then_KeepsTheTimeAndNullStillWorks()
    {
        var pfId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var plan = TrainingPlan.Create("Mixto", null);
        var week = TrainingWeek.Create(1, plan.Id);
        week.AddSession(TrainingSession.Create(week.Id, "Con hora", 1, DayOfWeek.Monday, startTime: SixPm));
        week.AddSession(TrainingSession.Create(week.Id, "Sin hora", 2, DayOfWeek.Wednesday));
        plan.AddWeek(week);

        await using (var context = CreateContext(pfId, dbName))
        {
            await context.TrainingPlans.AddAsync(plan);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(pfId, dbName))
        {
            var saved = await new TrainingPlanRepository(context).GetWithDetailsByIdAsync(plan.Id);
            var dto = TrainingPlanMapper.ToDetailDto(saved!, new Dictionary<Guid, string>());
            var sessions = dto.Weeks.Single().Sessions;

            Assert.Equal(SixPm, sessions.Single(s => s.Name == "Con hora").StartTime);
            Assert.Null(sessions.Single(s => s.Name == "Sin hora").StartTime);
        }
    }

    [Fact]
    public void Given_SessionWithTime_When_UpdatedWithoutTime_Then_TheTimeIsCleared()
    {
        var session = TrainingSession.Create(Guid.NewGuid(), "Lunes", 1, DayOfWeek.Monday, startTime: SixPm);

        session.Update("Lunes", 1, DayOfWeek.Monday, null, startTime: null);

        Assert.Null(session.StartTime);
    }

    [Fact]
    public void Given_PlanWithTimedSession_When_ClonedAsTemplate_Then_TheCopyKeepsTheTime()
    {
        var plan = CreatePlan(weeks: 1, startTime: SixPm);

        var clone = plan.CloneAs("Plantilla", isTemplate: true);

        Assert.Equal(SixPm, clone.Weeks.Single().Sessions.Single().StartTime);
    }

    [Fact]
    public void Given_SessionDtoWithTime_When_SerializedForTheApi_Then_StartTimeIsHoursMinutesSeconds()
    {
        var dto = new TrainingPlanSessionDto(Guid.NewGuid(), "Lunes", 1, DayOfWeek.Monday, null, [], SixPm);

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // Mismo formato que la hora de los partidos de Estructura
        Assert.Contains("\"startTime\":\"18:00:00\"", json);
    }

    [Fact]
    public async Task Given_TodaysSessionWithTime_When_AthleteAsksForTheRoutine_Then_ReturnsTheTime()
    {
        var athleteId = Guid.NewGuid();
        var session = TrainingSession.Create(Guid.NewGuid(), "Lunes", 1, DayOfWeek.Monday, startTime: SixPm);
        var sessions = new Mock<ITrainingSessionRepository>();
        sessions.Setup(repo => repo.GetByAthleteIdAsync(athleteId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        var structure = new Mock<IStructureClient>();
        structure.Setup(client => client.GetAthleteByIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureAthleteDto(athleteId, "Lucas Gómez"));
        var handler = new GetTodayRoutineQueryHandler(
            Mock.Of<IAthleteRecordRepository>(),
            sessions.Object,
            new LoadCalculatorEngine(),
            structure.Object,
            Mock.Of<IExerciseVideoRepository>(),
            Mock.Of<IPerformedSetRepository>());

        var result = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athleteId });

        Assert.Equal(SixPm, result.StartTime);
    }

    [Fact]
    public async Task Given_SessionsWithAndWithoutTime_When_AskingTheSchedule_Then_TimedFirstInOrder_AndBringsTheEndDate()
    {
        var pfId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var monday = new DateOnly(2026, 11, 2);
        var evening = CreatePlan(weeks: 4, startTime: new TimeOnly(19, 30));
        var morning = CreatePlan(weeks: 4, startTime: new TimeOnly(8, 0));
        var allDay = CreatePlan(weeks: 4);

        await using (var context = CreateContext(pfId, dbName))
        {
            foreach (var plan in new[] { evening, morning, allDay })
            {
                await context.TrainingPlans.AddAsync(plan);
                await context.PlanAssignments.AddAsync(PlanAssignment.Create(plan.Id, monday, categoryId: Guid.NewGuid()));
            }
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(pfId, dbName))
        {
            var schedule = await new GetPlanScheduleQueryHandler(new TrainingSessionRepository(context))
                .Handle(new GetPlanScheduleQuery(monday));

            Assert.Equal(
                [new TimeOnly(8, 0), new TimeOnly(19, 30), null],
                schedule.Select(item => item.StartTime).ToList());
            Assert.All(schedule, item => Assert.Equal(new DateOnly(2026, 11, 29), item.AssignmentEndDate));
        }
    }

    // --- Listado de asignaciones ---

    [Fact]
    public async Task Given_FinishedAndActiveAssignments_When_Listing_Then_ActiveFirst_WithEndDate_AndFilterByStatusWorks()
    {
        var pfId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var today = new DateOnly(2026, 12, 10);
        var finishedPlan = CreatePlan(weeks: 2);   // del 2/11 al 15/11 → terminada
        var activePlan = CreatePlan(weeks: 4);     // del 1/12 al 28/12 → activa

        await using (var context = CreateContext(pfId, dbName))
        {
            await context.TrainingPlans.AddRangeAsync(finishedPlan, activePlan);
            await context.PlanAssignments.AddAsync(PlanAssignment.Create(finishedPlan.Id, new DateOnly(2026, 11, 2), categoryId: Guid.NewGuid()));
            await context.PlanAssignments.AddAsync(PlanAssignment.Create(activePlan.Id, new DateOnly(2026, 12, 1), athleteId: Guid.NewGuid()));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(pfId, dbName))
        {
            var handler = new ListPlanAssignmentsQueryHandler(new TrainingSessionRepository(context));

            var all = await handler.Handle(new ListPlanAssignmentsQuery(Today: today));
            Assert.Equal(["Active", "Finished"], all.Select(item => item.Status).ToList());
            Assert.Equal(new DateOnly(2026, 12, 28), all[0].EndDate);
            Assert.Equal(new DateOnly(2026, 11, 15), all[1].EndDate);

            var onlyFinished = await handler.Handle(new ListPlanAssignmentsQuery(PlanAssignmentStatus.Finished, today));
            Assert.Equal(finishedPlan.Id, Assert.Single(onlyFinished).TrainingPlanId);
        }
    }

    [Fact]
    public async Task Given_AssignmentsOfAnotherPf_When_Listing_Then_ReturnsNothing()
    {
        var dbName = Guid.NewGuid().ToString();
        var plan = CreatePlan(weeks: 1);

        await using (var context = CreateContext(Guid.NewGuid(), dbName))
        {
            await context.TrainingPlans.AddAsync(plan);
            await context.PlanAssignments.AddAsync(PlanAssignment.Create(plan.Id, new DateOnly(2026, 12, 1), categoryId: Guid.NewGuid()));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(Guid.NewGuid(), dbName))
        {
            var result = await new ListPlanAssignmentsQueryHandler(new TrainingSessionRepository(context))
                .Handle(new ListPlanAssignmentsQuery());

            Assert.Empty(result);
        }
    }
}
