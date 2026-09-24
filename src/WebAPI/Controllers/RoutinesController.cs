using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProTraining.Application.Common;
using ProTraining.Application.DTOs;
using ProTraining.Common;
using ProTraining.Domain.Entities;
using ProTraining.Domain.Services;

namespace ProTraining.WebAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class RoutinesController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public RoutinesController(IAppDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet("exercises")]
    public async Task<ActionResult<List<ExerciseDto>>> GetExercises()
    {
        var exercises = await _db.Exercises
            .OrderBy(e => e.MuscleGroup)
            .ThenBy(e => e.Name)
            .Select(e => new ExerciseDto(e.Id, e.Name, e.MuscleGroup, e.Description))
            .ToListAsync();

        return Ok(exercises);
    }

    [HttpGet("records/{athleteId}")]
    public async Task<ActionResult<List<PersonalRecordDto>>> GetRecords(Guid athleteId)
    {
        var records = await _db.PersonalRecords
            .Where(r => r.AthleteId == athleteId && r.IsCurrent)
            .Include(r => r.Exercise)
            .Select(r => new PersonalRecordDto(
                r.Id,
                r.ExerciseId,
                r.Exercise!.Name,
                r.WeightOneRepMax,
                r.DateAchieved
            ))
            .ToListAsync();

        return Ok(records);
    }

    [HttpPost("records")]
    public async Task<ActionResult> SaveRecord([FromBody] SaveRecordRequest request)
    {
        if (request.WeightOneRepMax <= 0)
            throw new DomainException("El peso del récord 1RM debe ser mayor a 0 kg.");

        var accountId = _currentUser.AccountId ?? throw new DomainException("Cuenta no autenticada.");

        // Desactivar récord previo si existía
        var previousRecord = await _db.PersonalRecords
            .FirstOrDefaultAsync(r => r.AthleteId == request.AthleteId && r.ExerciseId == request.ExerciseId && r.IsCurrent);

        if (previousRecord != null)
        {
            previousRecord.IsCurrent = false;
        }

        var newRecord = new PersonalRecord
        {
            AccountId = accountId,
            AthleteId = request.AthleteId,
            ExerciseId = request.ExerciseId,
            WeightOneRepMax = request.WeightOneRepMax,
            DateAchieved = DateTime.UtcNow,
            IsCurrent = true
        };

        _db.PersonalRecords.Add(newRecord);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Récord guardado exitosamente." });
    }

    [HttpPost]
    public async Task<ActionResult> CreateRoutine([FromBody] CreateRoutineRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new DomainException("El título de la rutina es obligatorio.");

        if (request.Items == null || request.Items.Count == 0)
            throw new DomainException("Debe agregar al menos un ejercicio a la rutina.");

        var accountId = _currentUser.AccountId ?? throw new DomainException("Cuenta no autenticada.");

        var routine = new Routine
        {
            AccountId = accountId,
            Title = request.Title.Trim(),
            CategoryId = request.CategoryId,
            AthleteId = request.AthleteId,
            DayOfWeek = request.DayOfWeek
        };

        int order = 1;
        foreach (var item in request.Items)
        {
            routine.Items.Add(new RoutineItem
            {
                ExerciseId = item.ExerciseId,
                Sets = item.Sets,
                Reps = item.Reps,
                Percentage = item.Percentage,
                Order = order++
            });
        }

        _db.Routines.Add(routine);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Rutina creada y asignada exitosamente.", routineId = routine.Id });
    }

    /// <summary>
    /// CU-05: El atleta inicia sesión y ve los ejercicios del día con el peso calculado
    /// sobre su récord vigente, redondeado a 2,5 kg. Si integra dos categorías, ve ambas planificaciones.
    /// </summary>
    [HttpGet("today/{athleteId}")]
    public async Task<ActionResult<TodayRoutineDto>> GetTodayRoutine(Guid athleteId)
    {
        var athlete = await _db.Athletes
            .Include(a => a.AthleteCategories)
            .Include(a => a.Injuries)
            .FirstOrDefaultAsync(a => a.Id == athleteId);

        if (athlete == null)
            throw new NotFoundException("Atleta no encontrado.");

        var categoryIds = athlete.AthleteCategories.Select(ac => ac.CategoryId).ToList();
        var today = DateTime.UtcNow.DayOfWeek;

        // Rutinas asignadas a sus categorías O asignadas directamente a él
        var routines = await _db.Routines
            .Where(r => (r.AthleteId == athleteId || (r.CategoryId.HasValue && categoryIds.Contains(r.CategoryId.Value))))
            .Include(r => r.Items)
                .ThenInclude(i => i.Exercise)
            .ToListAsync();

        // Obtener récords 1RM vigentes del atleta
        var currentRecords = await _db.PersonalRecords
            .Where(r => r.AthleteId == athleteId && r.IsCurrent)
            .ToDictionaryAsync(r => r.ExerciseId, r => r.WeightOneRepMax);

        var routineItems = new List<TodayRoutineItemDto>();

        foreach (var item in routines.SelectMany(r => r.Items).OrderBy(i => i.Order))
        {
            decimal record = currentRecords.TryGetValue(item.ExerciseId, out var val) ? val : 0m;
            decimal prescribedWeight = record > 0
                ? CalculationEngine.CalculatePrescribedWeight(record, item.Percentage)
                : 0m;

            routineItems.Add(new TodayRoutineItemDto(
                item.ExerciseId,
                item.Exercise?.Name ?? "Ejercicio",
                item.Exercise?.MuscleGroup ?? "General",
                item.Sets,
                item.Reps,
                item.Percentage,
                prescribedWeight,
                record
            ));
        }

        bool hasActiveInjuries = athlete.Injuries.Any(i => i.Status == InjuryStatus.Active);

        return Ok(new TodayRoutineDto(
            athlete.Id,
            $"{athlete.FirstName} {athlete.LastName}",
            DateTime.UtcNow.Date,
            routineItems,
            hasActiveInjuries
        ));
    }

    /// <summary>
    /// CU-06: Registrar lo levantado y actualizar el récord mediante fórmula de Epley
    /// </summary>
    [HttpPost("log-execution")]
    public async Task<ActionResult> LogExecution([FromBody] LogExecutionRequest request)
    {
        var currentRecord = await _db.PersonalRecords
            .FirstOrDefaultAsync(r => r.AthleteId == request.AthleteId && r.ExerciseId == request.ExerciseId && r.IsCurrent);

        decimal currentOneRepMax = currentRecord?.WeightOneRepMax ?? 0m;

        // Estimar nuevo 1RM con fórmula de Epley
        decimal estimatedOneRepMax = CalculationEngine.EstimateOneRepMaxEpley(request.WeightUsed, request.RepsPerformed);

        bool recordUpdated = false;
        if (estimatedOneRepMax > currentOneRepMax)
        {
            var accountId = _currentUser.AccountId ?? currentRecord?.AccountId ?? Guid.NewGuid();

            if (currentRecord != null)
            {
                currentRecord.IsCurrent = false;
            }

            _db.PersonalRecords.Add(new PersonalRecord
            {
                AccountId = accountId,
                AthleteId = request.AthleteId,
                ExerciseId = request.ExerciseId,
                WeightOneRepMax = estimatedOneRepMax,
                DateAchieved = DateTime.UtcNow,
                IsCurrent = true
            });

            recordUpdated = true;
            await _db.SaveChangesAsync();
        }

        return Ok(new
        {
            weightUsed = request.WeightUsed,
            reps = request.RepsPerformed,
            estimatedOneRepMax,
            previousOneRepMax = currentOneRepMax,
            recordUpdated,
            message = recordUpdated
                ? $"¡Felicitaciones! Se estimó un nuevo récord de 1RM de {estimatedOneRepMax} kg por fórmula Epley."
                : "Ejecución registrada correctamente."
        });
    }
}
