namespace EhrAgent.Services;

public sealed record LoginStatusDto(string SessionId, bool LoggedIn, string CurrentUrl, string? Error = null);

public sealed record ChatResultDto(string Reply, IReadOnlyList<BrowserActionResultDto> Actions);

public sealed record BrowserActionResultDto(string Type, string Target, string Status, string? Error = null);
