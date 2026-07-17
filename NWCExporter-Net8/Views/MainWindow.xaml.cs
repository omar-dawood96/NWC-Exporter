using NWCExporter.Helpers;
using NWCExporter.ViewModels;
using System.Windows;

namespace NWCExporter.Views
{
    public partial class MainWindow : Window
    {
        private AppTheme _currentTheme;

        public MainWindow(MainViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;

            _currentTheme = ThemeManager.DetectRevitTheme();
            ThemeManager.ApplyTheme(this, _currentTheme);
        }

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            _currentTheme = _currentTheme == AppTheme.Dark
                ? AppTheme.Light
                : AppTheme.Dark;

            ThemeManager.ApplyTheme(this, _currentTheme);
        }
    }
}