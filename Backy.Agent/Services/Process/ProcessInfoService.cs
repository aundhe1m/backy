using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Backy.Agent.Models;
using Backy.Agent.Services.Core;

namespace Backy.Agent.Services.Process
{
    /// <summary>
    /// Provides information about system processes with specialized queries.
    /// </summary>
    /// <remarks>
    /// This service:
    /// - Retrieves information about running processes
    /// - Finds processes using specified resources (files, devices, etc.)
    /// - Filters processes by name, user, or resource usage
    /// - Provides detailed process statistics and command lines
    /// - Efficiently queries process information from the system
    /// 
    /// Uses the SystemCommandService for executing process queries,
    /// centralizing all process-related information retrieval.
    /// </remarks>
    public class ProcessInfoService : IProcessInfoService
    {
        private readonly ILogger<ProcessInfoService> _logger;
        private readonly ISystemCommandService _commandService;
        
        public ProcessInfoService(
            ILogger<ProcessInfoService> logger,
            ISystemCommandService commandService)
        {
            _logger = logger;
            _commandService = commandService;
        }
        
        // Implementation of process information methods will go here
    }
}