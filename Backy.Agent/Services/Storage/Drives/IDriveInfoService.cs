using System.Collections.Generic;
using System.Threading.Tasks;
using Backy.Agent.Models;

namespace Backy.Agent.Services.Storage.Drives
{
    /// <summary>
    /// Interface for retrieving information about physical drives in the system.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Retrieving information about physical drives with caching support
    /// - Providing detailed drive information including partitions
    /// - Looking up drives by various identifiers (serial, disk-id, device path)
    /// - Checking drive usage status
    /// - Managing drive information cache
    /// 
    /// Provides a consistent API for drive information with clear cache control.
    /// </remarks>
    public interface IDriveInfoService
    {
        // Drive information methods will be defined here
    }
}