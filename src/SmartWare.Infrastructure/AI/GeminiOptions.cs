namespace SmartWare.Infrastructure.AI;

internal sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gemini-3.6-flash";
    public string EmbeddingModel { get; set; } = "gemini-embedding-2";
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxOutputTokens { get; set; } = 4096;
    public string ThinkingLevel { get; set; } = "MINIMAL";
    public int EmbeddingDimensions { get; set; } = 768;
    public int RagTopK { get; set; } = 3;
    public double RagMinimumScore { get; set; } = 0.35;
    public int MaxToolRounds { get; set; } = 4;
    public string[] FallbackModels { get; set; } = [];
}
