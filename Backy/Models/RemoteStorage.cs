using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Backy.Models
{
    public class RemoteScan
    {
        [Key]
        [BindNever]
        public Guid Id { get; set; } = Guid.NewGuid();

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

        public DateTime? LastChecked { get; set; }

        public string Status { get; set; } = "Offline"; // Online/Offline

        [Required(ErrorMessage = "Authentication Method is required.")]
        [RegularExpression("Password|SSH Key", ErrorMessage = "Authentication Method must be either 'Password' or 'SSH Key'.")]
        public string AuthenticationMethod { get; set; } = "Password"; // Password or SSH Key
        public bool IsIndexing { get; set; } = false;
    }
}
