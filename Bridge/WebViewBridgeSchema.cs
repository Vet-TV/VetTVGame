using System.Text.Json.Serialization;

namespace VetTVGame.Bridge;

/// <summary>Explicit WebView2 bridge action names (JS ↔ C#).</summary>
public static class WebViewBridgeActions
{
    // Inbound: JS → C#
    public const string BuyLemons = "buyLemons";
    public const string SetWeather = "setWeather";
    public const string CheckRaytracing = "checkRaytracing";

    // Outbound: C# → JS
    public const string HostReady = "hostReady";
    public const string ActionResult = "actionResult";
    public const string FrameState = "frameState";
}

/// <summary>Inbound message from chrome.webview.postMessage.</summary>
public sealed class WebViewInboundMessage
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("intensity")]
    public float Intensity { get; set; }

    [JsonPropertyName("wind")]
    public float Wind { get; set; }
}

/// <summary>Outbound message posted via PostWebMessageAsJson.</summary>
public sealed class WebViewOutboundMessage
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    /// <summary>For actionResult: which inbound action this answers.</summary>
    [JsonPropertyName("forAction")]
    public string? ForAction { get; set; }

    [JsonPropertyName("ok")]
    public bool? Ok { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("money")]
    public int? Money { get; set; }

    [JsonPropertyName("lemons")]
    public int? Lemons { get; set; }

    [JsonPropertyName("weather")]
    public string? Weather { get; set; }

    [JsonPropertyName("count")]
    public int? Count { get; set; }

    [JsonPropertyName("intensity")]
    public float? Intensity { get; set; }

    [JsonPropertyName("wind")]
    public float? Wind { get; set; }

    [JsonPropertyName("raytracing")]
    public bool? Raytracing { get; set; }
}
