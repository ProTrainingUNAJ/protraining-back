using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace Infrastructure.Repositories
{
    public class ExerciseRepository : IExerciseRepository
    {
        private readonly AppDbContext context;

        public ExerciseRepository(AppDbContext context)
        {
            this.context = context;
        }

        public async Task AddExercise(Exercise exercise, CancellationToken cancellationToken = default)
        {
            await context.Exercises.AddAsync(exercise, cancellationToken);
        }

        public Task<(List<Exercise> Items, int TotalCount)> ListAsync(string? searchTerm, string? equipment, string? muscleGroup, string? muscle, ExerciseType? exerciseType, bool usedByPf, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            return QueryAsync(
                context.Exercises.AsNoTracking(),
                searchTerm,
                equipment,
                muscleGroup,
                muscle,
                exerciseType,
                usedByPf,
                page,
                pageSize,
                cancellationToken);
        }

        // E51: catálogo compartido únicamente. Se combina con el filtro global del AppDbContext,
        // que ya impide ver ejercicios de otros PF, y se refuerza con pf_id = Guid.Empty.
        public Task<(List<Exercise> Items, int TotalCount)> SearchCatalogAsync(string? searchTerm, string? equipment, string? muscle, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var catalog = context.Exercises
                .AsNoTracking()
                .Where(e => e.PfId == Guid.Empty);

            return QueryAsync(
                catalog,
                searchTerm,
                equipment,
                muscleGroup: null,
                muscle,
                exerciseType: null,
                usedByPf: false,
                page,
                pageSize,
                cancellationToken);
        }

        private async Task<(List<Exercise> Items, int TotalCount)> QueryAsync(
            IQueryable<Exercise> source,
            string? searchTerm,
            string? equipment,
            string? muscleGroup,
            string? muscle,
            ExerciseType? exerciseType,
            bool usedByPf,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            var exercises = ApplyFilters(source, searchTerm, equipment, muscleGroup, muscle, exerciseType, usedByPf);

            var totalCount = await exercises.CountAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                exercises = exercises.OrderByDescending(e => EF.Functions.TrigramsWordSimilarity(searchTerm, e.Name)).ThenBy(e => e.Name);
            }
            else
            {
                exercises = exercises.OrderBy(e => e.Name);
            }

            var items = await Paginate(exercises, page, pageSize).ToListAsync(cancellationToken);

            return (items, totalCount);
        }

        public async Task<(List<Exercise> Items, int TotalCount)> ListBasesAsync(string? searchTerm, string? equipment, string? muscleGroup, string? muscle, ExerciseType? exerciseType, bool usedByPf, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var matches = ApplyFilters(context.Exercises.AsNoTracking(), searchTerm, equipment, muscleGroup, muscle, exerciseType, usedByPf);

            // Un base entra si coincide él o alguna de sus variantes.
            var bases = context.Exercises.AsNoTracking()
                .Where(e => e.ParentExerciseId == null
                    && matches.Any(m => m.Id == e.Id || m.ParentExerciseId == e.Id));

            var totalCount = await bases.CountAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                // Se ordena por la mejor similitud de la familia (el base o cualquiera de sus variantes).
                bases = bases
                    .OrderByDescending(e => matches
                        .Where(m => m.Id == e.Id || m.ParentExerciseId == e.Id)
                        .Max(m => (double?)EF.Functions.TrigramsWordSimilarity(searchTerm, m.Name)))
                    // A igual similitud, primero las familias con más variantes (el ejercicio "principal").
                    .ThenByDescending(e => context.Exercises.Count(v => v.ParentExerciseId == e.Id))
                    .ThenBy(e => e.Name);
            }
            else
            {
                bases = bases.OrderBy(e => e.Name);
            }

            var items = await Paginate(bases, page, pageSize).ToListAsync(cancellationToken);

            return (items, totalCount);
        }

        public async Task<List<Exercise>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        {
            if (ids.Count == 0)
                return [];

            return await context.Exercises
                .AsNoTracking()
                .Where(e => ids.Contains(e.Id))
                .OrderBy(e => e.Name)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<Exercise>> GetVariantsAsync(IReadOnlyCollection<Guid> baseExerciseIds, CancellationToken cancellationToken = default)
        {
            if (baseExerciseIds.Count == 0)
                return [];

            return await context.Exercises
                .AsNoTracking()
                .Where(e => e.ParentExerciseId != null && baseExerciseIds.Contains(e.ParentExerciseId.Value))
                .OrderBy(e => e.VariantLabel)
                .ThenBy(e => e.Name)
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await context.Exercises.AnyAsync(e => e.Id == id, cancellationToken);
        }

        public async Task<Exercise?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            var trimmed = name.Trim();
            var exercises = await context.Exercises.ToListAsync(cancellationToken);
            var exercise = exercises.FirstOrDefault(e => CultureInfo.InvariantCulture.CompareInfo.Compare(e.Name,trimmed,CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0);

            return exercise;
        }

        public async Task<List<Exercise>> ListAllAsync(CancellationToken cancellationToken = default)
        {
            return await context.Exercises
                .AsNoTracking()
                .OrderBy(e => e.Name)
                .ToListAsync(cancellationToken);
        }

        public async Task<Exercise?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await context.Exercises.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        }


        private IQueryable<Exercise> ApplyFilters(IQueryable<Exercise> exercises, string? searchTerm, string? equipment, string? muscleGroup, string? muscle, ExerciseType? exerciseType, bool usedByPf)
        {
            if (usedByPf)
            {
                var planExerciseIds = context.PrescribedExercises.Select(pe => pe.ExerciseId);
                var recordExerciseIds = context.AthleteRecords.Select(r => r.ExerciseId);
                var usedExerciseIds = planExerciseIds.Union(recordExerciseIds);

                exercises = exercises.Where(e => usedExerciseIds.Contains(e.Id));
            }

            if (!string.IsNullOrWhiteSpace(equipment))
            {
                exercises = exercises.Where(e => e.Equipment != null && EF.Functions.ILike(e.Equipment, $"%{equipment}%"));
            }

            if (!string.IsNullOrWhiteSpace(muscleGroup))
            {
                exercises = exercises.Where(e => e.MuscleGroup.ToLower() == muscleGroup.ToLower());
            }

            if (!string.IsNullOrWhiteSpace(muscle))
            {
                // Normaliza la entrada (quita tildes, mayúsculas y espacios)
                var normalized = Normalize(muscle);

                // Obtiene las variantes correspondientes de la tabla
                var targets = MuscleEquivalences.TryGetValue(normalized, out var equivs)
                    ? equivs
                    : [normalized, muscle.Trim().ToLower()];

                exercises = exercises.Where(e => targets.Any(t => e.PrimaryMuscles.Contains(t))
                                              || targets.Any(t => e.SecondaryMuscles.Contains(t)))
                                     .OrderByDescending(e => targets.Any(t => e.PrimaryMuscles.Contains(t)))
                                     .ThenBy(e => e.Name);
            }

            if (exerciseType.HasValue)
            {
                exercises = exercises.Where(e => e.ExerciseType == exerciseType.Value);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                exercises = exercises.Where(e => EF.Functions.TrigramsAreWordSimilar(searchTerm, e.Name));
            }

            return exercises;
        }

        private static IQueryable<Exercise> Paginate(IQueryable<Exercise> exercises, int page, int pageSize)
        {
            var pag = page < 1 ? 1 : page;
            var pagSize = pageSize < 1 ? 20 : (pageSize > 100 ? 100 : pageSize);

            return exercises.Skip((pag - 1) * pagSize).Take(pagSize);
        }

        // Mapeo exhaustivo de equivalencias para búsqueda por submúsculo / músculo puntual (KAN-633).
        // Las claves están normalizadas (sin tildes, minúsculas). Los valores contienen los identificadores
        // técnicos en inglés (almacenados en Postgres por free-exercise-db) y sus variantes en español.
        private static readonly Dictionary<string, string[]> MuscleEquivalences = new(StringComparer.OrdinalIgnoreCase)
        {
            // Pecho
            ["pecho"] = ["chest", "pecho", "pectoral", "pectorales"],
            ["pechos"] = ["chest", "pecho", "pectoral", "pectorales"],
            ["pectoral"] = ["chest", "pecho", "pectoral", "pectorales"],
            ["pectorales"] = ["chest", "pecho", "pectoral", "pectorales"],
            ["chest"] = ["chest", "pecho", "pectoral", "pectorales"],

            // Core / Abdomen
            ["abdominales"] = ["abdominals", "abdominales", "abdominal", "abdomen", "core"],
            ["abdominal"] = ["abdominals", "abdominales", "abdominal", "abdomen", "core"],
            ["abdomen"] = ["abdominals", "abdominales", "abdominal", "abdomen", "core"],
            ["core"] = ["abdominals", "abdominales", "abdominal", "abdomen", "core"],
            ["abdominals"] = ["abdominals", "abdominales", "abdominal", "abdomen", "core"],

            // Hombros
            ["hombros"] = ["shoulders", "shoulder", "hombros", "hombro", "deltoides", "deltoide"],
            ["hombro"] = ["shoulders", "shoulder", "hombros", "hombro", "deltoides", "deltoide"],
            ["deltoides"] = ["shoulders", "shoulder", "hombros", "hombro", "deltoides", "deltoide"],
            ["deltoide"] = ["shoulders", "shoulder", "hombros", "hombro", "deltoides", "deltoide"],
            ["shoulders"] = ["shoulders", "shoulder", "hombros", "hombro", "deltoides", "deltoide"],
            ["shoulder"] = ["shoulders", "shoulder", "hombros", "hombro", "deltoides", "deltoide"],

            // Cuello
            ["cuello"] = ["neck", "cuello", "cuellos"],
            ["cuellos"] = ["neck", "cuello", "cuellos"],
            ["neck"] = ["neck", "cuello", "cuellos"],

            // Espalda - Dorsales
            ["dorsales"] = ["lats", "dorsales", "dorsal"],
            ["dorsal"] = ["lats", "dorsales", "dorsal"],
            ["lats"] = ["lats", "dorsales", "dorsal"],

            // Espalda - Espalda media
            ["espalda media"] = ["middle back", "espalda media"],
            ["middle back"] = ["middle back", "espalda media"],

            // Espalda - Espalda baja / Lumbar
            ["espalda baja"] = ["lower back", "espalda baja", "lumbar", "lumbares"],
            ["lower back"] = ["lower back", "espalda baja", "lumbar", "lumbares"],
            ["lumbar"] = ["lower back", "espalda baja", "lumbar", "lumbares"],
            ["lumbares"] = ["lower back", "espalda baja", "lumbar", "lumbares"],

            // Espalda - Trapecios
            ["trapecios"] = ["traps", "trapecios", "trapecio"],
            ["trapecio"] = ["traps", "trapecios", "trapecio"],
            ["traps"] = ["traps", "trapecios", "trapecio"],

            // Brazos - Bíceps
            ["biceps"] = ["biceps", "bíceps", "bicep"],
            ["bicep"] = ["biceps", "bíceps", "bicep"],

            // Brazos - Tríceps
            ["triceps"] = ["triceps", "tríceps", "tricep"],
            ["tricep"] = ["triceps", "tríceps", "tricep"],

            // Brazos - Antebrazos
            ["antebrazos"] = ["forearms", "forearm", "antebrazos", "antebrazo"],
            ["antebrazo"] = ["forearms", "forearm", "antebrazos", "antebrazo"],
            ["forearms"] = ["forearms", "forearm", "antebrazos", "antebrazo"],
            ["forearm"] = ["forearms", "forearm", "antebrazos", "antebrazo"],

            // Piernas - Cuádriceps
            ["cuadriceps"] = ["quadriceps", "quadricep", "cuadríceps", "cuadriceps", "cuadricep", "cuádriceps"],
            ["cuadricep"] = ["quadriceps", "quadricep", "cuadríceps", "cuadriceps", "cuadricep", "cuádriceps"],
            ["quadriceps"] = ["quadriceps", "quadricep", "cuadríceps", "cuadriceps", "cuadricep", "cuádriceps"],
            ["quadricep"] = ["quadriceps", "quadricep", "cuadríceps", "cuadriceps", "cuadricep", "cuádriceps"],

            // Piernas - Isquiotibiales
            ["isquiotibiales"] = ["hamstrings", "hamstring", "isquiotibiales", "isquiotibial", "isquios", "isquio"],
            ["isquiotibial"] = ["hamstrings", "hamstring", "isquiotibiales", "isquiotibial", "isquios", "isquio"],
            ["isquios"] = ["hamstrings", "hamstring", "isquiotibiales", "isquiotibial", "isquios", "isquio"],
            ["isquio"] = ["hamstrings", "hamstring", "isquiotibiales", "isquiotibial", "isquios", "isquio"],
            ["hamstrings"] = ["hamstrings", "hamstring", "isquiotibiales", "isquiotibial", "isquios", "isquio"],
            ["hamstring"] = ["hamstrings", "hamstring", "isquiotibiales", "isquiotibial", "isquios", "isquio"],

            // Piernas - Gemelos / Pantorrillas
            ["gemelos"] = ["calves", "calf", "gemelos", "gemelo", "pantorrillas", "pantorrilla"],
            ["gemelo"] = ["calves", "calf", "gemelos", "gemelo", "pantorrillas", "pantorrilla"],
            ["pantorrillas"] = ["calves", "calf", "gemelos", "gemelo", "pantorrillas", "pantorrilla"],
            ["pantorrilla"] = ["calves", "calf", "gemelos", "gemelo", "pantorrillas", "pantorrilla"],
            ["calves"] = ["calves", "calf", "gemelos", "gemelo", "pantorrillas", "pantorrilla"],
            ["calf"] = ["calves", "calf", "gemelos", "gemelo", "pantorrillas", "pantorrilla"],

            // Piernas - Glúteos
            ["gluteos"] = ["glutes", "glute", "glúteos", "gluteos", "glúteo", "gluteo"],
            ["gluteo"] = ["glutes", "glute", "glúteos", "gluteos", "glúteo", "gluteo"],
            ["glutes"] = ["glutes", "glute", "glúteos", "gluteos", "glúteo", "gluteo"],
            ["glute"] = ["glutes", "glute", "glúteos", "gluteos", "glúteo", "gluteo"],

            // Piernas - Aductores
            ["aductores"] = ["adductors", "adductor", "aductores", "aductor"],
            ["aductor"] = ["adductors", "adductor", "aductores", "aductor"],
            ["adductors"] = ["adductors", "adductor", "aductores", "aductor"],
            ["adductor"] = ["adductors", "adductor", "aductores", "aductor"],

            // Piernas - Abductores
            ["abductores"] = ["abductors", "abductor", "abductores", "abductor"],
            ["abductor"] = ["abductors", "abductor", "abductores", "abductor"],
            ["abductors"] = ["abductors", "abductor", "abductores", "abductor"],
        };

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var formD = value.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var ch in formD)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }
            return sb.ToString().ToLowerInvariant().Trim();
        }
    }
}
