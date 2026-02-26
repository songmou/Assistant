using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace EhrAgent.Services;

public sealed class AiAgentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;

    public AiAgentService(HttpClient httpClient, IOptions<AiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<ChatResultDto?> ProcessAsync(
        string sessionId,
        string message,
        BrowserSessionManager sessionManager,
        CancellationToken cancellationToken)
    {
        var session = await sessionManager.GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        try
        {
            var actions = await ExecuteDeterministicActionsAsync(session, message, cancellationToken);
            var pageText = await session.Page.EvaluateAsync<string>("() => (document.body?.innerText || '').slice(0, 3000)");
            var aiReply = await GetAiReplyAsync(message, pageText, cancellationToken);

            return new ChatResultDto(aiReply, actions);
        }
        finally
        {
            sessionManager.ReleaseSession(session);
        }
    }

    private async Task<IReadOnlyList<BrowserActionResultDto>> ExecuteDeterministicActionsAsync(BrowserSession session, string message, CancellationToken cancellationToken)
    {
        var actions = new List<BrowserActionResultDto>();
        var lower = message.ToLowerInvariant();

        if (lower.Contains("oa"))
        {
            actions.Add(await ClickByTextAsync(session, "OA"));
        }

        if (lower.Contains("ehr"))
        {
            actions.Add(await ClickByTextAsync(session, "EHR"));
        }

        if (lower.Contains("刷新") || lower.Contains("refresh"))
        {
            try
            {
                await session.Page.ReloadAsync();
                actions.Add(new BrowserActionResultDto("reload", "page", "ok"));
            }
            catch (Exception ex)
            {
                actions.Add(new BrowserActionResultDto("reload", "page", "error", ex.Message));
            }
        }

        return actions;
    }

    private static async Task<BrowserActionResultDto> ClickByTextAsync(BrowserSession session, string text)
    {
        try
        {
            var locator = session.Page.GetByText(text, new() { Exact = false }).First;
            if (await locator.CountAsync() == 0)
            {
                return new BrowserActionResultDto("click", text, "skipped", "not found");
            }

            await locator.ClickAsync();
            return new BrowserActionResultDto("click", text, "ok");
        }
        catch (Exception ex)
        {
            return new BrowserActionResultDto("click", text, "error", ex.Message);
        }
    }

    private async Task<string> GetAiReplyAsync(string userMessage, string pageText, CancellationToken cancellationToken)
    {
        var apiKey = _options.ApiKey ?? Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return "未配置DEEPSEEK_API_KEY。已执行可识别的网页操作（如打开OA/EHR），请配置后启用AI建议。";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.ApiUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = _options.Model,
            messages = new object[]
            {
                new { role = "system", content = "你是OA/EHR办公助手，仅处理OA和EHR事项。给出简洁、可执行的下一步建议。" },
                new { role = "user", content = $"用户请求：{userMessage}\n\n页面摘要：{pageText}" }
            },
            temperature = 0.2
        }), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return $"AI服务调用失败：{(int)response.StatusCode}，但自动化步骤已尝试执行。";
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var aiResponse = await JsonSerializer.DeserializeAsync<DeepSeekResponse>(stream, JsonOptions, cancellationToken);
        return aiResponse?.Choices?.FirstOrDefault()?.Message?.Content?.Trim()
               ?? "AI未返回内容，但自动化步骤已尝试执行。";
    }

    private sealed class DeepSeekResponse
    {
        public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        public Message? Message { get; set; }
    }

    private sealed class Message
    {
        public string? Content { get; set; }
    }
}
