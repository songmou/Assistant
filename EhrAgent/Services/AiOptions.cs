namespace EhrAgent.Services;

public sealed class AiOptions
{
    public const string SectionName = "Ai";
    public string ApiUrl { get; set; } = "https://api.deepseek.com/chat/completions";
    public string Model { get; set; } = "deepseek-chat";
    public string? ApiKey { get; set; }
}
