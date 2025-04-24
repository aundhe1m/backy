using System.Threading.Tasks;

namespace Backy.Agent.Services.Core
{
    /// <summary>
    /// Interface for executing system commands and returning structured results.
    /// Centralizes all external command execution in the application.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Executing shell commands in a controlled manner
    /// - Handling command execution errors
    /// - Providing standardized output format
    /// - Supporting both synchronous and asynchronous command execution
    /// - Process management (checking if running, killing)
    /// </remarks>
    public interface ISystemCommandService
    {
        // Command execution methods to be defined here
    }
}