using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Persistence;
using Domain.Entities;

namespace Application.UseCases.Export.ExportStructureCsv;

public sealed record ExportStructureCsvQuery;

public sealed record ExportStructureCsvResult(
    byte[] FileBytes,
    string ContentType,
    string FileName);

public sealed class ExportStructureCsvQueryHandler(
    IOrganizationRepository organizationRepository,
    ICategoryRepository categoryRepository,
    IAthleteRepository athleteRepository,
    IAthleteCategoryRepository athleteCategoryRepository)
{
    public async Task<ExportStructureCsvResult> HandleAsync(
        ExportStructureCsvQuery query,
        CancellationToken cancellationToken = default)
    {
        // 1. obtiene las organizaciones y los atletas
        var organizations = await organizationRepository.ListAsync(cancellationToken);
        var athletes = await athleteRepository.ListAsync(cancellationToken);

        // 2. obtiene las categorias asociadas a cada organizacion
        var categories = new List<Category>();
        foreach (var org in organizations)
        {
            var orgCategories = await categoryRepository.ListByOrganizationAsync(org.Id, cancellationToken);
            categories.AddRange(orgCategories);
        }

        // 3. construye el contenido del archivo csv con codificacion utf-8
        var sb = new StringBuilder();

        // seccion organizaciones
        sb.AppendLine("# ORGANIZACIONES");
        sb.AppendLine("Id,Nombre,Descripcion");
        foreach (var org in organizations)
        {
            sb.Append(org.Id).Append(',');
            sb.Append(EscapeCsv(org.Name)).Append(',');
            sb.Append(EscapeCsv(org.Description)).AppendLine();
        }
        sb.AppendLine();

        // seccion categorias
        sb.AppendLine("# CATEGORIAS");
        sb.AppendLine("Id,OrganizacionId,Nombre,Descripcion");
        foreach (var cat in categories)
        {
            sb.Append(cat.Id).Append(',');
            sb.Append(cat.OrganizationId).Append(',');
            sb.Append(EscapeCsv(cat.Name)).Append(',');
            sb.Append(EscapeCsv(cat.Description)).AppendLine();
        }
        sb.AppendLine();

        // seccion atletas
        sb.AppendLine("# ATLETAS");
        sb.AppendLine("Id,Nombre,Apellido,Email,Telefono,EsMenor,ContactoEmergencia,CondicionesMedicas,Activo,PermiteHabitos");
        foreach (var a in athletes)
        {
            sb.Append(a.Id).Append(',');
            sb.Append(EscapeCsv(a.FirstName)).Append(',');
            sb.Append(EscapeCsv(a.LastName)).Append(',');
            sb.Append(EscapeCsv(a.Email)).Append(',');
            sb.Append(EscapeCsv(a.Phone)).Append(',');
            sb.Append(a.IsMinor ? "true" : "false").Append(',');
            sb.Append(EscapeCsv(a.EmergencyContact)).Append(',');
            sb.Append(EscapeCsv(a.MedicalConditions)).Append(',');
            sb.Append(a.Active ? "true" : "false").Append(',');
            sb.Append(a.AllowsHabitsRecommendations ? "true" : "false").AppendLine();
        }
        sb.AppendLine();

        // seccion atletas por categoria (#017)
        sb.AppendLine("# ATLETAS_CATEGORIAS");
        sb.AppendLine("AtletaId,CategoriaId");
        foreach (var a in athletes)
        {
            var athleteCategories = await athleteCategoryRepository.ListByAthleteAsync(a.Id, cancellationToken);
            foreach (var ac in athleteCategories)
            {
                sb.Append(ac.AthleteId).Append(',');
                sb.Append(ac.CategoryId).AppendLine();
            }
        }

        // 4. genera los bytes utf-8 con bom para que excel abra sin problemas de caracteres
        var preamble = Encoding.UTF8.GetPreamble();
        var contentBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var fileBytes = new byte[preamble.Length + contentBytes.Length];
        Buffer.BlockCopy(preamble, 0, fileBytes, 0, preamble.Length);
        Buffer.BlockCopy(contentBytes, 0, fileBytes, preamble.Length, contentBytes.Length);

        var fileName = "estructura_export_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".csv";

        return new ExportStructureCsvResult(fileBytes, "text/csv; charset=utf-8", fileName);
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // neutraliza formulas de excel (csv injection) si empieza con =, +, - o @
        if (value.StartsWith('=') || value.StartsWith('+') || value.StartsWith('-') || value.StartsWith('@'))
        {
            value = "'" + value;
        }

        // si contiene comas, comillas dobles o saltos de linea, se encierra entre comillas
        if (value.Contains(',') || value.Contains('\"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
