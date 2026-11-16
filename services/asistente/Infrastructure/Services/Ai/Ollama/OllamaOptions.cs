namespace Infrastructure.Services.Ai.Ollama;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3.2:1b";
    public int TimeoutSeconds { get; set; } = 15;
    public double Temperature { get; set; } = 0.2;
    public int MaxOutputTokens { get; set; } = 1200;
}
