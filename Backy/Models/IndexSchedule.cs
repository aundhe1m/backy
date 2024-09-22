namespace Backy.Models
{
    public class IndexSchedule
    {
        public int Id { get; set; }
        public int RemoteStorageId { get; set; }
        public RemoteStorage? RemoteStorage { get; set; } // Made nullable
        public int DayOfWeek { get; set; } // 0 = Sunday, 6 = Saturday
        public int TimeOfDayMinutes { get; set; } // Minutes since midnight
    }
}
