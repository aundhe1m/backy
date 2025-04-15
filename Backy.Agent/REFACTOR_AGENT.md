# Backy.Agent Refactoring Plan

## Current Architecture Overview

The Backy.Agent application is a .NET-based agent that provides an API for managing disk storage, RAID arrays, and drive operations. Currently, the agent heavily relies on executing command-line tools such as `mdadm`, `lsblk`, `df`, and others, and then parses their output to gather information about the system's drives, RAID pools, mount points, and processes.

This approach has several drawbacks:
- Parsing command outputs is error-prone and can break if command output formats change
- Inefficient due to process spawning for each command
- Limited real-time monitoring capabilities
- High dependency on external tools and their availability
- Complex code with heavy nesting and regex processing

## Refactoring Goals

1. Replace command-based information gathering with direct file reading and .NET APIs where practical
2. Fix issues with GET /api/v1/pools/{poolGroupGuid} endpoint to list all drives in the array
3. Improve overall code architecture, readability, and maintainability
4. Remove deprecated properties (poolGroupId) in favor of poolGroupGuid
5. Minimize regex usage where possible and rely on more structured data sources

## 1. Minimal Command Approach with Direct File Access

The refactored implementation will rely on direct file access and .NET APIs where possible, minimizing command execution to a few essential cases:

### Command Usage Limited To:
- `lsblk -J -b -o NAME,SIZE,TYPE,MOUNTPOINT,UUID,SERIAL,VENDOR,MODEL,FSTYPE,PATH,ID-LINK` (kept for consistent JSON output)
- `kill` (for process termination)
- `mount`, `umount` (for mounting operations)
- `wipefs` (for cleaning drives)
- `mdadm --remove` and `mdadm --create` (for RAID array manipulation)

### Direct File Reading for:
- Reading RAID array status from `/proc/mdstat`
- Getting device information from `/sys/block/*` where applicable
- Reading partition and filesystem information directly from kernel provided files

### Using .NET APIs:
- `DriveInfo` class for retrieving disk space information (TotalSize, TotalFreeSpace, AvailableFreeSpace)
- File.ReadAllTextAsync for reading system files instead of `cat` commands
- DirectoryInfo for filesystem operations where applicable
- Structured JSON parsing instead of text regex where possible

## 2. Fixing GET /api/v1/pools/{poolGroupGuid} to List All Drives

The current implementation has a flaw where only the last drive in the array is shown in the API response. This will be fixed by:

1. Improving the PoolService's GetPoolDetailByGuidAsync method to ensure all drives are properly included
2. Enhancing the drive detection logic to better map component devices to their serials
3. Ensuring consistent ordering and completeness of drive information in the response
4. Adding proper validation and logging for edge cases
5. Using the MdStatReader's array information to accurately list all drives in a pool

### Implementation Approach:
```csharp
// Sample implementation logic (conceptually)
public async Task<(bool Success, string Message, PoolDetailResponse PoolDetail)> GetPoolDetailByGuidAsync(Guid poolGroupGuid)
{
    try
    {
        // Get array information from MdStatReader
        var arrayInfo = await _mdStatReader.GetArrayInfoByGuidAsync(poolGroupGuid);
        if (arrayInfo == null)
        {
            return (false, $"Pool with GUID '{poolGroupGuid}' not found", new PoolDetailResponse());
        }
        
        var response = new PoolDetailResponse();
        
        // Add each drive to the response (fixed to include ALL drives)
        foreach (var devicePath in arrayInfo.Devices)
        {
            // Get drive details and add to response.Drives
            // ...
        }
        
        return (true, "Pool details retrieved successfully", response);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error getting pool details by GUID {PoolGroupGuid}", poolGroupGuid);
        return (false, $"Error retrieving pool details: {ex.Message}", new PoolDetailResponse());
    }
}
```

## 3. Code Architecture and Simplification

### Standalone Services and Helpers

1. **FileSystemInfoService**: Responsible for reading direct file information
   - Abstracts reading from `/proc` and `/sys` directories
   - Handles caching of file content
   - Provides structured data access

2. **DriveInfoService**: Uses .NET's DriveInfo class for disk space information
   - Wraps System.IO.DriveInfo functionality
   - Maps between Linux device paths and .NET mount points
   - Provides consistent error handling

3. **MountManager**: Handles mount/unmount operations
   - Manages mount point validation
   - Executes mount/umount commands
   - Tracks mounted filesystems

4. **PoolMetadataService**: Manages pool metadata operations
   - Handles reading, writing, and validating metadata
   - Maintains consistent GUID-based identification
   - Provides robust error handling

