using Backy.Agent.Models;

namespace Backy.Agent.Services;

public interface IMdStatReader
{
    /// <summary>
    /// Gets the current MD array information from /proc/mdstat
    /// </summary>
    /// <returns>Information about all MD arrays on the system</returns>
    Task<MdStatInfo> GetMdStatInfoAsync();
    
    /// <summary>
    /// Gets information about a specific MD array
    /// </summary>
    /// <param name="deviceName">The device name (e.g., 'md0')</param>
    /// <returns>Information about the specified MD array, or null if not found</returns>
    Task<MdArrayInfo?> GetArrayInfoAsync(string deviceName);
    
    /// <summary>
    /// Gets information about an array by its GUID
    /// </summary>
    /// <param name="poolGroupGuid">The pool group GUID</param>
    /// <returns>Information about the MD array associated with the GUID, or null if not found</returns>
    Task<MdArrayInfo?> GetArrayInfoByGuidAsync(Guid poolGroupGuid);
}