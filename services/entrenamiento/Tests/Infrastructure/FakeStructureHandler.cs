using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Entrenamiento.Tests.Infrastructure;

/// <summary>
/// Intercepta las llamadas HTTP de StructureClient para simular respuestas de Estructura en tests de integración,
/// respetando el aislamiento multitenant por PF a partir del claim "pf_id" del token Bearer.
/// </summary>
public sealed class FakeStructureHandler : HttpMessageHandler
{
    private readonly Dictionary<Guid, Guid> _categoryOwnerMap = new(); // CategoryId -> PfId
    private readonly Dictionary<Guid, (Guid CategoryId, Guid PfId, string FullName)> _athleteMap = new(); // AthleteId -> (CategoryId, PfId, FullName)

    public void RegisterCategory(Guid categoryId, Guid pfId)
    {
        _categoryOwnerMap[categoryId] = pfId;
    }

    public void RegisterAthlete(Guid athleteId, Guid categoryId, Guid pfId, string fullName = "Atleta Test")
    {
        _categoryOwnerMap[categoryId] = pfId;
        _athleteMap[athleteId] = (categoryId, pfId, fullName);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath?.Trim('/') ?? string.Empty;
        var callerPfId = ExtractPfIdFromAuthorization(request);

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // GET api/v1/categories/{categoryId} (IsOwnCategoryAsync)
        if (segments.Length == 4 && segments[0] == "api" && segments[1] == "v1" && segments[2] == "categories"
            && Guid.TryParse(segments[3], out var categoryId))
        {
            if (_categoryOwnerMap.TryGetValue(categoryId, out var ownerPfId) && ownerPfId == callerPfId)
            {
                var body = JsonSerializer.Serialize(new { id = categoryId, name = "Test Category" });
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, body));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        // GET api/v1/athletes/{athleteId}/categories (GetAthleteCategoriesAsync)
        if (segments.Length == 5 && segments[0] == "api" && segments[1] == "v1" && segments[2] == "athletes"
            && Guid.TryParse(segments[3], out var athleteForCatsId) && segments[4] == "categories")
        {
            if (_athleteMap.TryGetValue(athleteForCatsId, out var athInfo) && athInfo.PfId == callerPfId)
            {
                var body = JsonSerializer.Serialize(new[] { new { id = athInfo.CategoryId, name = "Test Category" } });
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, body));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        // GET api/v1/athletes/{athleteId} (GetAthleteByIdAsync)
        if (segments.Length == 4 && segments[0] == "api" && segments[1] == "v1" && segments[2] == "athletes"
            && Guid.TryParse(segments[3], out var athleteId))
        {
            if (_athleteMap.TryGetValue(athleteId, out var athInfo) && athInfo.PfId == callerPfId)
            {
                var body = JsonSerializer.Serialize(new
                {
                    id = athleteId,
                    fullName = athInfo.FullName
                });
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, body));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        // GET api/v1/me/categories (GetMyCategoriesAsync)
        if (path.EndsWith("api/v1/me/categories", StringComparison.OrdinalIgnoreCase))
        {
            var myCats = _categoryOwnerMap
                .Where(kv => kv.Value == callerPfId)
                .Select(kv => new { id = kv.Key, name = $"Category {kv.Key}" })
                .ToList();

            var body = JsonSerializer.Serialize(myCats);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, body));
        }

        return Task.FromResult(JsonResponse(HttpStatusCode.OK, "[]"));
    }

    private static Guid? ExtractPfIdFromAuthorization(HttpRequestMessage request)
    {
        var authHeader = request.Headers.Authorization;
        if (authHeader == null || string.IsNullOrWhiteSpace(authHeader.Parameter))
            return null;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (handler.CanReadToken(authHeader.Parameter))
            {
                var token = handler.ReadJwtToken(authHeader.Parameter);
                var pfClaim = token.Claims.FirstOrDefault(c => c.Type == "pf_id");
                if (pfClaim != null && Guid.TryParse(pfClaim.Value, out var pfId))
                    return pfId;
            }
        }
        catch
        {
            // Fallback si no se puede leer el JWT
        }

        return null;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}