namespace Infrastructure.Services.Ai.Gemini;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-flash-lite-latest";
    public int TimeoutSeconds { get; set; } = 10;
}
