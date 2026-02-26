using EhrAgent.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.Configure<PortalOptions>(builder.Configuration.GetSection(PortalOptions.SectionName));
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.AddHttpClient<AiAgentService>();
builder.Services.AddSingleton<BrowserSessionManager>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.MapPost("/api/sessions", async (CreateSessionRequest request, BrowserSessionManager manager, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.UserId))
    {
        return Results.BadRequest(new { error = "UserId is required." });
    }

    var session = await manager.CreateSessionAsync(request.UserId, cancellationToken);
    return Results.Ok(new { sessionId = session.SessionId, userId = session.UserId });
});

app.MapGet("/api/sessions/{sessionId}/status", async (string sessionId, BrowserSessionManager manager, CancellationToken cancellationToken) =>
{
    var status = await manager.GetLoginStatusAsync(sessionId, cancellationToken);
    return status is null ? Results.NotFound() : Results.Ok(status);
});

app.MapGet("/api/sessions/{sessionId}/qr", async (string sessionId, BrowserSessionManager manager, CancellationToken cancellationToken) =>
{
    var image = await manager.GetQrImageAsync(sessionId, cancellationToken);
    return image is null ? Results.NotFound() : Results.File(image, "image/png");
});

app.MapPost("/api/sessions/{sessionId}/chat", async (string sessionId, ChatRequest request, BrowserSessionManager manager, AiAgentService aiAgentService, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "Message is required." });
    }

    var result = await aiAgentService.ProcessAsync(sessionId, request.Message, manager, cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.Run();

public sealed record CreateSessionRequest(string UserId);
public sealed record ChatRequest(string Message);
