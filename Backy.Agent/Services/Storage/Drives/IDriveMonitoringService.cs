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
        /// <summary>
        /// Event triggered when drives change (added, removed, or modified)
        /// </summary>
        event EventHandler<DriveChangeEventArgs>? DriveChanged;
        
        /// <summary>
        /// Initializes the drive mapping by scanning all drives
        /// </summary>
        /// <returns>True if initialization was successful</returns>
        Task<bool> InitializeDriveMapAsync();
        
        /// <summary>
        /// Refreshes the drive mapping by rescanning all drives
        /// </summary>
        /// <param name="force">Whether to force a refresh even if one is already in progress</param>
        /// <returns>True if refresh was successful or already in progress</returns>
        Task<bool> RefreshDrivesAsync(bool force = false);
        
        /// <summary>
        /// Gets the current drive mapping
        /// </summary>
        /// <returns>The current drive mapping</returns>
        DriveMapping GetDriveMapping();
        
        /// <summary>
        /// The time when drives were last refreshed
        /// </summary>
        DateTime LastRefreshTime { get; }
        
        /// <summary>
        /// Whether a refresh operation is currently in progress
        /// </summary>
        bool IsRefreshing { get; }
    }
}