using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backy.Models
{
    public class RemoteScanSchedule
    {
        [Key]
        public int Id { get; set; } // required

        [Required]
        public Guid RemoteConnectionId { get; set; } // required

        public bool SelectedDayMonday { get; set; } = false;
        public bool SelectedDayTuesday { get; set; } = false;
        public bool SelectedDayWednesday { get; set; } = false;
        public bool SelectedDayThursday { get; set; } = false;
        public bool SelectedDayFriday { get; set; } = false;
        public bool SelectedDaySaturday { get; set; } = false;
        public bool SelectedDaySunday { get; set; } = false;

        public int TimeOfDayMinutes { get; set; } = 0; // Minutes since midnight

        // Add this property
        [NotMapped]
        public string TimeOfDayString
        {
            get => TimeSpan.FromMinutes(TimeOfDayMinutes).ToString(@"hh\:mm");
            set
            {
                if (TimeSpan.TryParse(value, out var time))
                {
                    TimeOfDayMinutes = (int)time.TotalMinutes;
                }
            }
        }

        // Navigation property
        public RemoteConnection RemoteConnection { get; set; } = default!;
    }
}
