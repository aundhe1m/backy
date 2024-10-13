
using System;
using System.Threading.Tasks;

namespace Backy.Services
{
    public enum Theme
    {
        Dark,
        Light
    }

    public class ThemeService
    {
        private Theme _currentTheme = Theme.Dark; // Default theme

        public Theme CurrentTheme => _currentTheme;

        public event Action? OnThemeChanged;

        public void ToggleTheme()
        {
            _currentTheme = _currentTheme == Theme.Dark ? Theme.Light : Theme.Dark;
            NotifyThemeChanged();
        }

        public void SetTheme(Theme theme)
        {
            if (_currentTheme != theme)
            {
                _currentTheme = theme;
                NotifyThemeChanged();
            }
        }

        private void NotifyThemeChanged()
        {
            OnThemeChanged?.Invoke();
        }
    }
}