namespace Backy.Models
{
    public class ToastMessage
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public ToastLevel Level { get; set; } = ToastLevel.Info;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public enum ToastLevel
    {
        Success,
        Error,
        Warning,
        Info
    }
}
