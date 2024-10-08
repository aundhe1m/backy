using Backy.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Backy.Services
{
    public interface ICustomToastService
    {
        event Action<ToastMessage> OnShow;
        void ShowSuccess(string message, string title = "Success");
        void ShowError(string message, string title = "Error");
        void ShowWarning(string message, string title = "Warning");
        void ShowInfo(string message, string title = "Info");
    }
}
