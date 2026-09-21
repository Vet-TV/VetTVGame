// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace VetTVGame.Pages;

public sealed partial class SettingsPage : Page
{
    public bool IsDarkMode
    {
        get => (App.MainWindow?.Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;
        set => ApplyTheme(value);
    }

    public SettingsPage()
    {
        InitializeComponent();
    }

    private void ThemeToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ApplyTheme(((ToggleSwitch)sender).IsOn);
    }

    private void ThemeToggle_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ThemeToggle.IsOn = IsDarkMode;
    }

    private static void ApplyTheme(bool isDarkMode)
    {
        if (App.MainWindow?.Content is FrameworkElement root)
        {
            root.RequestedTheme = isDarkMode ? ElementTheme.Dark : ElementTheme.Light;
        }
    }
}
