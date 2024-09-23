namespace Backy.Models
{
    public class IndexSchedule
    {
        public int Id { get; set; }
        public Guid RemoteStorageId { get; set; }
        public RemoteStorage? RemoteStorage { get; set; }
        public int DayOfWeek { get; set; } // 0 = Sunday, 6 = Saturday
        public int TimeOfDayMinutes { get; set; } // Minutes since midnight
    }
}
