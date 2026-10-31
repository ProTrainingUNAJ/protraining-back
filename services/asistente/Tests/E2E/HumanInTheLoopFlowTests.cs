using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Asistente.Tests.E2E;

// KAN-414: prueba de punta a punta del flujo "la IA propone y el PF decide", con el Asistente real:
// objetivo → borrador → el PF edita → aprueba → la planificación queda creada y asignada en Entrenamiento.
[Trait("Category", "RequiresDocker")]
public sealed class HumanInTheLoopFlowTests
{
    private static readonly Guid LegPressId = Guid.Parse("a1000000-0000-0000-0000-000000000004");
    private static readonly Guid CategoryId = Guid.Parse("c1000000-0000-0000-0000-000000000001");
    private const string StartDate = "2026-11-09";

    private static void LoadCatalog(FakeExternalServices external)
    {
        external.Catalog[FakeAiService.SquatId] = "Sentadilla con barra";
        external.Catalog[FakeAiService.BenchId] = "Press de banca con barra - Agarre medio";
        external.Catalog[FakeAiService.RowId] = "Remo con barra inclinado";
        external.Catalog[LegPressId] = "Prensa de piernas";
    }

    private static async Task<JsonObject> CreateDraftAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/assistant/plan-drafts", new
        {
            goal = "Fuerza de piernas",
            categoryId = CategoryId,
            weeks = 1,
            daysPerWeek = 2,
            startDate = StartDate,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
    }

    // El PF sube la sentadilla de 3 a 5 series y cambia el remo por la prensa, un ejercicio que el Asistente
    // nunca vio (no está en su memoria): para validarlo tiene que preguntarle a Entrenamiento por id.
    private static object EditedContent(JsonObject draft)
    {
        var weeks = draft["weeks"]!.DeepClone().AsArray();
        foreach (var exercise in weeks.SelectMany(w => w!["sessions"]!.AsArray()).SelectMany(s => s!["exercises"]!.AsArray()))
        {
            var id = exercise!["exerciseId"]!.GetValue<Guid>();
            if (id == FakeAiService.SquatId)
                exercise["sets"] = 5;
            if (id == FakeAiService.RowId)
                exercise["exerciseId"] = LegPressId.ToString();
        }

        return new
        {
            name = draft["name"]!.GetValue<string>(),
            objective = draft["objective"]!.GetValue<string>(),
            justificationSummary = draft["justificationSummary"]!.GetValue<string>(),
            weeks,
        };
    }

