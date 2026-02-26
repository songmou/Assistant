using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace EhrAgent.Services;

public sealed class BrowserSessionManager : IAsyncDisposable
{
    private static readonly string[] QrSelectors =
    [
        "canvas",
        "img[src*='qr']",
        ".qr-code img",
        ".qrcode img"
    ];

    private readonly PortalOptions _options;
    private readonly ConcurrentDictionary<string, BrowserSession> _sessions = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IPlaywright? _playwright;
    private bool _browserInstalled;

    public BrowserSessionManager(IOptions<PortalOptions> options)
    {
        _options = options.Value;
    }

    public async Task<(string SessionId, string UserId)> CreateSessionAsync(string userId, CancellationToken cancellationToken)
    {
        var session = _sessions.Values.FirstOrDefault(s => s.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase));
        if (session is not null)
        {
            return (session.SessionId, session.UserId);
        }

        await EnsurePlaywrightAsync();

        var browser = await _playwright!.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = _options.Headless
        });

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
        });
        var page = await context.NewPageAsync();
        var newSession = new BrowserSession(Guid.NewGuid().ToString("N"), userId.Trim(), browser, context, page);
        try
        {
            await page.GotoAsync(_options.EntryUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        }
        catch (Exception ex)
        {
            newSession.LastError = ex.Message;
        }

        _sessions[newSession.SessionId] = newSession;

        return (newSession.SessionId, newSession.UserId);
    }

    public async Task<LoginStatusDto?> GetLoginStatusAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return null;
        }

        await session.Lock.WaitAsync(cancellationToken);
        try
        {
            var currentUrl = session.Page.Url;
            var loggedIn = false;
            try
            {
                loggedIn = await IsLoggedInAsync(session.Page);
            }
            catch (Exception ex)
            {
                session.LastError = ex.Message;
            }

            return new LoginStatusDto(session.SessionId, loggedIn, currentUrl, session.LastError);
        }
        finally
        {
            session.Lock.Release();
        }
    }

    public async Task<byte[]?> GetQrImageAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return null;
        }

        await session.Lock.WaitAsync(cancellationToken);
        try
        {
            foreach (var selector in QrSelectors)
            {
                var locator = session.Page.Locator(selector).First;
                if (await locator.CountAsync() > 0)
                {
                    return await locator.ScreenshotAsync();
                }
            }

            return await session.Page.ScreenshotAsync(new PageScreenshotOptions { FullPage = false });
        }
        catch (Exception ex)
        {
            session.LastError = ex.Message;
            return null;
        }
        finally
        {
            session.Lock.Release();
        }
    }

    public async Task<BrowserSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return null;
        }

        await session.Lock.WaitAsync(cancellationToken);
        return session;
    }

    public void ReleaseSession(BrowserSession session)
    {
        session.Lock.Release();
    }

    private async Task<bool> IsLoggedInAsync(IPage page)
    {
        var hasEntry = await page.GetByText("OA", new PageGetByTextOptions { Exact = false }).CountAsync() > 0
                       || await page.GetByText("EHR", new PageGetByTextOptions { Exact = false }).CountAsync() > 0;
        var hasQr = await page.Locator("canvas, img[src*='qr'], .qrcode").CountAsync() > 0;
        return hasEntry && !hasQr;
    }

    private async Task EnsurePlaywrightAsync()
    {
        if (_playwright is not null)
        {
            return;
        }

        await _initLock.WaitAsync();
        try
        {
            if (!_browserInstalled)
            {
                Microsoft.Playwright.Program.Main(["install", "chromium"]);
                _browserInstalled = true;
            }

            _playwright ??= await Playwright.CreateAsync();
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values)
        {
            await session.Page.CloseAsync();
            await session.Context.CloseAsync();
            await session.Browser.CloseAsync();
            session.Lock.Dispose();
        }

        _sessions.Clear();
        _initLock.Dispose();
        _playwright?.Dispose();
    }
}

public sealed class BrowserSession(
    string sessionId,
    string userId,
    IBrowser browser,
    IBrowserContext context,
    IPage page)
{
    public string SessionId { get; } = sessionId;
    public string UserId { get; } = userId;
    public IBrowser Browser { get; } = browser;
    public IBrowserContext Context { get; } = context;
    public IPage Page { get; } = page;
    public SemaphoreSlim Lock { get; } = new(1, 1);
    public string? LastError { get; set; }
}
