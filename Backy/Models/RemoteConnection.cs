using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backy.Models
{
    public class RemoteConnection
    {
        [Key]
        public Guid RemoteConnectionId { get; set; } = Guid.NewGuid();

        [Required(ErrorMessage = "Name is required.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Host is required.")]
        public string Host { get; set; } = string.Empty;

        [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535.")]
        public int Port { get; set; } = 22;

        [Required(ErrorMessage = "Username is required.")]
        public string Username { get; set; } = string.Empty;

        public string? Password { get; set; } // Encrypted

        public string? SSHKey { get; set; } // Encrypted SSH Key

        [Required(ErrorMessage = "Remote Path is required.")]
        public string RemotePath { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = true;

        public DateTimeOffset? LastChecked { get; set; }

        public bool IsOnline { get; set; } = false;

        public enum AuthMethod
        {
            Password,
            SSHKey
        }

        [Required(ErrorMessage = "Authentication Method is required.")]
        [Column(TypeName = "varchar(20)")]
        public AuthMethod AuthenticationMethod { get; set; } = AuthMethod.Password;

        public bool ScanningActive { get; set; } = false;

        // Navigation properties
        public ICollection<RemoteScanSchedule> ScanSchedules { get; set; } = new List<RemoteScanSchedule>();
        public ICollection<RemoteFile> RemoteFiles { get; set; } = new List<RemoteFile>();
        public ICollection<RemoteFilter> Filters { get; set; } = new List<RemoteFilter>();
    }
}
