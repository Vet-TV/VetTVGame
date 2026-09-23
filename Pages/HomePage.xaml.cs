using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using VetTVGame.Bridge;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace VetTVGame.Pages;

public sealed partial class HomePage : Page
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Stable virtual host for packaged webui (not file://).</summary>
    private const string WebUiVirtualHostName = "vettvgame.local";

    private const int MaxAutoRecoverAttempts = 3;
    private static readonly TimeSpan[] RecoverBackoff =
    [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
    ];

    // Stub HUD state echoed back to JS (no real game logic yet).
    private int _money = 100;
    private int _lemons;
    private string _weather = "Sunny";
    private bool _raytracing;
    private bool _webUiReady;
    private bool _recovering;
    private int _autoRecoverAttempts;
    private int _unresponsiveSignals;

    public HomePage()
    {
        InitializeComponent();
        GameWebView.Loaded += GameWebView_Loaded;
    }

    private async void GameWebView_Loaded(object sender, RoutedEventArgs e)
    {
        GameWebView.Loaded -= GameWebView_Loaded;
        await EnsureCoreAndNavigateAsync();
    }

    private async Task EnsureCoreAndNavigateAsync()
    {
        try
        {
            await GameWebView.EnsureCoreWebView2Async();
            var core = GameWebView.CoreWebView2
                ?? throw new InvalidOperationException("CoreWebView2 is null after EnsureCoreWebView2Async");

            WireCoreEvents(core);
            ApplyVirtualHostMapping(core);
            NavigateToWebUi();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WebView bridge: EnsureCoreWebView2Async/Navigate failed — {ex.Message}");
            SetBridgeStatus("WebView failed to start (see Debug Output).");
        }
    }

    private void WireCoreEvents(CoreWebView2? core)
    {
        if (core is null)
        {
            return;
        }

        // Always (re)attach on a possibly new CoreWebView2 instance.
        core.WebMessageReceived -= CoreWebView2_WebMessageReceived;
        core.NavigationCompleted -= CoreWebView2_NavigationCompleted;
        core.ProcessFailed -= CoreWebView2_ProcessFailed;

        core.WebMessageReceived += CoreWebView2_WebMessageReceived;
        core.NavigationCompleted += CoreWebView2_NavigationCompleted;
        core.ProcessFailed += CoreWebView2_ProcessFailed;
    }

    /// <summary>Physical folder copied to output by csproj (src\webui\**).</summary>
    private static string GetWebUiFolderPath() =>
        Path.Combine(AppContext.BaseDirectory, "src", "webui");

    /// <summary>Entry URL under the virtual host (https://vettvgame.local/index.html).</summary>
    private static string GetWebUiUri() =>
        $"https://{WebUiVirtualHostName}/index.html";

    private void ApplyVirtualHostMapping(CoreWebView2 core)
    {
        var folder = GetWebUiFolderPath();
        if (!Directory.Exists(folder))
        {
            Debug.WriteLine($"WebView bridge: webui folder missing — {folder}");
        }

        // Allow page scripts to fetch relative assets from the mapped folder.
        core.SetVirtualHostNameToFolderMapping(
            WebUiVirtualHostName,
            folder,
            CoreWebView2HostResourceAccessKind.Allow);

        Debug.WriteLine(
            $"WebView bridge: virtual host https://{WebUiVirtualHostName}/ → {folder} (Allow)");
    }

    private void NavigateToWebUi()
    {
        _webUiReady = false;
        var uri = GetWebUiUri();
        Debug.WriteLine($"WebView bridge: Navigate → {uri}");
        GameWebView.CoreWebView2?.Navigate(uri);
    }

    private void CoreWebView2_NavigationCompleted(
        CoreWebView2 sender,
        CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
            Debug.WriteLine($"WebView bridge: navigation failed ({args.WebErrorStatus})");
            return;
        }

        _webUiReady = true;
        _autoRecoverAttempts = 0;
        _unresponsiveSignals = 0;
        _recovering = false;
        SetBridgeStatus(null);

        Debug.WriteLine("WebView bridge: NavigationCompleted — posting hostReady");
        PostToWebUi(new WebViewOutboundMessage
        {
            Action = WebViewBridgeActions.HostReady,
            Ok = true,
            Detail = "VetTVGame WebView2 host ready",
            Money = _money,
            Lemons = _lemons,
            Weather = _weather,
        });
    }

    private void CoreWebView2_ProcessFailed(
        CoreWebView2 sender,
        CoreWebView2ProcessFailedEventArgs args)
    {
        var kind = args.ProcessFailedKind;
        var reason = args.Reason;
        var exitCode = args.ExitCode;

        Debug.WriteLine(
            $"WebView bridge: ProcessFailed kind={kind} reason={reason} exitCode={exitCode}");

        // ProcessFailed can arrive off the UI thread.
        var dispatcher = DispatcherQueue;
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            _ = HandleProcessFailedAsync(kind);
        }
        else
        {
            dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                _ = HandleProcessFailedAsync(kind);
            });
        }
    }

    private async Task HandleProcessFailedAsync(CoreWebView2ProcessFailedKind kind)
    {
        _webUiReady = false;

        switch (kind)
        {
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
            case CoreWebView2ProcessFailedKind.FrameRenderProcessExited:
                await TryAutoRecoverAsync(kind, recreateControl: false);
                break;

            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                await TryAutoRecoverAsync(kind, recreateControl: true);
                break;

            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                // Fires repeatedly while hung — do not auto-reload in a loop.
                _unresponsiveSignals++;
                Debug.WriteLine(
                    $"WebView bridge: render unresponsive signal #{_unresponsiveSignals} (no auto-reload)");
                SetBridgeStatus("WebView unresponsive (waiting; see Debug Output).");
                break;

            default:
                Debug.WriteLine($"WebView bridge: ProcessFailed kind={kind} — logged only (no auto-recover)");
                break;
        }
    }

    private async Task TryAutoRecoverAsync(CoreWebView2ProcessFailedKind kind, bool recreateControl)
    {
        if (_recovering)
        {
            Debug.WriteLine($"WebView bridge: recovery already in progress — ignoring {kind}");
            return;
        }

        if (_autoRecoverAttempts >= MaxAutoRecoverAttempts)
        {
            Debug.WriteLine(
                $"WebView bridge: giving up after {_autoRecoverAttempts} auto-recover attempts (kind={kind})");
            SetBridgeStatus("WebView recovery failed — restart the app or reopen Home.");
            return;
        }

        _recovering = true;
        var attempt = _autoRecoverAttempts;
        var delay = RecoverBackoff[Math.Min(attempt, RecoverBackoff.Length - 1)];
        _autoRecoverAttempts++;

        SetBridgeStatus($"WebView recovering ({_autoRecoverAttempts}/{MaxAutoRecoverAttempts})…");
        Debug.WriteLine(
            $"WebView bridge: auto-recover attempt {_autoRecoverAttempts}/{MaxAutoRecoverAttempts} " +
            $"for {kind} (recreate={recreateControl}, backoff={delay.TotalMilliseconds}ms)");

        try
        {
            await Task.Delay(delay);

            if (recreateControl)
            {
                await RecreateWebViewControlAsync();
            }
            else
            {
                await RecoverByReloadOrNavigateAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WebView bridge: auto-recover failed — {ex.Message}");
            SetBridgeStatus("WebView recovery error (see Debug Output).");
        }
        finally
        {
            // Unblock further ProcessFailed handling; attempt counter still limits loops.
            // Successful NavigationCompleted resets _autoRecoverAttempts.
            _recovering = false;
        }
    }

    private async Task RecoverByReloadOrNavigateAsync()
    {
        var core = GameWebView.CoreWebView2;
        if (core is null)
        {
            Debug.WriteLine("WebView bridge: CoreWebView2 null — falling back to Ensure + Navigate");
            await EnsureCoreAndNavigateAsync();
            return;
        }

        WireCoreEvents(core);
        ApplyVirtualHostMapping(core);

        try
        {
            // Prefer Reload when already on the virtual host; else re-Navigate entry.
            var source = core.Source;
            if (!string.IsNullOrEmpty(source) &&
                source.Contains(WebUiVirtualHostName, StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine("WebView bridge: recovery via Reload()");
                core.Reload();
            }
            else
            {
                Debug.WriteLine("WebView bridge: recovery via Navigate(entry)");
                NavigateToWebUi();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WebView bridge: Reload/Navigate threw — {ex.Message}; Ensure+Navigate");
            await EnsureCoreAndNavigateAsync();
        }
    }

    private async Task RecreateWebViewControlAsync()
    {
        Debug.WriteLine("WebView bridge: recreating WebView2 control after BrowserProcessExited");

        var parent = GameWebView.Parent as Panel
            ?? throw new InvalidOperationException("GameWebView has no Panel parent");

        var index = parent.Children.IndexOf(GameWebView);
        var row = Grid.GetRow(GameWebView);
        var column = Grid.GetColumn(GameWebView);
        var stretchH = GameWebView.HorizontalAlignment;
        var stretchV = GameWebView.VerticalAlignment;

        parent.Children.Remove(GameWebView);
        try
        {
            GameWebView.Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WebView bridge: Close() on old control — {ex.Message}");
        }

        GameWebView = new WebView2
        {
            HorizontalAlignment = stretchH,
            VerticalAlignment = stretchV,
        };
        Grid.SetRow(GameWebView, row);
        Grid.SetColumn(GameWebView, column);

        if (index >= 0 && index <= parent.Children.Count)
        {
            parent.Children.Insert(index, GameWebView);
        }
        else
        {
            parent.Children.Add(GameWebView);
        }

        _webUiReady = false;
        // Ensure path re-applies SetVirtualHostNameToFolderMapping on the new CoreWebView2.
        await EnsureCoreAndNavigateAsync();
    }

    private void SetBridgeStatus(string? message)
    {
        void Apply()
        {
            if (string.IsNullOrEmpty(message))
            {
                BridgeStatusText.Text = string.Empty;
                BridgeStatusText.Visibility = Visibility.Collapsed;
            }
            else
            {
                BridgeStatusText.Text = message;
                BridgeStatusText.Visibility = Visibility.Visible;
            }
        }

        var dispatcher = DispatcherQueue;
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            Apply();
        }
        else
        {
            dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, Apply);
        }
    }

    private void CoreWebView2_WebMessageReceived(
        CoreWebView2 sender,
        CoreWebView2WebMessageReceivedEventArgs args)
    {
        WebViewInboundMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<WebViewInboundMessage>(args.WebMessageAsJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"WebView bridge: malformed JSON rejected — {ex.Message}");
            PostActionResult(forAction: null, ok: false, detail: "malformed JSON");
            return;
        }

        var action = message?.Action;
        if (string.IsNullOrWhiteSpace(action))
        {
            Debug.WriteLine("WebView bridge: rejected message with missing action");
            PostActionResult(forAction: null, ok: false, detail: "missing action");
            return;
        }

        switch (action)
        {
            case WebViewBridgeActions.BuyLemons:
                HandleBuyLemons(message!);
                break;
            case WebViewBridgeActions.SetWeather:
                HandleSetWeather(message!);
                break;
            case WebViewBridgeActions.CheckRaytracing:
                HandleCheckRaytracing();
                break;
            default:
                Debug.WriteLine($"WebView bridge: rejected unknown action '{action}'");
                PostActionResult(forAction: action, ok: false, detail: "unknown action");
                break;
        }
    }

    private void HandleBuyLemons(WebViewInboundMessage message)
    {
        var count = message.Count;
        Debug.WriteLine($"buyLemons: {count}");

        // Stub ledger only — no DX12 / game logic.
        _lemons += count;
        _money -= count * 2;

        PostActionResult(
            forAction: WebViewBridgeActions.BuyLemons,
            ok: true,
            detail: $"bought {count}",
            extra: new WebViewOutboundMessage
            {
                Count = count,
                Money = _money,
                Lemons = _lemons,
            });
        PushFrameState();
    }

    private void HandleSetWeather(WebViewInboundMessage message)
    {
        var intensity = message.Intensity;
        var wind = message.Wind;
        Debug.WriteLine($"setWeather: intensity={intensity}, wind={wind}");

        _weather = intensity > 0.5f ? "Storm" : "Sunny";

        PostActionResult(
            forAction: WebViewBridgeActions.SetWeather,
            ok: true,
            detail: _weather,
            extra: new WebViewOutboundMessage
            {
                Intensity = intensity,
                Wind = wind,
                Weather = _weather,
            });
        PushFrameState();
    }

    private void HandleCheckRaytracing()
    {
        Debug.WriteLine("checkRaytracing");
        _raytracing = !_raytracing;

        PostActionResult(
            forAction: WebViewBridgeActions.CheckRaytracing,
            ok: true,
            detail: _raytracing ? "raytracing on (stub)" : "raytracing off (stub)",
            extra: new WebViewOutboundMessage
            {
                Raytracing = _raytracing,
            });
    }

    /// <summary>Push HUD / frame snapshot to JS (primary C# to JS path).</summary>
    public void PushFrameState()
    {
        NotifyWebUi(new WebViewOutboundMessage
        {
            Action = WebViewBridgeActions.FrameState,
            Money = _money,
            Lemons = _lemons,
            Weather = _weather,
            Raytracing = _raytracing,
        });
    }

    /// <summary>Post an arbitrary outbound bridge message to the WebUI.</summary>
    public void NotifyWebUi(WebViewOutboundMessage message)
    {
        PostToWebUi(message);
    }

    private void PostActionResult(
        string? forAction,
        bool ok,
        string? detail,
        WebViewOutboundMessage? extra = null)
    {
        var outbound = extra ?? new WebViewOutboundMessage();
        outbound.Action = WebViewBridgeActions.ActionResult;
        outbound.ForAction = forAction;
        outbound.Ok = ok;
        outbound.Detail = detail;
        PostToWebUi(outbound);
    }

    private void PostToWebUi(WebViewOutboundMessage message)
    {
        void Send()
        {
            if (GameWebView.CoreWebView2 is null || !_webUiReady)
            {
                return;
            }

            try
            {
                var json = JsonSerializer.Serialize(message, JsonOptions);
                GameWebView.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebView bridge: PostWebMessageAsJson failed — {ex.Message}");
            }
        }

        var dispatcher = DispatcherQueue;
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            Send();
        }
        else
        {
            dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, Send);
        }
    }
}