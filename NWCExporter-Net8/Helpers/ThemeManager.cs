using Autodesk.Revit.UI;
using System;
using System.Linq;
using System.Windows;

namespace NWCExporter.Helpers
{
    public enum AppTheme
    {
        Dark,
        Light
    }

    public static class ThemeManager
    {
        private const string ThemeDictionaryTag = "ACTIVE_THEME_DICTIONARY";

        public static void ApplyTheme(Window window, AppTheme theme)
        {
            string themeFile = theme == AppTheme.Dark
                ? "Themes/ThemeDark.xaml"
                : "Themes/ThemeLight.xaml";

            var uri = new Uri($"/NWCExporter;component/{themeFile}", UriKind.Relative);

            var newDict = new ResourceDictionary { Source = uri };

            
            for (int i = window.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
            {
                var d = window.Resources.MergedDictionaries[i];
                if (d.Source != null && d.Source.OriginalString.Contains("Theme"))
                    window.Resources.MergedDictionaries.RemoveAt(i);
            }

            // Insert the new theme dictionary at the beginning to ensure it takes precedence
            window.Resources.MergedDictionaries.Insert(0, newDict);
        }

        public static AppTheme DetectRevitTheme()
        {
            try
            {
                return UIThemeManager.CurrentTheme == UITheme.Dark
                    ? AppTheme.Dark
                    : AppTheme.Light;
            }
            catch
            {
                return AppTheme.Light;
            }
        }
    }
}