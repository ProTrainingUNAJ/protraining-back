using Application.DTOs.AthleteRecords;
using Application.DTOs.Memberships;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using System.Globalization;
using System.Text;

namespace Application.UseCases.AthleteRecord.PreviewRecordsImport;

public class PreviewRecordsImportQueryHandler
{
    private readonly IExerciseRepository _exerciseRepository;
    private readonly IStructureClient _structureClient;

    public PreviewRecordsImportQueryHandler(IExerciseRepository exerciseRepository, IStructureClient structureClient)
    {
        _exerciseRepository = exerciseRepository;
        _structureClient = structureClient;
    }

    public async Task<List<ImportRecordPreviewRowDto>> Handle(PreviewRecordsImportQuery query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.CsvContent))
            throw new DomainException("El archivo CSV está vacío.");

        var text = query.CsvContent.TrimStart('\ufeff');
        var delimiter = DetectDelimiter(text);

        var rows = ParseCsv(text, delimiter);
        if (rows.Count == 0)
            throw new DomainException("El archivo CSV está vacío.");

        var headers = rows[0];
        var dataRows = rows.Skip(1).Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();

        if (dataRows.Count == 0)
            throw new DomainException("El CSV no contiene filas de datos.");
        if (dataRows.Count > 2000)
            throw new DomainException("El archivo supera las 2.000 filas permitidas.");

        var athleteIndex = IndexOfHeader(headers, query.AthleteColumn);
        var exerciseIndex = IndexOfHeader(headers, query.ExerciseColumn);
        var weightIndex = IndexOfHeader(headers, query.WeightColumn);
        var dateIndex = string.IsNullOrWhiteSpace(query.DateColumn) ? (int?)null : IndexOfHeader(headers, query.DateColumn!);
        var sourceIndex = string.IsNullOrWhiteSpace(query.SourceColumn) ? (int?)null : IndexOfHeader(headers, query.SourceColumn!);

        var athletes = await _structureClient.ListAthletesAsync(cancellationToken);
        var athleteMap = new Dictionary<string, StructureAthleteDto>(StringComparer.Ordinal);
        foreach (var athlete in athletes)
        {
            var key = Normalize(athlete.FullName);
            if (!athleteMap.ContainsKey(key))
                athleteMap[key] = athlete;
        }

        var result = new List<ImportRecordPreviewRowDto>(dataRows.Count);
        var rowNumber = 1;
        foreach (var dataRow in dataRows)
        {
            var athleteName = GetCell(dataRow, athleteIndex);
            var exerciseName = GetCell(dataRow, exerciseIndex);
            var weightText = GetCell(dataRow, weightIndex);
            var dateText = dateIndex.HasValue ? GetCell(dataRow, dateIndex.Value) : null;
            var sourceText = sourceIndex.HasValue ? GetCell(dataRow, sourceIndex.Value) : null;

            Guid? athleteId = null;
            Guid? exerciseId = null;
            decimal? weight = null;
            DateTime? achievedAt = null;
            string? origin = null;

            var status = ImportRecordRowStatus.Ok;
            string message = "Listo para importar.";

            // Atleta
            var athleteKey = Normalize(athleteName);
            if (athleteMap.TryGetValue(athleteKey, out var foundAthlete))
            {
                athleteId = foundAthlete.Id;
            }
            else
            {
                status = ImportRecordRowStatus.AthleteNotFound;
                message = $"No se encontró el atleta '{athleteName}'.";
            }

            // Ejercicio
            Domain.Entities.Exercise? exercise = null;
            if (status == ImportRecordRowStatus.Ok)
            {
                exercise = await _exerciseRepository.FindByNameAsync(exerciseName, cancellationToken);
                if (exercise is null)
                {
                    var (items, _) = await _exerciseRepository.ListAsync(exerciseName, null, null, null, false, 1, 1, cancellationToken);
                    exercise = items.FirstOrDefault();
                }

                if (exercise is null)
                {
                    status = ImportRecordRowStatus.ExerciseNotFound;
                    message = $"No se encontró el ejercicio '{exerciseName}'.";
                }
                else
                {
                    exerciseId = exercise.Id;
                }
            }

            // Peso
            if (status == ImportRecordRowStatus.Ok)
            {
                if (TryParseWeight(weightText, out var parsedWeight))
                {
                    if (parsedWeight <= 0)
                    {
                        status = ImportRecordRowStatus.InvalidWeight;
                        message = $"El peso '{weightText}' no es válido.";
                    }
                    else
                    {
                        weight = parsedWeight;
                    }
                }
                else
                {
                    status = ImportRecordRowStatus.InvalidWeight;
                    message = $"El peso '{weightText}' no es válido.";
                }
            }

            // Fecha
            if (!string.IsNullOrWhiteSpace(dateText))
            {
                if (TryParseDate(dateText, out var parsedDate))
                    achievedAt = parsedDate;
            }

            // Origen
            if (!string.IsNullOrWhiteSpace(sourceText))
                origin = ResolveOrigin(sourceText);

            if (status != ImportRecordRowStatus.Ok)
            {
                if (status == ImportRecordRowStatus.AthleteNotFound)
                {
                    exerciseId = null;
                    weight = null;
                    achievedAt = null;
                    origin = null;
                }
                else if (status == ImportRecordRowStatus.ExerciseNotFound)
                {
                    weight = null;
                }
            }

            result.Add(new ImportRecordPreviewRowDto(
                rowNumber,
                athleteName,
                exerciseName,
                weightText,
                string.IsNullOrWhiteSpace(dateText) ? null : dateText,
                string.IsNullOrWhiteSpace(sourceText) ? null : sourceText,
                status,
                message,
                athleteId,
                exerciseId,
                weight,
                achievedAt,
                origin));

            rowNumber++;
        }

        return result;
    }

    private static char DetectDelimiter(string text)
    {
        var firstLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        var commas = firstLine.Count(c => c == ',');
        var semicolons = firstLine.Count(c => c == ';');
        return semicolons > commas ? ';' : ',';
    }

    private static List<List<string>> ParseCsv(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var field = new StringBuilder();
        var row = new List<string>();
        var inQuotes = false;
        var fieldStarted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                    fieldStarted = true;
                }
                else if (c == delimiter)
                {
                    row.Add(field.ToString());
                    field.Clear();
                    fieldStarted = false;
                }
                else if (c == '\n')
                {
                    row.Add(field.ToString());
                    rows.Add(row);
                    row = new List<string>();
                    field.Clear();
                    fieldStarted = false;
                }
                else if (c == '\r')
                {
                    // ignore
                }
                else
                {
                    field.Append(c);
                    fieldStarted = true;
                }
            }
        }

        if (field.Length > 0 || fieldStarted || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    private static int IndexOfHeader(List<string> headers, string columnName)
    {
        var index = headers.FindIndex(h => string.Equals(h.Trim(), columnName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            throw new DomainException($"No se encontró la columna '{columnName}' en el CSV.");
        return index;
    }

    private static string GetCell(List<string> row, int index)
    {
        if (index < 0 || index >= row.Count)
            return string.Empty;
        return row[index].Trim();
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var formD = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in formD)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return sb.ToString().ToLowerInvariant().Trim();
    }

    private static bool TryParseWeight(string text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseDate(string text, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var formats = new[] { "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "dd-MM-yyyy", "d/M/yyyy" };
        if (DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            return true;

        return DateTime.TryParse(text.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out value);
    }

    private static string ResolveOrigin(string text)
    {
        var trimmed = text.Trim();
        foreach (var (source, name) in Origins)
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        return "Reported";
    }

    private static readonly Dictionary<RecordSource, string> Origins = new()
    {
        [RecordSource.Tested] = "TestedInSession",
        [RecordSource.Reported] = "Reported",
        [RecordSource.Estimated] = "EstimatedEpley",
        [RecordSource.Epley] = "Epley",
    };
}
