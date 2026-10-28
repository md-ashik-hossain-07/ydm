using System;
using System.Windows;
using Microsoft.Win32;

namespace YDM.UI.Services;

public static class ThemeManager
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string RegistryValueName = "AppsUseLightTheme";

    public static bool IsDarkThemeActive { get; private set; }

    public static void ApplyTheme(string themePreference)
    {
        bool useDark = false;

        if (string.Equals(themePreference, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            useDark = true;
        }
        else if (string.Equals(themePreference, "Light", StringComparison.OrdinalIgnoreCase))
        {
            useDark = false;
        }
        else // "System"
        {
            useDark = IsSystemInDarkMode();
        }

        IsDarkThemeActive = useDark;

        string themeUri = useDark 
            ? "pack://application:,,,/YDM.UI;component/Themes/DarkTheme.xaml"
            : "pack://application:,,,/YDM.UI;component/Themes/LightTheme.xaml";

        try
        {
            var newDict = new ResourceDictionary { Source = new Uri(themeUri, UriKind.Absolute) };

            var appResources = Application.Current?.Resources;
            if (appResources == null) return;

            // Remove existing theme dictionaries
            for (int i = appResources.MergedDictionaries.Count - 1; i >= 0; i--)
            {
                var dict = appResources.MergedDictionaries[i];
                if (dict.Source != null && (dict.Source.OriginalString.Contains("LightTheme.xaml") || dict.Source.OriginalString.Contains("DarkTheme.xaml")))
                {
                    appResources.MergedDictionaries.RemoveAt(i);
                }
            }

            appResources.MergedDictionaries.Add(newDict);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply theme: {ex.Message}");
        }
    }

    public static bool IsSystemInDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key != null)
            {
                var val = key.GetValue(RegistryValueName);
                if (val is int intVal)
                {
                    return intVal == 0; // 0 = Dark mode, 1 = Light mode
                }
            }
        }
        catch { }

        return false;
    }
}