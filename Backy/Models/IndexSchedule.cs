namespace Backy.Models
{
    public class IndexSchedule
    {
        public int Id { get; set; }
        public Guid RemoteScanId { get; set; }
        public RemoteScan? RemoteScan { get; set; }
        public int DayOfWeek { get; set; } // 0 = Sunday, 6 = Saturday
        public int TimeOfDayMinutes { get; set; } // Minutes since midnight
    }
}
