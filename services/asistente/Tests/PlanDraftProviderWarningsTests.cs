using Application.Common.PlanDrafts;
using Xunit;

namespace Asistente.Tests;

// El PF tiene que ver por qué el borrador no lo armó el modelo local (respaldo Gemini o reglas fijas)
public sealed class PlanDraftProviderWarningsTests
{
    [Fact]
    public void ProviderWarnings_Ollama_HasNoWarning()
    {
        Assert.Empty(PlanDraftMapper.ProviderWarnings("Ollama"));
    }

    [Fact]
    public void ProviderWarnings_Gemini_ExplainsTheExternalFallbackWithoutPersonalData()
    {
        var warning = Assert.Single(PlanDraftMapper.ProviderWarnings("Gemini"));

        Assert.Contains("respaldo externo (Gemini)", warning);
        Assert.Contains("sin nombres ni datos personales", warning);
    }

    [Fact]
    public void ProviderWarnings_Rules_ExplainsThatBothAiProvidersFailed()
    {
        var warning = Assert.Single(PlanDraftMapper.ProviderWarnings("Reglas"));

        Assert.Contains("reglas fijas de ProTraining", warning);
        Assert.Contains("Revisalo", warning);
    }
}
