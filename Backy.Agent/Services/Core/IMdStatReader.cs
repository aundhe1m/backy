using System.Threading.Tasks;
using System.Collections.Generic;
using Backy.Agent.Models;

namespace Backy.Agent.Services.Core
{
    /// <summary>
    /// Interface for reading and interpreting MD RAID status from the system.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Reading /proc/mdstat and other MD-related status files
    /// - Parsing the status information into structured data
    /// - Providing information about MD arrays, their states, and component devices
    /// - Monitoring MD array health and status changes
    /// 
    /// This allows the application to get consistent, well-structured RAID information
    /// without duplicating complex parsing logic across multiple services.
    /// </remarks>
    public interface IMdStatReader
    {
        // MD stat reading methods will be defined here
    }
}