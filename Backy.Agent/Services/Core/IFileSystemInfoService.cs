using System.Collections.Generic;
using System.Threading.Tasks;

namespace Backy.Agent.Services.Core
{
    /// <summary>
    /// Interface for file system operations with caching and error handling support.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Reading and writing files with caching capabilities
    /// - Checking for file and directory existence
    /// - Listing directory contents
    /// - Reading from special system directories like /proc and /sys
    /// - Managing file operation caching for improved performance
    /// - Watching directories for changes
    /// 
    /// This interface centralizes all file system access, enabling consistent caching,
    /// error handling, and logging across the application.
    /// </remarks>
    public interface IFileSystemInfoService
    {
        // File system operation methods will be defined here
    }
}