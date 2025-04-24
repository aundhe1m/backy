using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Backy.Agent.Models;

namespace Backy.Agent.Services.Storage.Drives
{
    /// <summary>
    /// Interface for drive monitoring service that provides event-based notifications of drive changes.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Watching for changes to drives in the system
    /// - Maintaining a mapping between various drive identifiers
    /// - Providing drive change notifications via events
    /// - Maintaining a cache of drive information
    /// - Refreshing drive information in response to system changes
    /// 
    /// Replaces the timer-based polling approach with more efficient event-based monitoring.
    /// </remarks>
    public interface IDriveMonitoringService
    {
        // Drive monitoring methods will be defined here
    }
}