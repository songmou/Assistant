namespace EhrAgent.Services;

public sealed class AiOptions
{
    public const string SectionName = "Ai";
    public string ApiUrl { get; set; } = "https://api.deepseek.com/chat/completions";
    public string Model { get; set; } = "deepseek-chat";
    // 如未配置该值，系统会回退读取环境变量 DEEPSEEK_API_KEY。
    public string? ApiKey { get; set; }
}
