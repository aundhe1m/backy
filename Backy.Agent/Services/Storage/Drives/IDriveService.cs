using System.Collections.Generic;
using System.Threading.Tasks;
using Backy.Agent.Models;

namespace Backy.Agent.Services.Storage.Drives
{
    /// <summary>
    /// Interface for high-level drive operations that combines information retrieval and management.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Providing a unified API for drive operations
    /// - Exposing drive information with appropriate caching
    /// - Managing protected drive status
    /// - Handling drive operation results with proper error reporting
    /// - Enforcing business rules for drive operations
    /// 
    /// Acts as an aggregate facade over lower-level drive services.
    /// </remarks>
    public interface IDriveService
    {
        // Drive operation methods will be defined here
    }
}