### Reducing Nesting and Simplifying Functions

1. **Extract helper methods** for common operations:
   - Reading `/proc/mdstat` (already improved by MdStatReader)
   - Parsing mount information
   - Executing specific system commands

2. **Use early returns** to reduce nesting:
   ```csharp
   // Instead of:
   if (condition) {
       // Many lines of nested code
   }
   
   // Use:
   if (!condition) return defaultValue;
   // Continue with main logic (not nested)
   ```

3. **Create lightweight, focused functions**:
   - Each method should do one thing well
   - Clear input and output parameters
   - Proper documentation
   - Unit testable design

### Minimizing Regex Usage

1. Replace regex with structured data sources where possible:
   - JSON output from `lsblk`
   - Structured parsing of well-defined files like `/proc/mdstat`
   - Key-value mapping from system information

2. Where regex is still needed:
   - Create pre-compiled regex patterns as static fields
   - Add clear documentation for the pattern
   - Add proper validation and error handling

## 4. Removing Deprecated poolGroupId

Replace all references to poolGroupId with poolGroupGuid throughout the codebase:

1. Update model classes to remove the deprecated property
2. Update service methods to use only GUID-based identification
3. Update API endpoints to consistently use GUIDs
4. Ensure backward compatibility during the transition period
5. Update documentation to reflect the change

## 5. Implementation Strategy

### Phase 1: Direct File Access Implementation

1. Implement FileSystemInfoService for direct file access
2. Integrate DriveInfo class usage for disk space information
3. Update MdStatReader to provide all needed information from `/proc/mdstat`
4. Create caching layer to improve performance

#### Phase 1 Implementation Summary (Completed)

The first phase of refactoring has been completed with the following improvements:

1. **New `FileSystemInfoService` Implementation**:
   - Created a dedicated service for direct file system access
   - Implemented caching mechanism with TTL from configuration
   - Added methods for reading from `/proc` and `/sys` directories
   - Included safety checks and proper error handling
   - Provided structured access to system device information

2. **New `DriveInfoService` Implementation**:
   - Added service using .NET's `DriveInfo` class for disk space information
   - Implemented fallback mechanism to command-line execution if .NET API fails
   - Created methods to check mount points and read mounted filesystems
   - Added direct reading of `/proc/mounts` instead of parsing `mount` command output
   - Improved error handling and logging

3. **Enhanced Model Classes**:
   - Added `MountInfo` model for structured information about mounted filesystems
   - Created comprehensive `MdStatInfo` and `MdArrayInfo` models to represent RAID arrays
   - Added `PoolMetadataCollection` and `PoolMetadata` models for storing persistent pool data
   - Used proper typing and documentation for all models

4. **Refactored `MdStatReader`**:
   - Switched from command execution to direct file reading
   - Implemented pre-compiled regex patterns for better performance
   - Added structured parsing of `/proc/mdstat` content
   - Improved error handling and added fallback mechanisms
   - Enhanced GUID-based pool identification
   - Added proper caching to reduce file system reads

5. **Caching Improvements**:
   - Implemented memory cache for file content with configurable TTL
   - Added cache invalidation methods
   - Used caching strategically to reduce system calls
   - Preserved cache freshness for critical system state information

6. **Program.cs Updates**:
   - Registered the new services in the dependency injection container
   - Added memory cache registration for file content caching
   - Maintained backward compatibility with existing services

These improvements provide a solid foundation for Phase 2, which will focus on refactoring the DriveService and PoolService to use these new implementations, fixing listing issues, and creating additional focused service classes.

### Phase 2: Service Refactoring

1. Refactor DriveService to use new implementations
2. Refactor PoolService to fix listing issues
3. Create new focused service classes
4. Implement helper methods to reduce code complexity

#### Phase 2 Implementation Summary (Completed)

Phase 2 of the refactoring has been completed with the following significant improvements:

1. **Refactored `DriveService`**:
   - Updated to use `MdStatReader` for array information instead of command execution and parsing
   - Integrated the new `FileSystemInfoService` and `DriveInfoService` for direct file access
   - Improved the `GetDriveStatusAsync` method to better detect drives in RAID arrays
   - Enhanced mount point detection using `DriveInfoService` for more reliable information
   - Maintained original API surface for backward compatibility
   - Improved error handling with proper logging

