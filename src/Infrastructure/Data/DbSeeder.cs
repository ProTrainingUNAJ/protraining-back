using Microsoft.EntityFrameworkCore;
using ProTraining.Domain.Entities;

namespace ProTraining.Infrastructure.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        // Solo sembrar si no hay cuentas
        if (await db.Accounts.IgnoreQueryFilters().AnyAsync())
            return;

        var pfAccount = new Account
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Email = "pf@protraining.com",
            FullName = "Prof. Martín Benítez (PF)",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
            Role = UserRole.PF,
            CreatedAt = DateTime.UtcNow
        };

        var atletaAccount = new Account
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Email = "atleta@protraining.com",
            FullName = "Lucas Fernández (Atleta)",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
            Role = UserRole.Athlete,
            CreatedAt = DateTime.UtcNow
        };

        db.Accounts.AddRange(pfAccount, atletaAccount);

        // Organizaciones del PF
        var clubQuilmes = new Organization
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            AccountId = pfAccount.Id,
            Name = "Club Atlético Quilmes",
            Description = "Plantel de Rugby & Hockey"
        };

        var gymSpartacus = new Organization
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            AccountId = pfAccount.Id,
            Name = "Gimnasio Spartacus",
            Description = "Alumnos particulares de fuerza"
        };

        db.Organizations.AddRange(clubQuilmes, gymSpartacus);

        // Categorías
        var catJuveniles = new Category
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            AccountId = pfAccount.Id,
            OrganizationId = clubQuilmes.Id,
            Name = "Juveniles M19"
        };

        var catPrimera = new Category
        {
            Id = Guid.Parse("66666666-6666-6666-6666-666666666666"),
            AccountId = pfAccount.Id,
            OrganizationId = clubQuilmes.Id,
            Name = "Primera División"
        };

        db.Categories.AddRange(catJuveniles, catPrimera);

        // Atleta asignado a 2 categorías (CU-02 de la propuesta)
        var atletaLucas = new Athlete
        {
            Id = Guid.Parse("77777777-7777-7777-7777-777777777777"),
            AccountId = pfAccount.Id,
            UserId = atletaAccount.Id,
            FirstName = "Lucas",
            LastName = "Fernández",
            Email = "atleta@protraining.com",
            Phone = "+54 9 11 4455-6677",
            MedicalConditions = "Sin antecedentes cardíacos. Alergia al polen.",
            EmergencyContact = "Mamá: 11-9988-7766",
            ConsentGiven = true
        };

        db.Athletes.Add(atletaLucas);

        db.AthleteCategories.AddRange(
            new AthleteCategory { AthleteId = atletaLucas.Id, CategoryId = catJuveniles.Id, AccountId = pfAccount.Id },
            new AthleteCategory { AthleteId = atletaLucas.Id, CategoryId = catPrimera.Id, AccountId = pfAccount.Id }
        );

        // Ejercicios de fuerza básica
        var exBench = new Exercise { Id = Guid.Parse("88888888-8888-8888-8888-888888888888"), Name = "Press de Banco Plano", MuscleGroup = "Pectoral / Tren Superior" };
        var exSquat = new Exercise { Id = Guid.Parse("99999999-9999-9999-9999-999999999999"), Name = "Sentadilla Trasera con Barra", MuscleGroup = "Piernas / Tren Inferior" };
        var exDeadlift = new Exercise { Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Name = "Peso Muerto Convencional", MuscleGroup = "Espalda / Cadena Posterior" };

        db.Exercises.AddRange(exBench, exSquat, exDeadlift);

        // Récord personal 1RM inicial: 60 kg en banco plano (según ejemplo de CU-04/CU-05) y 100 kg en sentadilla
        db.PersonalRecords.AddRange(
            new PersonalRecord
            {
                AccountId = pfAccount.Id,
                AthleteId = atletaLucas.Id,
                ExerciseId = exBench.Id,
                WeightOneRepMax = 60m,
                DateAchieved = DateTime.UtcNow.AddDays(-15),
                IsCurrent = true
            },
            new PersonalRecord
            {
                AccountId = pfAccount.Id,
                AthleteId = atletaLucas.Id,
                ExerciseId = exSquat.Id,
                WeightOneRepMax = 100m,
                DateAchieved = DateTime.UtcNow.AddDays(-20),
                IsCurrent = true
            }
        );

        // Rutina para Juveniles (Día de hoy)
        var rutinaJuveniles = new Routine
        {
            Id = Guid.NewGuid(),
            AccountId = pfAccount.Id,
            Title = "Fuerza Tren Superior - Juveniles",
            CategoryId = catJuveniles.Id,
            DayOfWeek = DateTime.UtcNow.DayOfWeek,
            Items =
            [
                new RoutineItem
                {
                    ExerciseId = exBench.Id,
                    Sets = 4,
                    Reps = 6,
                    Percentage = 80m, // 80% de 60 kg = 48 kg -> redondeado a 47.5 kg
                    Order = 1
                }
            ]
        };

        // Rutina para Primera (Día de hoy)
        var rutinaPrimera = new Routine
        {
            Id = Guid.NewGuid(),
            AccountId = pfAccount.Id,
            Title = "Potencia y Piernas - Primera",
            CategoryId = catPrimera.Id,
            DayOfWeek = DateTime.UtcNow.DayOfWeek,
            Items =
            [
                new RoutineItem
                {
                    ExerciseId = exSquat.Id,
                    Sets = 3,
                    Reps = 5,
                    Percentage = 75m, // 75% de 100 kg = 75 kg
                    Order = 1
                }
            ]
        };

        db.Routines.AddRange(rutinaJuveniles, rutinaPrimera);

        // Check-ins recientes para demostrar histórico
        db.DailyCheckIns.AddRange(
            new DailyCheckIn
            {
                AccountId = pfAccount.Id,
                AthleteId = atletaLucas.Id,
                Date = DateTime.UtcNow.Date.AddDays(-2),
                EffortRate = 9,
                SleepHours = 4.5m,
                MoodRate = 2,
                PainZones = "Rodilla derecha, Isquiotibial"
            },
            new DailyCheckIn
            {
                AccountId = pfAccount.Id,
                AthleteId = atletaLucas.Id,
                Date = DateTime.UtcNow.Date.AddDays(-1),
                EffortRate = 8,
                SleepHours = 5.0m,
                MoodRate = 3,
                PainZones = "Rodilla derecha"
            }
        );

        await db.SaveChangesAsync();
    }
}
