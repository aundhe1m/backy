# Backy.Agent Refactoring Plan

## Current Architecture Overview

The Backy.Agent application is a .NET-based agent that provides an API for managing disk storage, RAID arrays, and drive operations. Currently, the agent heavily relies on executing command-line tools such as `mdadm`, `lsblk`, `df`, and others, and then parses their output to gather information about the system's drives, RAID pools, mount points, and processes.

This approach has several drawbacks:
- Parsing command outputs is error-prone and can break if command output formats change
- Inefficient due to process spawning for each command
- Limited real-time monitoring capabilities
- High dependency on external tools and their availability

## Refactoring Goals

1. Replace command-based information gathering with file-based monitoring where practical
2. Add resync percentage information to the `/api/v1/pools` response
3. Improve API implementation by replacing POST with DELETE for removal operations
4. Enhance code structure and maintainability
5. Remove redundant properties from API responses

## 1. Hybrid Approach: Commands and File-Based Monitoring

Based on testing, we'll adopt a hybrid approach:
- Continue using `lsblk` for basic block device information (including serial numbers)
- Implement file-based monitoring for MD arrays via `/proc/mdstat`
- Use sysfs files for additional MD array details where appropriate

### Using `lsblk` for Device Information

The `lsblk` command provides device information in a consistent JSON format that's easy to parse. It includes serial numbers, vendor information, and other details that are difficult to reliably extract from sysfs files across different hardware configurations.

#### Benefits of keeping `lsblk`:
- Consistently formatted JSON output
- Reliable access to serial numbers
- Cross-compatible with different hardware configurations
- Built-in ability to filter by device type

We'll continue using:
```bash
lsblk -J -b -o NAME,SIZE,TYPE,MOUNTPOINT,UUID,SERIAL,VENDOR,MODEL,FSTYPE,PATH,ID-LINK
```

This provides a solid foundation of device information that would be complex to extract directly from sysfs files.

### Reading from `/proc/mdstat`

The `/proc/mdstat` file contains real-time information about Linux MD (Multiple Devices) RAID arrays. We'll directly read and parse this file to get:

- List of active RAID arrays
- RAID levels
- Array states
- Resync/recovery progress
- Component devices

#### File Format and Expected Content

The `/proc/mdstat` file typically contains output that looks like this:

```
Personalities : [raid1] [linear] [multipath] [raid0] [raid6] [raid5] [raid4] [raid10]
md0 : active raid1 sdc1[0] sdb1[1]
      976759808 blocks super 1.2 [2/2] [UU]
      bitmap: 1/8 pages [4KB], 65536KB chunk

md1 : active raid1 sdd1[0] sde1[1]
      976759808 blocks super 1.2 [2/2] [UU]
      [==>.....................]  resync = 12.6% (123456789/976759808) finish=127.5min speed=111690K/sec
      bitmap: 2/8 pages [8KB], 65536KB chunk

unused devices: <none>
```

Key information to extract:
- Device names (md0, md1, etc.)
- RAID level (raid1, raid5, etc.)
- Component devices (sdc1[0], sdb1[1], etc.)
- Array status ([UU] indicates all drives are active)
- Resync/recovery percentage (when applicable)

#### Implementation Approach

The MdStatReader service should:
1. Read the `/proc/mdstat` file directly
2. Parse the content using regular expressions to extract structured data
3. Return models that represent the MD devices and their status
4. Implement caching to avoid frequent reads when information hasn't changed

### Supplementing with Specific `/sys/block/md*` Files

While `/proc/mdstat` provides a good overview, some specific information can be read directly from sysfs files for MD devices.

Key files to monitor for MD arrays:

```
/sys/block/md0/md/
├── array_state        # Current state ("clean", "active", etc.)
├── degraded           # Number of degraded devices (0 if healthy)
├── level              # RAID level (raid1, raid5, etc.) 
├── raid_disks         # Number of disks in the array
└── sync_action        # Current sync action (idle, resync, recover, etc.)
```

We'll focus on these key files rather than attempting to read all available sysfs entries.

## 2. Adding Resync Percentage to `/api/v1/pools` Response

The resync percentage will be read directly from `/proc/mdstat`, as it provides the most reliable and up-to-date information about resync progress.

### Extracting Resync Information

When a RAID array is resyncing or recovering, the `/proc/mdstat` file will contain a line like:

```
[==>.....................]  resync = 12.6% (123456789/976759808) finish=127.5min speed=111690K/sec
```

From this, we'll extract the progress percentage (12.6%) and include it in the API response.

Using regex pattern matching, we can extract:
- The percentage value
- Current operation (resync, recovery, check)
- Speed information (optional)

### Model Updates

The `PoolListItem` and `PoolDetailResponse` models will include a new property:

```
ResyncPercentage: double? (nullable double to handle case where no resync is happening)
```

This will allow the frontend to display resync progress when needed.

## 3. Improving API Implementation with DELETE for removal operations

The current API uses POST for operations that remove resources, which doesn't align with RESTful API best practices. Instead, we should use DELETE for these operations.

