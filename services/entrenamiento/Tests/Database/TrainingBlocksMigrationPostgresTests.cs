using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;
using Testcontainers.PostgreSql;
using Xunit;

namespace Entrenamiento.Tests.Database;

[Trait("Category", "RequiresDocker")]
public sealed class TrainingBlocksMigrationPostgresTests
{
    private const string PostgresImage = "postgres:16-alpine";

    [Fact]
    public async Task Given_RealPostgreSql_When_MigratingPreExistingSessions_Then_ExercisesAreBackfilledIntoPrincipalBlockWithoutDataLoss()
    {
        await using var postgresContainer = new PostgreSqlBuilder()
            .WithImage(PostgresImage)
            .Build();
        await postgresContainer.StartAsync();

        var connectionString = postgresContainer.GetConnectionString();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var pfId = Guid.NewGuid();
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(u => u.PfId).Returns(pfId);

        var planId = Guid.NewGuid();
        var weekId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var pe1Id = Guid.NewGuid();
        var pe2Id = Guid.NewGuid();

        var exercise1 = Exercise.Create("Press de Banca", "Pecho");
        var exercise2 = Exercise.Create("Remo con Barra", "Espalda");

        // 1. Aplicamos las migraciones solo hasta ANTES de AddTrainingBlocksAndWeeklyProgression
        await using (var preMigrationContext = new AppDbContext(options, currentUserMock.Object))
        {
            var migrator = preMigrationContext.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20261118220837_AddCurrentRecordIdToRecordProposals");

            // Insertamos ejercicios con EF Core para poblar limpiamente todas las columnas NOT NULL (arrays de músculos, origin, etc.)
            await preMigrationContext.Exercises.AddRangeAsync(exercise1, exercise2);
            await preMigrationContext.SaveChangesAsync();

            // 2. Insertamos datos pre-existentes (plan, semana, sesión y ejercicios directos a sesión)
            // utilizando SQL directo para simular fielmente el estado pre-migración de la base (sin training_block_id)
            await using var connection = (Npgsql.NpgsqlConnection)preMigrationContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $@"
                INSERT INTO training_plans (id, pf_id, name, objective, is_template, created_at)
                VALUES ('{planId}', '{pfId}', 'Plan Pre-Migración', 'Objetivo Hipertrofia', false, NOW());

                INSERT INTO training_weeks (id, pf_id, training_plan_id, week_number, created_at)
                VALUES ('{weekId}', '{pfId}', '{planId}', 1, NOW());

                INSERT INTO training_sessions (id, pf_id, training_week_id, name, order_index, day_of_week, created_at)
                VALUES ('{sessionId}', '{pfId}', '{weekId}', 'Lunes Torso', 1, 1, NOW());

                INSERT INTO prescribed_exercises (id, pf_id, training_session_id, exercise_id, order_index, sets, reps, target_percentage, created_at)
                VALUES 
                    ('{pe1Id}', '{pfId}', '{sessionId}', '{exercise1.Id}', 1, 4, 8, 80.0, NOW()),
                    ('{pe2Id}', '{pfId}', '{sessionId}', '{exercise2.Id}', 2, 3, 10, 70.0, NOW());
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Ahora aplicamos todas las migraciones restantes (incluyendo 20261119024945_AddTrainingBlocksAndWeeklyProgression y su backfill SQL)
        await using (var postMigrationContext = new AppDbContext(options, currentUserMock.Object))
        {
            await postMigrationContext.Database.MigrateAsync();

            // 4. Verificamos que la migración y el backfill se ejecutaron limpiamente sin pérdida de datos
            var blocks = await postMigrationContext.TrainingBlocks
                .Where(b => b.TrainingSessionId == sessionId)
                .ToListAsync();

            Assert.Single(blocks);
            var principalBlock = blocks[0];
            Assert.Equal("Principal", principalBlock.Title);
            Assert.Equal(1, principalBlock.Order);
            Assert.Equal(pfId, principalBlock.PfId);

            var exercises = await postMigrationContext.PrescribedExercises
                .Where(pe => pe.TrainingBlockId == principalBlock.Id)
                .OrderBy(pe => pe.Order)
                .ToListAsync();

            Assert.Equal(2, exercises.Count);

            // Ejercicio 1: Se preservan valores exactos y se mapea al bloque Principal
            Assert.Equal(1, exercises[0].Order);
            Assert.Equal(4, exercises[0].Sets);
            Assert.Equal("8", exercises[0].Reps);
            Assert.Equal(80.0m, exercises[0].TargetPercentage);
            Assert.Equal(exercise1.Id, exercises[0].ExerciseId);
            Assert.Equal(principalBlock.Id, exercises[0].TrainingBlockId);
            Assert.Equal(sessionId, exercises[0].TrainingSessionId);

            // Ejercicio 2: Se preservan valores exactos y se mapea al bloque Principal
            Assert.Equal(2, exercises[1].Order);
            Assert.Equal(3, exercises[1].Sets);
            Assert.Equal("10", exercises[1].Reps);
            Assert.Equal(70.0m, exercises[1].TargetPercentage);
            Assert.Equal(exercise2.Id, exercises[1].ExerciseId);
            Assert.Equal(principalBlock.Id, exercises[1].TrainingBlockId);
            Assert.Equal(sessionId, exercises[1].TrainingSessionId);
        }
    }
}
