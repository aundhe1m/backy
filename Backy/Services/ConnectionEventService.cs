using System;

namespace Backy.Services
{
    /// <summary>
    /// Service that broadcasts changes to RemoteConnection objects 
    /// to all listening components in Blazor Server.
    /// </summary>
    public class ConnectionEventService
    {
        // Event is raised whenever a RemoteConnection is updated
        public event Action<Guid>? ConnectionUpdated;

        /// <summary>
        /// Call this method to notify *all* subscribers 
        /// that the specified RemoteConnection has updated.
        /// </summary>
        public void NotifyConnectionUpdated(Guid remoteConnectionId)
        {
            ConnectionUpdated?.Invoke(remoteConnectionId);
        }
    }
}
