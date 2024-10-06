using System;
using System.Threading.Tasks;

namespace Backy.Services
{
    public interface IThemeService
    {
        event Func<Task> OnThemeChanged;
        Task ToggleTheme();
        Task SetDarkMode(bool isDark);
        bool IsDarkMode { get; }
    }

    public class ThemeService : IThemeService
    {
        public event Func<Task>? OnThemeChanged;

        public bool IsDarkMode { get; private set; } = false;

        public async Task ToggleTheme()
        {
            IsDarkMode = !IsDarkMode;
            if (OnThemeChanged != null)
                await OnThemeChanged.Invoke();
        }

        public async Task SetDarkMode(bool isDark)
        {
            IsDarkMode = isDark;
            if (OnThemeChanged != null)
                await OnThemeChanged.Invoke();
        }
    }
}
