using Application.Interfaces.Services;
using Application.UseCases.TrainingPlans.ApplyTemplate;
using Application.UseCases.TrainingPlans.AssignPlan;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Entrenamiento.Tests
{
    public class PlanTemplateCloningIntegrityTests : IAsyncLifetime
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _contextOptions;
        private readonly Guid _pfId = Guid.NewGuid();
        private readonly Guid _categoryId = Guid.NewGuid();
        private sealed class TestCurrentUser(Guid pfId) : ICurrentUser
        {
            public Guid? PfId => pfId;
        }
        public PlanTemplateCloningIntegrityTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _contextOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;
        }
        public async Task InitializeAsync()
        {
            await _connection.OpenAsync();
            await using var context = CreateContext();
            await context.Database.EnsureCreatedAsync();
        }
        public async Task DisposeAsync()
        {
            await _connection.DisposeAsync();
        }
        private AppDbContext CreateContext() => new(_contextOptions, new TestCurrentUser(_pfId));
        private (ApplyPlanTemplateCommandHandler Handler, Mock<IStructureClient> StructureMock) CreateHandler(AppDbContext context)
        {
            var planRepo = new TrainingPlanRepository(context);
            var uow = new UnitOfWork(context);
            var structureMock = new Mock<IStructureClient>();
            structureMock
                .Setup(s => s.IsOwnCategoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            var assignHandler = new AssignPlanCommandHandler(planRepo, uow, structureMock.Object);
            var applyHandler = new ApplyPlanTemplateCommandHandler(planRepo, uow, assignHandler);
            return (applyHandler, structureMock);
        }
        // =========================================================================
        // DADO un plan clonado a partir de una plantilla
        // CUANDO se edita un ejercicio en el nuevo plan
        // ENTONCES la plantilla original mantiene sus valores iniciales inalterados
        // =========================================================================
        [Fact]
        public async Task Given_PlanClonedFromTemplate_When_PrescribedExerciseIsModifiedInClonedPlan_Then_OriginalTemplateMaintainsInitialValuesUnaltered()
        {
            // 1. ARRANGE: Crear y persistir la plantilla inicial con su jerarquía completa
            Guid templateId;
            Guid exerciseId;
            await using (var seedContext = CreateContext())
            {
                var exercise = Exercise.Create("Press de Banca", "Pecho");
                await seedContext.Exercises.AddAsync(exercise);
                exerciseId = exercise.Id;
                // Plantilla: 1 semana, 1 sesión, 1 ejercicio con 4 series de 8 reps al 75%
                var template = TrainingPlan.Create("Plantilla Fuerza Hipertrofia", "Ganancia muscular", isTemplate: true);
                var week = TrainingWeek.Create(1, template.Id);
                var session = TrainingSession.Create(week.Id, "Día Torso", 1, DayOfWeek.Monday, "Notas de coach iniciales");
                var prescribedEx = PrescribedExercise.Create(1, 4, 8, 75.0m, session.Id, exercise.Id);
                session.AddPrescribedExercise(prescribedEx);
                week.AddSession(session);
                template.AddWeek(week);
                await seedContext.TrainingPlans.AddAsync(template);
                await seedContext.SaveChangesAsync();
                templateId = template.Id;
            }
            // 2. ACT: Clonar la plantilla a un nuevo plan usando el caso de uso
            Guid assignmentId;
            await using (var actContext = CreateContext())
            {
                var (handler, _) = CreateHandler(actContext);
                var command = new ApplyPlanTemplateCommand(
                    templateId,
                    new DateOnly(2026, 11, 2),
                    _categoryId,
                    null);
                assignmentId = await handler.Handle(command);
            }
            // 3. ACT: Editar el ejercicio prescrito en el nuevo plan clonado
            await using (var editContext = CreateContext())
            {
                var assignment = await editContext.PlanAssignments.FindAsync(assignmentId);
                Assert.NotNull(assignment);
                var clonedPlan = await editContext.TrainingPlans
                    .Include(p => p.Weeks)
                        .ThenInclude(w => w.Sessions)
                            .ThenInclude(s => s.PrescribedExercises)
                    .FirstAsync(p => p.Id == assignment.TrainingPlanId);
                var clonedExercise = clonedPlan.Weeks.First().Sessions.First().PrescribedExercises.First();
                // Modificamos series de 4 a 5, repeticiones de 8 a 12 y porcentaje de 75% a 85%
                clonedExercise.Update(
                    order: clonedExercise.Order,
                    sets: 5,
                    reps: 12,
                    targetPercentage: 85.0m,
                    exerciseId: clonedExercise.ExerciseId);
                await editContext.SaveChangesAsync();
            }
            // 4. ASSERT: Consultar la plantilla original en un nuevo contexto limpio
            await using (var assertContext = CreateContext())
            {
                var originalTemplate = await assertContext.TrainingPlans
                    .Include(p => p.Weeks)
                        .ThenInclude(w => w.Sessions)
                            .ThenInclude(s => s.PrescribedExercises)
                    .FirstAsync(p => p.Id == templateId);
                var templateEx = originalTemplate.Weeks.First().Sessions.First().PrescribedExercises.First();
                // Los valores de la plantilla original DEBEN permanecer inalterados
                Assert.True(originalTemplate.IsTemplate);
                Assert.Equal(4, templateEx.Sets);
                Assert.Equal(8, templateEx.Reps);
                Assert.Equal(75.0m, templateEx.TargetPercentage);
                // Verificamos que el plan clonado sí tenga los valores editados
                var assignment = await assertContext.PlanAssignments.FindAsync(assignmentId);
                var modifiedPlan = await assertContext.TrainingPlans
                    .Include(p => p.Weeks)
                        .ThenInclude(w => w.Sessions)
                            .ThenInclude(s => s.PrescribedExercises)
                    .FirstAsync(p => p.Id == assignment!.TrainingPlanId);
                var modifiedEx = modifiedPlan.Weeks.First().Sessions.First().PrescribedExercises.First();
                Assert.False(modifiedPlan.IsTemplate);
                Assert.Equal(5, modifiedEx.Sets);
                Assert.Equal(12, modifiedEx.Reps);
                Assert.Equal(85.0m, modifiedEx.TargetPercentage);
            }
        }
        // =========================================================================
        // Valida que todos los IDs sean nuevos (sin colisiones de claves primarias)
        // y que todos los datos (series, porcentajes, notas) se copien fielmente.
        // =========================================================================
        [Fact]
        public async Task Given_TemplateWithFullHierarchy_When_ClonedToPlan_Then_AllPrimaryKeysAreUniqueAndValuesPreserved()
        {
            Guid templateId;
            Guid ex1Id, ex2Id;
            await using (var seedContext = CreateContext())
            {
                var ex1 = Exercise.Create("Sentadilla Trasera", "Piernas");
                var ex2 = Exercise.Create("Peso Muerto Rumano", "Piernas");
                await seedContext.Exercises.AddRangeAsync(ex1, ex2);
                ex1Id = ex1.Id;
                ex2Id = ex2.Id;
                var template = TrainingPlan.Create("Plantilla Pierna Completa", "Fuerza básica", isTemplate: true);
                // Semana 1
                var week1 = TrainingWeek.Create(1, template.Id);
                var session1 = TrainingSession.Create(week1.Id, "Día Pierna Pesado", 1, DayOfWeek.Tuesday, "Calentar 10 min movilidad");
                session1.AddPrescribedExercise(PrescribedExercise.Create(1, 4, 6, 80.0m, session1.Id, ex1.Id));
                session1.AddPrescribedExercise(PrescribedExercise.Create(2, 3, 10, 65.0m, session1.Id, ex2.Id));
                week1.AddSession(session1);
                template.AddWeek(week1);
                // Semana 2
                var week2 = TrainingWeek.Create(2, template.Id);
                var session2 = TrainingSession.Create(week2.Id, "Día Pierna Volumen", 1, DayOfWeek.Friday, "Pausa de 2 seg abajo");
                session2.AddPrescribedExercise(PrescribedExercise.Create(1, 3, 12, 60.0m, session2.Id, ex1.Id));
                week2.AddSession(session2);
                template.AddWeek(week2);
                await seedContext.TrainingPlans.AddAsync(template);
                await seedContext.SaveChangesAsync();
                templateId = template.Id;
            }
            // Clonar
            Guid assignmentId;
            await using (var actContext = CreateContext())
            {
                var (handler, _) = CreateHandler(actContext);
                var command = new ApplyPlanTemplateCommand(
                    templateId,
                    new DateOnly(2026, 11, 2),
                    _categoryId,
                    null);
                assignmentId = await handler.Handle(command);
            }
            // Assert
            await using (var assertContext = CreateContext())
            {
                var template = await assertContext.TrainingPlans
                    .Include(p => p.Weeks)
                        .ThenInclude(w => w.Sessions)
                            .ThenInclude(s => s.PrescribedExercises)
                    .FirstAsync(p => p.Id == templateId);
                var assignment = await assertContext.PlanAssignments.FindAsync(assignmentId);
                var clonedPlan = await assertContext.TrainingPlans
                    .Include(p => p.Weeks)
                        .ThenInclude(w => w.Sessions)
                            .ThenInclude(s => s.PrescribedExercises)
                    .FirstAsync(p => p.Id == assignment!.TrainingPlanId);
                // 1. Claves primarias únicas del plan
                Assert.NotEqual(template.Id, clonedPlan.Id);
                Assert.False(clonedPlan.IsTemplate);
                // 2. Semanas: IDs distintos y FKs correctas
                Assert.Equal(template.Weeks.Count, clonedPlan.Weeks.Count);
                foreach (var templateWeek in template.Weeks)
                {
                    var matchingClonedWeek = clonedPlan.Weeks.FirstOrDefault(w => w.WeekNumber == templateWeek.WeekNumber);
                    Assert.NotNull(matchingClonedWeek);
                    Assert.NotEqual(templateWeek.Id, matchingClonedWeek.Id);
                    Assert.Equal(clonedPlan.Id, matchingClonedWeek.TrainingPlanId);
                    // 3. Sesiones: IDs distintos, FKs y CoachNotes preservadas
                    Assert.Equal(templateWeek.Sessions.Count, matchingClonedWeek.Sessions.Count);
                    foreach (var templateSession in templateWeek.Sessions)
                    {
                        var matchingClonedSession = matchingClonedWeek.Sessions.FirstOrDefault(s => s.Order == templateSession.Order);
                        Assert.NotNull(matchingClonedSession);
                        Assert.NotEqual(templateSession.Id, matchingClonedSession.Id);
                        Assert.Equal(matchingClonedWeek.Id, matchingClonedSession.TrainingWeekId);
                        Assert.Equal(templateSession.CoachNotes, matchingClonedSession.CoachNotes);
                        // 4. Ejercicios prescritos: IDs distintos, FKs, series, reps y targetPercentage preservados
                        Assert.Equal(templateSession.PrescribedExercises.Count, matchingClonedSession.PrescribedExercises.Count);
                        foreach (var templateEx in templateSession.PrescribedExercises)
                        {
                            var matchingClonedEx = matchingClonedSession.PrescribedExercises.FirstOrDefault(e => e.Order == templateEx.Order);
                            Assert.NotNull(matchingClonedEx);
                            Assert.NotEqual(templateEx.Id, matchingClonedEx.Id);
                            Assert.Equal(matchingClonedSession.Id, matchingClonedEx.TrainingSessionId);
                            Assert.Equal(templateEx.ExerciseId, matchingClonedEx.ExerciseId);
                            Assert.Equal(templateEx.Sets, matchingClonedEx.Sets);
                            Assert.Equal(templateEx.Reps, matchingClonedEx.Reps);
                            Assert.Equal(templateEx.TargetPercentage, matchingClonedEx.TargetPercentage);
                        }
                    }
                }
            }
        }
        // =========================================================================
        // Validar que al cambiar notas del entrenador en una sesión del plan clonado,
        // la plantilla mantenga sus notas iniciales.
        // =========================================================================
        [Fact]
        public async Task Given_PlanClonedFromTemplate_When_SessionCoachNotesModifiedInClonedPlan_Then_TemplateNotesRemainUnaltered()
        {
            Guid templateId;
            await using (var seedContext = CreateContext())
            {
                var exercise = Exercise.Create("Dominadas", "Espalda");
                await seedContext.Exercises.AddAsync(exercise);
                var template = TrainingPlan.Create("Plantilla Tracción", "Espalda y Bíceps", isTemplate: true);
                var week = TrainingWeek.Create(1, template.Id);
                var session = TrainingSession.Create(week.Id, "Día Jalón", 1, DayOfWeek.Wednesday, "Nota original de la plantilla");
                session.AddPrescribedExercise(PrescribedExercise.Create(1, 4, 8, 80m, session.Id, exercise.Id));
                week.AddSession(session);
                template.AddWeek(week);
                await seedContext.TrainingPlans.AddAsync(template);
                await seedContext.SaveChangesAsync();
                templateId = template.Id;
            }
            Guid assignmentId;
            await using (var actContext = CreateContext())
            {
                var (handler, _) = CreateHandler(actContext);
                var command = new ApplyPlanTemplateCommand(templateId, new DateOnly(2026, 11, 2), _categoryId, null);
                assignmentId = await handler.Handle(command);
            }
            // Modificamos las notas del coach solo en el plan clonado
            await using (var editContext = CreateContext())
            {
                var assignment = await editContext.PlanAssignments.FindAsync(assignmentId);
                var clonedPlan = await editContext.TrainingPlans
                    .Include(p => p.Weeks)
                        .ThenInclude(w => w.Sessions)
                    .FirstAsync(p => p.Id == assignment!.TrainingPlanId);
                var clonedSession = clonedPlan.Weeks.First().Sessions.First();
                clonedSession.UpdateCoachNotes("Nota personalizada para el atleta");
                await editContext.SaveChangesAsync();
            }
            // Verificamos que la plantilla mantenga la nota original
            await using (var assertContext = CreateContext())
            {
                var template = await assertContext.TrainingPlans
                    .Include(p => p.Weeks)
                        .ThenInclude(w => w.Sessions)
                    .FirstAsync(p => p.Id == templateId);
                Assert.Equal("Nota original de la plantilla", template.Weeks.First().Sessions.First().CoachNotes);
            }
        }
    }
}