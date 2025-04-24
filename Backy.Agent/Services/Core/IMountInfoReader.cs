using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Backy.Agent.Models;

namespace Backy.Agent.Services.Core
{
    /// <summary>
    /// Interface for reading information about mounted filesystems.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Reading mount information from /proc/mounts or similar sources
    /// - Providing structured data about mounted filesystems
    /// - Getting disk usage information for mount points
    /// - Looking up mount points by device path
    /// - Checking mount status of volumes
    /// 
    /// Centralizes all mount-related operations to avoid duplication in other services.
    /// </remarks>
    public interface IMountInfoReader
    {
        // Mount information methods will be defined here
    }
}