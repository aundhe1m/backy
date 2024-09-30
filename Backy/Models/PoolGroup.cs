using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Backy.Models
{
    public class PoolGroup
    {
        [Key]
        public int PoolGroupId { get; set; }  // EF will make it the primary key
        public string GroupLabel { get; set; } = "Unnamed Group";
        public bool PoolEnabled { get; set; } = false;

        public List<Drive> Drives { get; set; } = new List<Drive>();
    }
}
