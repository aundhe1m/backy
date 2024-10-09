using System;
using System.Threading.Tasks;

namespace Backy.Services
{
    public interface ILoadingService
    {
        event Func<Task> OnShow;
        event Func<Task> OnHide;

        Task ShowLoading();
        Task HideLoading();
    }

    public class LoadingService : ILoadingService
    {
        public event Func<Task>? OnShow;
        public event Func<Task>? OnHide;

        public async Task ShowLoading()
        {
            if (OnShow != null)
                await OnShow.Invoke();
        }

        public async Task HideLoading()
        {
            if (OnHide != null)
                await OnHide.Invoke();
        }
    }

}