### Endpoints to Update

The following endpoints should change from POST to DELETE:

1. Pool removal:
   - `/api/v1/pools/guid/{poolGroupGuid}/remove` → DELETE `/api/v1/pools/guid/{poolGroupGuid}`
   - This aligns with REST conventions where DELETE is used to remove resources

2. Pool metadata removal:
   - `/api/v1/pools/metadata/remove` → DELETE `/api/v1/pools/metadata`

3. For backwards compatibility, the old POST endpoints could be kept but marked as deprecated in the Swagger documentation.

## 4. General Code Improvements

### Log Level Optimization

The current implementation doesn't effectively distinguish between debug-level and information-level logs. The logging strategy should be improved:

- **Debug Level**: Use for detailed logging that's only needed during troubleshooting
  - Command executions with full inputs and outputs
  - Parsing details for file content
  - Cache hits and misses
  - Method entry/exit for complex operations

- **Information Level**: Use for normal operational events
  - Pool creation, mounting, unmounting, removal
  - File-based monitoring state changes
  - Raid state changes
  - Startup and shutdown of services

- **Warning Level**: Use for conditions that might lead to errors
  - Failed file reads that will be retried
  - Temporary parsing issues
  - Metadata inconsistencies

- **Error Level**: Use for actual error conditions
  - File read failures
  - Parsing failures that prevent operations
  - API call failures

### File Monitoring Approach

Implement a minimal file monitoring system focused on `/proc/mdstat`:

1. **Timer-based Monitoring (Recommended Approach)**
   - Simple periodic check of `/proc/mdstat` contents (every 5-10 seconds)
   - Compare with previous content to detect changes
   - Update cached information when changes are detected
   - Simple to implement and reliable

2. **Cache Implementation**
   - Cache `/proc/mdstat` content with a short TTL (5-10 seconds)
   - Invalidate cache on timer or when file content changes
   - Provide in-memory access for rapid retrieval

3. **Graceful Fallbacks**
   - If file-based reading fails, fall back to mdadm commands
   - Log detailed diagnostics when falling back to commands
   - Cache fallback results with appropriate TTL

### Implementation Strategy

#### Phase 1: Add File-Based MD Status Reader

1. Implement `MdStatReader` to read and parse `/proc/mdstat`
2. Keep using `lsblk` for device information
3. Create caching layer for MD information
4. Implement tests with mock `/proc/mdstat` content

#### Phase 2: Update Services to Use Hybrid Approach

1. Modify `PoolService` to use the new `MdStatReader` for RAID information
2. Continue using `lsblk` in `DriveService` for device details
3. Add the resync percentage property to models and responses
4. Update the API documentation to reflect the changes

#### Phase 3: Improve API Design

1. Replace POST with DELETE for removal operations
2. Update API documentation to reflect the REST improvements

### Phase 4: Add Real-Time Monitoring

1. Implement the monitoring service for key files
2. Add notification mechanisms for changes
3. Implement caching to improve performance

## 5. Removing Redundant Properties from API Responses (Completed ✓)

As part of the refactoring effort, redundant properties have been removed from API responses to make the API more efficient and avoid duplication of information.

### Removed Properties:

1. **`driveCount` from PoolListItem model**
   - The `driveCount` property has been removed from the `/api/v1/pools` endpoint response
   - This property was redundant since the number of drives can be determined from the length of the `drives` array
   - Frontend code should be updated to use `response.drives.length` instead of `response.driveCount`

The implementation of this change involved:
- Removing the property from the `PoolListItem` class in `Models/PoolMetadata.cs`
- Removing code in `PoolService.cs` that was setting this property
- Updating the OpenAPI documentation in `PoolEndpoints.cs` to reflect this change

This change simplifies the API response and removes duplication of data while maintaining all the necessary information for the frontend.

## Recommendations for Additional Improvements

1. **Caching**: Implement a caching layer to avoid repeatedly reading the same files
2. **Event-based updates**: Use file system watchers to trigger updates when files change
3. **Graceful fallbacks**: If file-based reading fails, fall back to command execution

## Benefits of the Refactoring

1. **Improved reliability**: Reduced dependency on parsing complex command outputs
2. **Better performance**: Direct file access for MD data is faster than spawning mdadm processes
3. **Real-time monitoring**: Ability to detect RAID array changes as they happen
4. **Pragmatic approach**: Using the right tool for each job (files for MD status, lsblk for device info)
5. **Better API design**: More RESTful API with appropriate HTTP methods
6. **Enhanced features**: New capabilities like resync percentage monitoring
7. **Simplified responses**: Removal of redundant properties makes the API more efficient

## Conclusion

This refactoring plan adopts a pragmatic hybrid approach, using file-based monitoring for RAID arrays while continuing to use `lsblk` for reliable device information. This ensures we get the best of both worlds - the performance and real-time benefits of file monitoring where it works well, and the reliability of well-tested commands where they're more practical.

Additionally, the API responses have been optimized by removing redundant properties like `driveCount`, which improves efficiency without losing any functionality.

