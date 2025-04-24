using System.Collections.Generic;
using System.Threading.Tasks;
using Backy.Agent.Models;

namespace Backy.Agent.Services.Process
{
    /// <summary>
    /// Interface for terminating system processes.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Terminating individual processes by PID
    /// - Terminating groups of processes with options
    /// - Controlling process termination signals (graceful vs forced)
    /// - Validating process termination with retries
    /// - Reporting termination results
    /// 
    /// Provides a safe abstraction for process termination operations.
    /// </remarks>
    public interface IProcessKillService
    {
        // Process termination methods will be defined here
    }
}