2. **Enhanced `PoolService`**:
   - Fixed the critical issue in `GetPoolDetailByGuidAsync` to correctly list ALL drives in a pool
   - Added code to properly track disconnected or missing drives using the pool metadata
   - Implemented explicit tracking of added drive serials using HashSets to prevent duplicates
   - Used `MdStatReader` directly with GUID lookup for more efficient array information retrieval
   - Integrated `FileSystemInfoService` for metadata file operations
   - Used `DriveInfoService` for mount information detection
   - Added better metadata handling for disconnected drives
   - Improved status reporting for drives in various states

3. **Pool Drive Listing Improvements**:
   - Fixed the drive listing issue by using a two-phase approach:
     1. First adding drives from current array information (connected drives)
     2. Then adding any drives found in metadata but not in the current array (disconnected drives)
   - Added explicit status reporting for disconnected drives
   - Improved label handling for all drives
   - Enhanced detection of drive status based on array status characters

4. **Error Handling and Code Structure**:
   - Improved error handling throughout the services
   - Reduced nested code complexity using early returns
   - Enhanced logging with context information
   - Used strongly typed collections for better code safety

5. **HashSet Handling**:
   - Fixed HashSet creation with proper string comparison for case-insensitive serial matching
   - Used appropriate string comparison options for dictionary lookups
   - Improved drive tracking using proper collections

These improvements have resolved the issue with the GET /api/v1/pools/{poolGroupGuid} endpoint, which now correctly lists all drives in a pool, including those that are disconnected or missing. The code is now more maintainable, uses fewer system commands, and provides more accurate information about the system state.

### Phase 3: API and Model Updates

1. Fix GET /api/v1/pools/{poolGroupGuid} endpoint
2. Remove poolGroupId from models
3. Update API documentation to reflect changes
4. Ensure backward compatibility where needed

#### Phase 3 Implementation Summary (Completed)

Phase 3 of the refactoring has been successfully completed, focusing on API and model updates to fully remove the deprecated poolGroupId in favor of poolGroupGuid. The following key improvements were made:

1. **Updated Model Classes**:
   - Completely removed the deprecated `PoolGroupId` property from the `PoolMetadata` class
   - Ensured all model classes exclusively use `poolGroupGuid` for pool identification
   - Updated related API models to maintain consistency with the GUID-based approach
   - Fixed documentation and comments to reflect the GUID-only identification system

2. **Refactored PoolService Interface**:
   - Updated the `IPoolService` interface to remove methods that referenced the old integer-based ID
   - Maintained only the GUID-based methods for API operations
   - Ensured consistent method signatures throughout the service

3. **Updated Service Implementation**:
   - Removed deprecated methods such as `GetPoolMetadataByGroupIdAsync` that used the old ID system
   - Removed the overloaded version of `ResolveMdDeviceAsync` that accepted both ID and GUID parameters
   - Updated the `SavePoolMetadataAsync` method to remove references to `poolGroupId` when determining which metadata entries to replace
   - Fixed error in logging message parameters that was causing build warnings

4. **Enhanced GUID-based Pool Management**:
   - Updated all pool management logic to work exclusively with GUIDs
   - Ensured the metadata system properly handles GUID-based identification across system reboots
   - Maintained backward compatibility in the API while removing legacy code

5. **Code Quality Improvements**:
   - Fixed a CA2017 warning related to logging parameter mismatch
   - Improved readability by removing conditional logic that referenced the deprecated ID
   - Enhanced comments and code documentation to match the current implementation

These improvements complete our transition to a fully GUID-based pool identification system, which provides more consistent and reliable pool identification across system reboots. The code is now cleaner, more maintainable, and follows modern best practices for unique identifier management.

### Phase 4: Testing and Validation

1. Create unit tests for new services
2. Validate behavior with real-world scenarios
3. Perform regression testing
4. Document any behavior changes

## Benefits of the Refactoring

1. **Improved reliability**: Less dependency on parsing complex command outputs
2. **Better performance**: Direct file access is faster than spawning processes
3. **Enhanced maintainability**: Cleaner code structure and better organization
4. **Reduced complexity**: Simpler functions and less nested code
5. **Better API consistency**: Fixed endpoints and consistent GUID usage
6. **Future-proof design**: More resilient to system changes
7. **Better testability**: Well-structured code is easier to test

## Conclusion

This refactoring plan takes a pragmatic approach to improving the Backy.Agent codebase. It focuses on reducing command usage in favor of direct file access and .NET APIs, fixing specific API issues, and improving overall code structure. The hybrid approach retains `lsblk` for its valuable JSON output while eliminating unnecessary command executions. By creating specialized services and simplifying the code, the system will be more maintainable, reliable, and performant.