    [Fact]
    public async Task Given_PfEditsAndApprovesTheAiDraft_When_FlowCompletes_Then_EntrenamientoCreatesAndAssignsThePlanWithThePfChanges()
    {
        await using var factory = await AsistenteE2EFactory.CreateAsync();
        LoadCatalog(factory.External);
        var pfId = Guid.NewGuid();
        var client = factory.CreatePfClient(pfId);

        // 1. Objetivo → borrador (IA de prueba).
        var draft = await CreateDraftAsync(client);
        var draftId = draft["id"]!.GetValue<Guid>();
        Assert.Equal("Draft", draft["status"]!.GetValue<string>());
        Assert.Equal("IaDePrueba", draft["provider"]!.GetValue<string>());

        // 2. El PF edita: una serie y un ejercicio nuevo.
        var update = await client.PutAsJsonAsync($"/api/v1/assistant/plan-drafts/{draftId}", EditedContent(draft));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.NotEmpty(factory.External.To("entrenamiento.test", HttpMethod.Get, $"/api/v1/exercises?ids={LegPressId}"));

        // 3. El PF aprueba.
        var approve = await client.PostAsync($"/api/v1/assistant/plan-drafts/{draftId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var approved = JsonNode.Parse(await approve.Content.ReadAsStringAsync())!;
        Assert.Equal(factory.External.CreatedPlanId, approved["approvedPlanId"]!.GetValue<Guid>());
        Assert.True(approved["assigned"]!.GetValue<bool>());

        // 4. A Entrenamiento le llegó la planificación con los cambios del PF, con el token del PF...
        var created = Assert.Single(factory.External.To("entrenamiento.test", HttpMethod.Post, "/api/v1/training-plans"),
            r => r.PathAndQuery == "/api/v1/training-plans");
        Assert.StartsWith("Bearer ", created.Authorization);
        var planExercises = JsonNode.Parse(created.Body)!["weeks"]!.AsArray()
            .SelectMany(w => w!["sessions"]!.AsArray())
            .SelectMany(s => s!["exercises"]!.AsArray())
            .ToList();
        Assert.Equal(5, planExercises.Single(e => e!["exerciseId"]!.GetValue<Guid>() == FakeAiService.SquatId)!["sets"]!.GetValue<int>());
        Assert.Contains(planExercises, e => e!["exerciseId"]!.GetValue<Guid>() == LegPressId);
        Assert.DoesNotContain(planExercises, e => e!["exerciseId"]!.GetValue<Guid>() == FakeAiService.RowId);

        // ...y después la asignación a la categoría, con la fecha de inicio.
        var assignment = Assert.Single(factory.External.To("entrenamiento.test", HttpMethod.Post, "/api/v1/training-plans/assignments"));
        var assignmentBody = JsonNode.Parse(assignment.Body)!;
        Assert.Equal(factory.External.CreatedPlanId, assignmentBody["trainingPlanId"]!.GetValue<Guid>());
        Assert.Equal(CategoryId, assignmentBody["categoryId"]!.GetValue<Guid>());
        Assert.Equal(StartDate, assignmentBody["startDate"]!.GetValue<string>());
        var order = factory.External.Requests.Select(r => r.PathAndQuery).ToList();
        Assert.True(order.IndexOf("/api/v1/training-plans") < order.IndexOf("/api/v1/training-plans/assignments"));

        // 5. El borrador queda aprobado, con el id de la planificación.
        var final = JsonNode.Parse(await client.GetStringAsync($"/api/v1/assistant/plan-drafts/{draftId}"))!;
        Assert.Equal("Approved", final["status"]!.GetValue<string>());
        Assert.Equal(factory.External.CreatedPlanId, final["approvedPlanId"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task Given_EntrenamientoDown_When_PfApproves_Then_Responds503AndTheDraftStaysInDraft()
    {
        await using var factory = await AsistenteE2EFactory.CreateAsync();
        LoadCatalog(factory.External);
        factory.External.CreatePlanStatus = HttpStatusCode.ServiceUnavailable;
        var client = factory.CreatePfClient(Guid.NewGuid());

        var draft = await CreateDraftAsync(client);
        var draftId = draft["id"]!.GetValue<Guid>();

        var approve = await client.PostAsync($"/api/v1/assistant/plan-drafts/{draftId}/approve", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, approve.StatusCode);
        Assert.Empty(factory.External.To("entrenamiento.test", HttpMethod.Post, "/api/v1/training-plans/assignments"));
        var final = JsonNode.Parse(await client.GetStringAsync($"/api/v1/assistant/plan-drafts/{draftId}"))!;
        Assert.Equal("Draft", final["status"]!.GetValue<string>());
        Assert.Null(final["approvedPlanId"]?.GetValue<Guid?>());
    }

    [Fact]
    public async Task Given_ExerciseNotInEntrenamiento_When_PfEdits_Then_Responds400AndNothingIsSaved()
    {
        await using var factory = await AsistenteE2EFactory.CreateAsync();
        LoadCatalog(factory.External);
        var client = factory.CreatePfClient(Guid.NewGuid());

        var draft = await CreateDraftAsync(client);
        var draftId = draft["id"]!.GetValue<Guid>();
        factory.External.Catalog.TryRemove(LegPressId, out _);

        var update = await client.PutAsJsonAsync($"/api/v1/assistant/plan-drafts/{draftId}", EditedContent(draft));

        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        Assert.Contains("no existe en el catálogo", await update.Content.ReadAsStringAsync());
        var unchanged = JsonNode.Parse(await client.GetStringAsync($"/api/v1/assistant/plan-drafts/{draftId}"))!;
        Assert.Contains(unchanged["weeks"]!.AsArray().SelectMany(w => w!["sessions"]!.AsArray()).SelectMany(s => s!["exercises"]!.AsArray()),
            e => e!["exerciseId"]!.GetValue<Guid>() == FakeAiService.RowId);
    }
}
