using Backy.Models;
using System;

namespace Backy.Services
{
    public class CustomToastService : ICustomToastService
    {
        public event Action<ToastMessage>? OnShow;

        public void ShowSuccess(string message, string title = "Success")
        {
            ShowToast(new ToastMessage
            {
                Title = title,
                Message = message,
                Level = ToastLevel.Success
            });
        }

        public void ShowError(string message, string title = "Error")
        {
            ShowToast(new ToastMessage
            {
                Title = title,
                Message = message,
                Level = ToastLevel.Error
            });
        }

        public void ShowWarning(string message, string title = "Warning")
        {
            ShowToast(new ToastMessage
            {
                Title = title,
                Message = message,
                Level = ToastLevel.Warning
            });
        }

        public void ShowInfo(string message, string title = "Info")
        {
            ShowToast(new ToastMessage
            {
                Title = title,
                Message = message,
                Level = ToastLevel.Info
            });
        }

        private void ShowToast(ToastMessage toast)
        {
            OnShow?.Invoke(toast);
        }
    }
}
