using Microsoft.UI.Xaml.Controls;
using System.Text.Json;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace VetTVGame.Pages;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        GameWebView.Loaded += GameWebView_Loaded;
    }

    private async void GameWebView_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        GameWebView.Loaded -= GameWebView_Loaded;

        await GameWebView.EnsureCoreWebView2Async();
        GameWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

        var webUiPath = Path.Combine(AppContext.BaseDirectory, "src", "webui", "index.html");
        GameWebView.CoreWebView2.Navigate(new Uri(webUiPath).AbsoluteUri);
    }

    private void CoreWebView2_WebMessageReceived(
        Microsoft.Web.WebView2.Core.CoreWebView2 sender,
        Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs args)
    {
        using var message = JsonDocument.Parse(args.WebMessageAsJson);
        var root = message.RootElement;

        if (!root.TryGetProperty("action", out var actionProperty))
        {
            return;
        }

        switch (actionProperty.GetString())
        {
            case "buyLemons":
                var count = root.TryGetProperty("count", out var countProperty)
                    ? countProperty.GetInt32()
                    : 0;
                System.Diagnostics.Debug.WriteLine($"buyLemons: {count}");
                break;
            case "setWeather":
                var intensity = root.TryGetProperty("intensity", out var intensityProperty)
                    ? intensityProperty.GetSingle()
                    : 0;
                var wind = root.TryGetProperty("wind", out var windProperty)
                    ? windProperty.GetSingle()
                    : 0;
                System.Diagnostics.Debug.WriteLine($"setWeather: intensity={intensity}, wind={wind}");
                break;
            case "checkRaytracing":
                System.Diagnostics.Debug.WriteLine("checkRaytracing");
                break;
        }
    }
}
