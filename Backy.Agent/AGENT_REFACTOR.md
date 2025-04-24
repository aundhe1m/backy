# Backy.Agent Service Layer Refactoring Guide

## Table of Contents
1. [Current Issues](#current-issues)
2. [Proposed Architecture](#proposed-architecture)
3. [Implementation Principles](#implementation-principles)
   - [Interface-Based Design](#1-interface-based-design)
   - [Constructor Dependency Injection](#2-constructor-dependency-injection)
   - [Consistent Error Handling](#3-consistent-error-handling)
   - [Result Pattern](#4-result-pattern)
4. [Service Descriptions](#service-descriptions)
   - [Core Services](#core-services)
   - [Storage Services](#storage-services)
   - [Process Services](#process-services)
5. [Implementation Details](#implementation-details)
   - [Enhanced Drive Monitoring Approach](#enhanced-drive-monitoring-approach)
   - [Using .NET APIs for Pool Size Information](#using-net-apis-for-pool-size-information)
6. [Step-by-Step Refactoring Plan](#step-by-step-refactoring-plan)
7. [Model Enhancements](#model-enhancements)
8. [Dependency Registration](#dependency-registration)
9. [Error Handling Strategy](#error-handling-strategy)
10. [Event-Based Architecture](#event-based-architecture)
11. [Caching Strategy](#caching-strategy)
12. [Interface Organization](#interface-organization)
13. [Command Execution Strategy](#command-execution-strategy)
14. [Transaction Pattern for Pool Operations](#transaction-pattern-for-pool-operations)
15. [Additional Pool Management Enhancements](#additional-enhancements-for-robust-pool-management)
16. [Benefits of This Refactor](#benefits-of-this-refactor)
17. [Conclusion](#conclusion)

## Current Issues

After analyzing the current Backy.Agent codebase, I've identified several structural issues in the Services layer:

1. **Lack of Clear Separation of Concerns**: Services have mixed responsibilities, making them difficult to maintain and test
2. **Tight Coupling**: Many services depend directly on concrete implementations rather than abstractions
3. **Inconsistent Error Handling**: Error handling patterns vary across services
4. **Missing Organized Structure**: Related services are not grouped logically
5. **Direct Dependency on System Calls**: System command execution is spread across multiple services
6. **Inefficient Drive Information Gathering**: Repeatedly running `lsblk` for all drives when only targeted information is needed
7. **Timer-Based Refreshes Instead of Event-Based**: Using scheduled timers rather than responding to actual system changes
8. **Insufficient Logging Standardization**: Logging approaches vary between services

## Proposed Architecture

I recommend refactoring the Services folder into a more logical and maintainable structure:

```
Services/
├── Core/                       # Core infrastructure services
│   ├── ISystemCommandService.cs        # Command execution abstraction
│   ├── SystemCommandService.cs         # Implementation of system command execution
│   ├── IFileSystemInfoService.cs       # File system operations abstraction
│   ├── FileSystemInfoService.cs        # Implementation of file operations
│   ├── IMdStatReader.cs                # RAID status reading abstraction
│   ├── MdStatReader.cs                 # Implementation of RAID status reader
│   ├── IMountInfoReader.cs             # Mount point information reader 
│   └── MountInfoReader.cs              # Implementation of mount point reader
│
├── Storage/                    # Drive and storage management
│   ├── Drives/                         # Drive-related services
│   │   ├── IDriveInfoService.cs        # Drive information abstraction
│   │   ├── DriveInfoService.cs         # Implementation of drive info operations
│   │   ├── IDriveService.cs            # Drive operations abstraction
│   │   ├── DriveService.cs             # Implementation of drive operations
│   │   ├── IDriveMonitoringService.cs  # Drive monitoring abstraction
│   │   └── DriveMonitoringService.cs   # Event-based drive monitoring service
│   │
│   ├── Pools/                          # Pool-related services
│   │   ├── IPoolService.cs             # Pool operations abstraction
│   │   ├── PoolService.cs              # Implementation of pool operations
│   │   ├── IPoolInfoService.cs         # Pool information abstraction
│   │   ├── PoolInfoService.cs          # Implementation of pool info operations
│   │   ├── IPoolOperationManager.cs    # Pool operation management abstraction
│   │   ├── PoolOperationManager.cs     # Implementation of pool operation manager
│   │   └── PoolOperationCleanupService.cs # Background cleanup service for pool operations
│   │
│   └── Metadata/                       # Metadata management services
│       ├── IPoolMetadataService.cs     # Pool metadata operations abstraction
│       ├── PoolMetadataService.cs      # Implementation of pool metadata operations
│       └── PoolMetadataValidationService.cs # Background validation service
│
├── Process/                    # Process information and management
│   ├── IProcessInfoService.cs          # Process information reader
│   ├── ProcessInfoService.cs           # Implementation of process info reader
│   ├── IProcessKillService.cs          # Process termination service
│   └── ProcessKillService.cs           # Implementation of process termination
│
└── Mock/                       # Mock services for testing/development (future consideration)
```

## Implementation Principles

### 1. Interface-Based Design

Each service should have a clearly defined interface:

```csharp
public interface IDriveService
{
    Task<Result<LsblkOutput>> GetDrivesAsync();
    Task<Result<DriveStatus>> GetDriveStatusAsync(string deviceId);
    Task<Result<bool>> RefreshDrivesCacheAsync(bool force = false);
    // Other drive-related operations
}

public class DriveService : IDriveService
{
    // Implementation
}
```

### 2. Constructor Dependency Injection

Services should accept dependencies through constructor injection:

```csharp
public class DriveService : IDriveService
{
    private readonly ILogger<DriveService> _logger;
    private readonly ISystemCommandService _commandService;
    private readonly IDriveMonitoringService _driveMonitoringService;
    
    public DriveService(
        ILogger<DriveService> logger,
        ISystemCommandService commandService,
        IDriveMonitoringService driveMonitoringService)
    {
        _logger = logger;
        _commandService = commandService;
        _driveMonitoringService = driveMonitoringService;
    }
    
    // Methods
}
```

### 3. Consistent Error Handling

Standardize error handling across services:

```csharp
public async Task<Result<DriveStatus>> GetDriveStatusAsync(string deviceId)
{
    try
    {
        // Implementation
        return Result<DriveStatus>.Success(driveStatus);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to get status for drive {DeviceId}", deviceId);
        return Result<DriveStatus>.Error($"Failed to get drive status: {ex.Message}");
    }
}
```

### 4. Result Pattern

Adopt a standard Result pattern for all service methods:

```csharp
public class Result<T>
{
    public bool Success { get; private set; }
    public T? Data { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;
    
    public static Result<T> Success(T data) => new() { Success = true, Data = data };
    public static Result<T> Error(string message) => new() { Success = false, ErrorMessage = message };
}
```

## Service Descriptions

### Core Services

#### `ISystemCommandService`
Responsible for executing system commands and returning structured results. Centralizes all external command execution.

```csharp
public interface ISystemCommandService
{
    Task<CommandResult> ExecuteCommandAsync(string command, bool sudo = false);
    Task<CommandResult> ExecuteCommandWithOutputAsync(string command, bool sudo = false);
    Task<bool> IsProcessRunningAsync(int pid);
    Task<bool> KillProcessAsync(int pid, bool force = false);
}
```

#### `IFileSystemInfoService`
Handles all file system operations with proper caching and error handling.

```csharp
public interface IFileSystemInfoService
{
    Task<string> ReadFileAsync(string filePath, bool useCacheIfAvailable = true);
    Task<bool> WriteFileAsync(string filePath, string content);
    Task<bool> FileExistsAsync(string filePath);
    Task<bool> DirectoryExistsAsync(string dirPath);
    Task<IEnumerable<string>> GetDirectoriesAsync(string dirPath);
    Task<IEnumerable<string>> GetFilesAsync(string dirPath);
    Task WatchDirectoryForChangesAsync(string dirPath, Action onChange);
    void InvalidateFileCache(string filePath);
}
```

#### `IMountInfoReader`
Provides information about mounted filesystems.

```csharp
public interface IMountInfoReader
{
    Task<IEnumerable<MountInfo>> GetMountPointsAsync();
    Task<MountInfo?> GetMountInfoAsync(string mountPath);
    Task<DriveInfo> GetDriveInfoAsync(string mountPath);
}
```

### Storage Services

#### `IDriveMonitoringService`
Monitors for drive changes using an event-based approach rather than timer-based.

```csharp
public interface IDriveMonitoringService
{
    Task<bool> InitializeDriveMapAsync();
    Task<bool> RefreshDrivesAsync(bool force = false);
    event EventHandler<DriveChangeEventArgs> DriveChanged;
    DriveMapping GetDriveMapping();
    DateTime LastRefreshTime { get; }
    bool IsRefreshing { get; }
}
```

#### `IDriveInfoService`
Provides information about physical drives in the system, with clear separation of cached vs fresh data retrieval.

```csharp
public interface IDriveInfoService
{
    // Get all drives (default uses cache)
    Task<IEnumerable<DriveInfo>> GetAllDrivesAsync(bool useCache = true);
    
    // Get a specific drive by ID (default uses cache)
    Task<DriveInfo?> GetDriveByDiskIdNameAsync(string diskIdName, bool useCache = true);
    
    // Get a specific drive by serial (default uses cache)
    Task<DriveInfo?> GetDriveBySerialAsync(string serial, bool useCache = true);
    
    // Get detailed drive info (default uses cache)
    Task<DriveDetailInfo?> GetDetailedDriveInfoAsync(string diskIdName, bool useCache = true);
    
    // Check if a drive is in use
    Task<bool> IsDriveInUseAsync(string diskIdName);
    
    // Clear cache for a specific drive
    void InvalidateDriveCache(string diskIdName);
    
    // Clear entire drive cache
    void InvalidateAllDriveCache();
}
```

#### `IPoolInfoService`
Provides size and usage information about storage pools using .NET APIs instead of external commands where possible.

```csharp
public interface IPoolInfoService
{
    Task<Result<PoolSizeInfo>> GetPoolSizeInfoAsync(Guid poolGroupGuid);
    Task<Result<IEnumerable<PoolSizeInfo>>> GetAllPoolSizesAsync();
    Task<Result<PoolHealthInfo>> GetPoolHealthInfoAsync(Guid poolGroupGuid);
    Task<Result<PoolDetailInfo>> GetPoolDetailInfoAsync(Guid poolGroupGuid);
}
```

#### `IPoolService`
Manages RAID pools and their operations.

```csharp
public interface IPoolService
{
    Task<Result<IEnumerable<PoolInfo>>> GetPoolsAsync();
    Task<Result<PoolCreationResponse>> CreatePoolAsync(PoolCreationRequest request);
    Task<Result<CommandResponse>> MountPoolAsync(Guid poolGroupGuid, string? mountPath = null);
    Task<Result<CommandResponse>> UnmountPoolAsync(Guid poolGroupGuid);
    Task<Result<CommandResponse>> RemovePoolAsync(Guid poolGroupGuid);
    Task<Result<CommandResponse>> ForceAddDriveToPoolAsync(string deviceId, Guid poolGroupGuid);
}
```

### Process Services

#### `IProcessInfoService`
Provides information about running processes.

```csharp
public interface IProcessInfoService
{
    Task<IEnumerable<ProcessInfo>> GetProcessesUsingPathAsync(string path);
    Task<bool> IsProcessRunningAsync(int pid);
    Task<ProcessDetailInfo?> GetProcessInfoAsync(int pid);
}
```

#### `IProcessKillService`
Handles termination of processes.

```csharp
public interface IProcessKillService
{
    Task<Result<bool>> KillProcessAsync(int pid, bool force = false);
    Task<Result<KillResponse>> KillProcessesAsync(IEnumerable<int> pids, bool force = false);
    Task<Result<KillResponse>> KillProcessesUsingPathAsync(string path, bool force = false);
}
```

## Implementation Details

### Enhanced Drive Monitoring Approach

The current approach uses timer-based refreshes and scans all drives. The new approach will:

1. **Use Event-Based Monitoring**: 
   - Watch for changes in the `/dev/disk/by-id/` directory instead of polling on a timer
   - Only trigger full refreshes when actual changes are detected

2. **Build Device Mapping**:
   ```csharp
   public class DriveMapping
   {
       // Maps from disk ID name to drive info
       public Dictionary<string, DriveInfo> DiskIdNameToDrive { get; } = new();
       
       // Maps from device path to disk ID name
       public Dictionary<string, string> DevicePathToDiskIdName { get; } = new();
       
       // Maps from device name to disk ID name
       public Dictionary<string, string> DeviceNameToDiskIdName { get; } = new();
       
       // Maps from serial number to disk ID name
       public Dictionary<string, string> SerialToDiskIdName { get; } = new();
       
       // Last time the mapping was updated
       public DateTime LastUpdated { get; set; }
   }
   ```

3. **Disk Information Service Implementation**:
   ```csharp
   public class DriveInfoService : IDriveInfoService
   {
       private readonly IDriveMonitoringService _monitoringService;
       private readonly ISystemCommandService _commandService;
       private readonly ILogger<DriveInfoService> _logger;
       
       public DriveInfoService(
           IDriveMonitoringService monitoringService,
           ISystemCommandService commandService,
           ILogger<DriveInfoService> logger)
       {
           _monitoringService = monitoringService;
           _commandService = commandService;
           _logger = logger;
       }
       
       public async Task<DriveInfo?> GetDriveByDiskIdNameAsync(string diskIdName, bool useCache = true)
       {
           var mapping = _monitoringService.GetDriveMapping();
           
           // Check cache if requested
           if (useCache && mapping.DiskIdNameToDrive.TryGetValue(diskIdName, out var cachedInfo))
           {
               return cachedInfo;
           }
           
           // If not using cache or not in cache, fetch fresh data
           return await FetchDriveInfoAsync(diskIdName);
       }
       
       private async Task<DriveInfo?> FetchDriveInfoAsync(string diskIdName)
       {
           // Fetch using targeted command
           var diskIdPath = $"/dev/disk/by-id/{diskIdName}";
           var result = await _commandService.ExecuteCommandAsync(
               $"lsblk -J -b -o NAME,SIZE,TYPE,MOUNTPOINT,UUID,SERIAL,FSTYPE,PATH {diskIdPath}");
           
           if (!result.Success)
           {
               return null;
           }
           
           // Parse the JSON result
           // Update the cache in the monitoring service
           // Return the parsed data
           // ...
       }
       
       // Other method implementations
   }
   ```

4. **Cache Static Information**:
   - Store static drive information (serial, size) that doesn't change
   - Only refresh dynamic information (mountpoints, partition info) when needed

5. **Event Subscription Model**:
   ```csharp
   public class DriveMonitoringService : BackgroundService, IDriveMonitoringService
   {
       private readonly FileSystemWatcher _diskByIdWatcher = new FileSystemWatcher();
       private readonly DriveMapping _driveMapping = new DriveMapping();
       private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
       
       public event EventHandler<DriveChangeEventArgs>? DriveChanged;
       
       protected override async Task ExecuteAsync(CancellationToken stoppingToken)
       {
           // Initial mapping
           await InitializeDriveMapAsync();
           
           // Watch for changes to /dev/disk/by-id/
           _diskByIdWatcher.Path = "/dev/disk/by-id";
           _diskByIdWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
           _diskByIdWatcher.Created += OnDiskByIdChanged;
           _diskByIdWatcher.Deleted += OnDiskByIdChanged;
           _diskByIdWatcher.Renamed += OnDiskByIdChanged;
           _diskByIdWatcher.EnableRaisingEvents = true;
           
           // Wait for cancellation
           await Task.Delay(Timeout.Infinite, stoppingToken);
       }
       
       private async void OnDiskByIdChanged(object sender, FileSystemEventArgs e)
       {
           await RefreshDrivesAsync();
           DriveChanged?.Invoke(this, new DriveChangeEventArgs { ChangeType = e.ChangeType });
       }
       
       // Implementation of other methods
   }
   ```

### Using .NET APIs for Pool Size Information

Instead of using command-line tools, use .NET's built-in APIs to get disk information:

```csharp
public class PoolInfoService : IPoolInfoService
{
    private readonly IPoolMetadataService _metadataService;
    private readonly ILogger<PoolInfoService> _logger;
    
    public async Task<Result<PoolSizeInfo>> GetPoolSizeInfoAsync(Guid poolGroupGuid)
    {
        try
        {
            // Get the metadata to find the mount path
            var metadataResult = await _metadataService.GetPoolMetadataAsync(poolGroupGuid);
            if (!metadataResult.Success || metadataResult.Data == null)
            {
                return Result<PoolSizeInfo>.Error("Failed to retrieve pool metadata");
            }
            
            var mountPath = metadataResult.Data.MountPath;
            if (string.IsNullOrEmpty(mountPath))
            {
                return Result<PoolSizeInfo>.Error("Pool is not mounted");
            }
            
            // Use .NET's DriveInfo to get space information
            var driveInfo = new DriveInfo(mountPath);
            if (!driveInfo.IsReady)
            {
                return Result<PoolSizeInfo>.Error("Drive is not ready");
            }
            
            // Calculate usage percentage
            var usedSpace = driveInfo.TotalSize - driveInfo.AvailableFreeSpace;
            var usagePercent = (usedSpace * 100.0 / driveInfo.TotalSize).ToString("0.0") + "%";
            
            return Result<PoolSizeInfo>.Success(new PoolSizeInfo
            {
                PoolGroupGuid = poolGroupGuid,
                Size = driveInfo.TotalSize,
                Used = usedSpace,
                Available = driveInfo.AvailableFreeSpace,
                UsePercent = usagePercent
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pool size information for {PoolGroupGuid}", poolGroupGuid);
            return Result<PoolSizeInfo>.Error($"Error: {ex.Message}");
        }
    }
    
    // Other implementations
}
```

## Step-by-Step Refactoring Plan

### Phase 1: Core Infrastructure Services

1. **Create Core Service Interfaces**:
   - Define `ISystemCommandService` with clear input/output contracts
   - Define `IFileSystemInfoService` for all file operations
   - Create dedicated interface for RAID information via `IMdStatReader`
   - Implement `IMountInfoReader` for mount point information

2. **Implement Core Services**:
   - Move existing logic from current implementations into new structure
   - Add proper logging and error handling
   - Ensure all system calls are centralized in `SystemCommandService`

### Phase 2: Storage Services

1. **Drive Services**:
   - Implement event-based `DriveMonitoringService` that watches for changes in `/dev/disk/by-id/`
   - Build a comprehensive disk mapping on startup that connects device IDs to device information
   - Use targeted `lsblk` and `smartctl` commands only when needed for specific devices
   - Create a cache that avoids re-fetching static drive information

2. **Pool Services**:
   - Implement a dedicated `PoolInfoService` to gather metrics (size, used, available)
   - Separate pool information retrieval from operations
   - Create clear boundaries between metadata and operation management

3. **Metadata Services**:
   - Centralize all metadata operations in a dedicated service
   - Implement robust validation and recovery logic

### Phase 3: Process Management Services

1. **Process Services**:
   - Create dedicated service for process information reading
   - Implement proper process termination service
   - Extract process-related functionality into their own namespace

### Phase 4: API Integration

1. Update API endpoints to use the new service structure
2. Update API responses according to the new model structure
3. Add comprehensive API documentation

## Model Enhancements

Update model classes to better support the service architecture:

```csharp
public class DriveInfo
{
    // Persistent identifiers
    public string DiskIdName { get; set; } = string.Empty;  // e.g., "scsi-example-drive-1234"
    public string DiskIdPath { get; set; } = string.Empty;  // e.g., "/dev/disk/by-id/scsi-example-drive-1234"
    public string Serial { get; set; } = string.Empty;
    
    // Current device info (may change after reboot)
    public string DevicePath { get; set; } = string.Empty;  // e.g., "/dev/sda"
    public string DeviceName { get; set; } = string.Empty;  // e.g., "sda"
    
    // Static information
    public long Size { get; set; }
    
    // Dynamic information
    public bool IsMounted { get; set; }
    public List<PartitionInfo> Partitions { get; set; } = new();
}

public class DriveDetailInfo : DriveInfo
{
    // SMART data
    public string ModelFamily { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    
    // Factory method
    public static DriveDetailInfo FromBasicInfo(DriveInfo basicInfo)
    {
        return new DriveDetailInfo
        {
            DiskIdName = basicInfo.DiskIdName,
            DiskIdPath = basicInfo.DiskIdPath,
            Serial = basicInfo.Serial,
            DevicePath = basicInfo.DevicePath,
            DeviceName = basicInfo.DeviceName,
            Size = basicInfo.Size,
            IsMounted = basicInfo.IsMounted,
            Partitions = basicInfo.Partitions
        };
    }
}

public class PoolSizeInfo
{
    public Guid PoolGroupGuid { get; set; }
    public long Size { get; set; }
    public long Used { get; set; }
    public long Available { get; set; }
    public string UsePercent { get; set; } = string.Empty;
}
```

## Dependency Registration

Update the dependency registration in `Program.cs` to support the new structure:

```csharp
// Core services
builder.Services.AddSingleton<ISystemCommandService, SystemCommandService>();
builder.Services.AddSingleton<IFileSystemInfoService, FileSystemInfoService>();
builder.Services.AddSingleton<IMdStatReader, MdStatReader>();
builder.Services.AddSingleton<IMountInfoReader, MountInfoReader>();

// Add memory cache for file content caching
builder.Services.AddMemoryCache();

// Drive monitoring service (singleton since it maintains state)
builder.Services.AddSingleton<IDriveMonitoringService, DriveMonitoringService>();
builder.Services.AddHostedService(sp => (DriveMonitoringService)sp.GetRequiredService<IDriveMonitoringService>());

// Storage services
builder.Services.AddScoped<IDriveInfoService, DriveInfoService>();
builder.Services.AddScoped<IDriveService, DriveService>();
builder.Services.AddScoped<IPoolService, PoolService>();
builder.Services.AddScoped<IPoolInfoService, PoolInfoService>();
builder.Services.AddScoped<IPoolMetadataService, PoolMetadataService>();

// Background services
builder.Services.AddSingleton<IPoolOperationManager, PoolOperationManager>();
builder.Services.AddHostedService<PoolOperationCleanupService>();
builder.Services.AddHostedService<PoolMetadataValidationService>();

// Process services
builder.Services.AddScoped<IProcessInfoService, ProcessInfoService>();
builder.Services.AddScoped<IProcessKillService, ProcessKillService>();
```

## Error Handling Strategy

For consistent error handling across the application, we'll implement a combination of user-friendly messages and detailed diagnostics:

1. **Standard Result Pattern**: 
   ```csharp
   public class Result<T>
   {
       public bool Success { get; private set; }
       public T? Data { get; private set; }
       public string ErrorMessage { get; private set; } = string.Empty;
       public string? DetailedError { get; private set; }
       
       public static Result<T> Success(T data) => new() { Success = true, Data = data };
       public static Result<T> Error(string message, string? details = null) => 
           new() { Success = false, ErrorMessage = message, DetailedError = details };
   }
   ```

2. **Exception Handling Policy**:
   - User-friendly messages in regular logs (Warning/Error level)
   - Detailed command outputs and stack traces only in Debug logs
   - API responses with simplified error messages and optional details for troubleshooting

3. **Logging Strategy**:
   ```csharp
   // For expected errors with user-friendly messages
   _logger.LogWarning("Cannot mount pool: {Error}", errorMessage);
   
   // For unexpected errors with detailed diagnostic info
   _logger.LogError(ex, "Command execution failed: {Command}", command);
   
   // Only log detailed command output at debug level
   _logger.LogDebug("Command output: {Output}", commandResult.Output);
   ```

## Event-Based Architecture

We'll implement FileSystemWatcher-based monitoring for drive changes:

1. **Drive Monitoring Service**:
   ```csharp
   public class DriveMonitoringService : BackgroundService, IDriveMonitoringService
   {
       private readonly FileSystemWatcher _diskByIdWatcher = new();
       
       public event EventHandler<DriveChangeEventArgs>? DriveChanged;
       
       protected override async Task ExecuteAsync(CancellationToken stoppingToken)
       {
           // Setup watcher for /dev/disk/by-id directory
           _diskByIdWatcher.Path = "/dev/disk/by-id";
           _diskByIdWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
           _diskByIdWatcher.Created += OnDiskByIdChanged;
           _diskByIdWatcher.Deleted += OnDiskByIdChanged;
           _diskByIdWatcher.Renamed += OnDiskByIdChanged;
           _diskByIdWatcher.EnableRaisingEvents = true;
           
           // Wait until cancelled
           await Task.Delay(Timeout.Infinite, stoppingToken);
       }
       
       private void OnDiskByIdChanged(object sender, FileSystemEventArgs e)
       {
           // Notify subscribers of the change
           DriveChanged?.Invoke(this, new DriveChangeEventArgs { Path = e.FullPath, ChangeType = e.ChangeType });
       }
   }
   ```

2. **Event Subscription**:
   ```csharp
   public class DriveInfoService : IDriveInfoService
   {
       public DriveInfoService(IDriveMonitoringService monitoringService)
       {
           // Subscribe to drive change events
           monitoringService.DriveChanged += OnDriveChanged;
       }
       
       private void OnDriveChanged(object sender, DriveChangeEventArgs e)
       {
           // Invalidate relevant caches when drives change
           // This prevents outdated information from being served
       }
   }
   ```

## Caching Strategy

Based on requirements for improved performance while maintaining data accuracy:

1. **Static Information Caching**:
   - Drive hardware information (serial numbers, model, capacity) will be cached indefinitely
   - Caches will only be invalidated on explicit refresh requests or drive change events
   - Implementation will use in-memory caching with no timeout for static data

2. **Dynamic Information Caching**:
   - Mount status, usage information will be cached for 1 hour
   - Event-based invalidation will be triggered on relevant system changes
   - Explicit refresh API endpoint will be provided

3. **Cache Implementation**:
   ```csharp
   public class DriveInfoService : IDriveInfoService
   {
       private readonly IMemoryCache _cache;
       
       public async Task<DriveInfo?> GetDriveByIdAsync(string id, bool useCache = true)
       {
           string cacheKey = $"drive:{id}";
           
           if (useCache && _cache.TryGetValue(cacheKey, out DriveInfo? cachedInfo))
           {
               return cachedInfo;
           }
           
           // Fetch fresh data
           var driveInfo = await FetchDriveInfoAsync(id);
           
           if (driveInfo != null)
           {
               // Cache with no expiration for static data
               _cache.Set(cacheKey, driveInfo);
           }
           
           return driveInfo;
       }
       
       public void InvalidateDriveCache(string id)
       {
           _cache.Remove($"drive:{id}");
       }
   }
   ```

### Interface Organization

We'll use a hybrid approach with both high-level aggregate interfaces and smaller focused ones:

1. **High-Level Aggregate Interfaces**:
   - `IDriveService`: Primary interface for drive operations
   - `IPoolService`: Primary interface for pool operations
   - `IProcessService`: Primary interface for process operations

2. **Focused Interfaces**:
   - `IDriveInfoService`: Read-only drive information
   - `IPoolInfoService`: Read-only pool information
   - `IPoolMetadataService`: Pool metadata operations
   - `IPoolOperationService`: Pool create/mount/unmount operations

3. **Implementation Example**:
   ```csharp
   // High-level aggregate interface
   public interface IPoolService : IPoolInfoService, IPoolOperationService
   {
       // Additional methods that don't fit into the focused interfaces
   }
   
   // Implementation delegates to focused implementations
   public class PoolService : IPoolService
   {
       private readonly IPoolInfoService _poolInfoService;
       private readonly IPoolOperationService _poolOperationService;
       private readonly IPoolMetadataService _poolMetadataService;
       
       public PoolService(
           IPoolInfoService poolInfoService,
           IPoolOperationService poolOperationService,
           IPoolMetadataService poolMetadataService)
       {
           _poolInfoService = poolInfoService;
           _poolOperationService = poolOperationService;
           _poolMetadataService = poolMetadataService;
       }
       
       // Delegate to focused implementations
       public Task<Result<PoolDetailInfo>> GetPoolDetailAsync(Guid poolGuid) => 
           _poolInfoService.GetPoolDetailAsync(poolGuid);
           
       public Task<Result<CommandResponse>> MountPoolAsync(Guid poolGuid, string mountPath) => 
           _poolOperationService.MountPoolAsync(poolGuid, mountPath);
   }
   ```

## Command Execution Strategy

We'll prioritize direct file access over shell commands where possible:

1. **File Access Priority**:
   - Direct .NET file access for reading files in `/proc`, `/sys`, etc.
   - Built-in .NET APIs for file operations
   - Shell commands only as a fallback when no .NET alternative exists

2. **Example Implementation**:
   ```csharp
   public async Task<MdStatusInfo> GetMdStatusAsync()
   {
       // Direct file access instead of executing 'cat /proc/mdstat'
       string mdstatContent = await File.ReadAllTextAsync("/proc/mdstat");
       return ParseMdStatContent(mdstatContent);
   }
   
   public async Task<DriveInfo[]> GetMountedDrivesAsync()
   {
       // Use .NET's built-in DriveInfo instead of 'df' command
       return DriveInfo.GetDrives()
           .Where(d => d.IsReady && d.DriveType != DriveType.Network)
           .ToArray();
   }
   ```

3. **Fallback Pattern**:
   ```csharp
   public async Task<SmartData> GetSmartDataAsync(string devicePath)
   {
       // No direct .NET API for SMART data, use command
       return await _commandService.ExecuteCommandAsync($"smartctl -a {devicePath} -j");
   }
   ```

## Transaction Pattern for Pool Operations

For operations that require multiple steps, we'll implement a transaction-like pattern:

1. **Transaction Context**:
   ```csharp
   public class TransactionContext
   {
       private readonly List<Func<Task>> _rollbackActions = new();
       
       public void AddRollbackAction(Func<Task> action)
       {
           _rollbackActions.Add(action);
       }
       
       public void Begin() 
       {
           _rollbackActions.Clear();
       }
       
       public async Task CommitAsync()
       {
           // Success - clear rollback actions
           _rollbackActions.Clear();
       }
       
       public async Task RollbackAsync()
       {
           // Execute rollback actions in reverse order
           for (int i = _rollbackActions.Count - 1; i >= 0; i--)
           {
               try
               {
                   await _rollbackActions[i]();
               }
               catch (Exception ex)
               {
                   // Log rollback failures but continue with other rollbacks
               }
           }
       }
   }
   ```

2. **Usage Example**:
   ```csharp
   public async Task<Result<PoolCreationResponse>> CreatePoolAsync(PoolCreationRequest request)
   {
       return await ExecuteTransactionAsync<PoolCreationResponse>(async context =>
       {
           // Step 1: Create the array
           var createResult = await CreateMdArrayAsync(request.DriveIds, request.Label);
           if (!createResult.Success)
           {
               return Result<PoolCreationResponse>.Error(createResult.ErrorMessage);
           }
           
           // Register rollback action to remove the array if subsequent steps fail
           context.AddRollbackAction(async () => 
               await _commandService.ExecuteCommandAsync($"mdadm --stop {createResult.Data.DevicePath}")
           );
           
           // Step 2: Format the array
           var formatResult = await FormatArrayAsync(createResult.Data.DevicePath);
           if (!formatResult.Success)
           {
               return Result<PoolCreationResponse>.Error(formatResult.ErrorMessage);
           }
           
           // Step 3: Mount the array
           var mountResult = await MountArrayAsync(createResult.Data.DevicePath, request.MountPath);
           if (!mountResult.Success)
           {
               return Result<PoolCreationResponse>.Error(mountResult.ErrorMessage);
           }
           
           // Register rollback action to unmount if subsequent steps fail
           context.AddRollbackAction(async () => 
               await _commandService.ExecuteCommandAsync($"umount {request.MountPath}")
           );
           
           // Step 4: Save metadata
           var saveResult = await SaveMetadataAsync(new PoolMetadata
           {
               PoolGroupGuid = createResult.Data.PoolGuid,
               // Other metadata properties
           });
           
           if (!saveResult.Success)
           {
               return Result<PoolCreationResponse>.Error(saveResult.ErrorMessage);
           }
           
           // All steps completed successfully
           return Result<PoolCreationResponse>.Success(new PoolCreationResponse
           {
               PoolGroupGuid = createResult.Data.PoolGuid,
               MountPath = request.MountPath,
               Status = "Active"
           });
       });
   }
   ```

## Additional Enhancements for Robust Pool Management

### Named MD Paths and UUID-Based Management

A significant discovery for improving pool reliability is that mdadm supports:

1. **Named Paths**: Instead of relying on auto-assigned `/dev/md0`, `/dev/md1`, etc. which can change after reboots, we can use persistent named paths like `/dev/md/{uuid}`.

2. **UUID Assignment**: We can explicitly assign UUIDs to RAID arrays that match our `poolGroupGuid`.

3. **Name/Label Assignment**: We can assign human-readable labels to arrays.

This allows for much more robust pool management:

```shell
# Create a pool with explicit UUID and name
sudo mdadm --create /dev/md/46d3eceddb4f4d889670c29b8aaac3a1 --name=label123 --level=1 --uuid=46d3eceddb4f4d889670c29b8aaac3a1 --raid-devices=1 /dev/sdc --run --force

# Assemble a pool by UUID
mdadm --assemble --uuid=46d3eceddb4f4d889670c29b8aaac3a1 /dev/md/46d3eceddb4f4d889670c29b8aaac3a1

# List all existing arrays with their UUIDs
mdadm --detail --scan
# Output:
# ARRAY /dev/md/46d3eceddb4f4d889670c29b8aaac3a1 metadata=1.2 UUID=46d3eced:db4f4d88:9670c29b:8aaac3a1
```

#### Implementation Considerations

1. **UUID Conversion Functions**:
   - When working with mdadm we need to convert between .NET Guid format and mdadm UUID format:
     - Remove dashes from standard GUID format: `46d3eced-db4f-4d88-9670-c29b8aaac3a1` → `46d3eceddb4f4d889670c29b8aaac3a1`
     - Remove colons from mdadm format: `46d3eced:db4f4d88:9670c29b:8aaac3a1` → `46d3eceddb4f4d889670c29b8aaac3a1`

2. **GUID Collision Prevention**:
   - Before creating a pool, verify the GUID isn't already in use by:
     - Checking our metadata records
     - Running `mdadm --detail --scan` to check system-wide

3. **Dynamic MD Device Resolution**:
   - Instead of storing the `/dev/mdX` path in metadata, resolve it dynamically when needed:
   - Use .NET's `File.ResolveLinkTarget()` or similar to follow the symlink from `/dev/md/{uuid}` to the actual `/dev/mdX` device
   - Example: `/dev/md/46d3eceddb4f4d889670c29b8aaac3a1` might resolve to `/dev/md127`

4. **Metadata Simplification**:
   - We can remove `mdDeviceName` from the metadata as it's no longer needed
   - The pool UUID (based on `poolGroupGuid`) becomes the canonical identifier

### Updated Metadata Structure

With these improvements, we can simplify the metadata structure:

```json
{
  "pools": [
    {
      "label": "custom-label-from-frontend",
      "poolGroupGuid": "375aa626-62f3-404e-b2a7-59acaed5e9c0",
      "mountPath": "/mnt/backy/375aa626-62f3-404e-b2a7-59acaed5e9c0",
      "isMounted": true,
      "createdAt": "2025-04-21T19:15:07.846894Z",
      "drives": {
        "scsi-SATA_WDC_WD80EFAX-68K_1234567": {
          "label": "disk-1-custom-label",
          "serial_number": "1234567",
          "diskIdPath": "/dev/disk/by-id/scsi-SATA_WDC_WD80EFAX-68K_1234567"
        },
        "scsi-SATA_WDC_WD80EFAX-68K_0987654": {
          "label": "disk-2-custom-label",
          "serial_number": "0987654",
          "diskIdPath": "/dev/disk/by-id/scsi-SATA_WDC_WD80EFAX-68K_0987654"
        }
      }
    }
  ],
  "lastUpdated": "2025-04-21T19:15:07.8472637Z"
}
```

### Enhanced Pool Service Implementation

Here's how the improved `PoolService` implementation would work with these changes:

```csharp
public async Task<Result<PoolCreationResponse>> CreatePoolAsync(PoolCreationRequest request)
{
    try
    {
        // Generate or use provided pool GUID
        Guid poolGuid = request.PoolGroupGuid == Guid.Empty ? Guid.NewGuid() : request.PoolGroupGuid;
        
        // Convert to mdadm UUID format (no dashes)
        string mdadmUuid = poolGuid.ToString("N");
        
        // Check if this UUID is already in use
        if (await IsPoolUuidInUseAsync(poolGuid))
        {
            return Result<PoolCreationResponse>.Error($"A pool with GUID {poolGuid} already exists");
        }
        
        // Build the device path
        string mdDevicePath = $"/dev/md/{mdadmUuid}";
        
        // Build the mdadm command with explicit UUID, name, and device path
        string mdadmCommand = $"mdadm --create {mdDevicePath} --name={request.Label} --level=1 --uuid={mdadmUuid} --raid-devices={request.DriveIds.Count} ";
        
        // Add drive paths and other options
        // ...

        // Create metadata (without storing mdDeviceName)
        var metadata = new PoolMetadata
        {
            PoolGroupGuid = poolGuid,
            Label = request.Label,
            MountPath = request.MountPath,
            IsMounted = true,
            CreatedAt = DateTime.UtcNow,
            Drives = CreateDriveDictionary(request)
        };
        
        // Save metadata
        await _metadataService.SavePoolMetadataAsync(metadata);
        
        return Result<PoolCreationResponse>.Success(new PoolCreationResponse
        {
            PoolGroupGuid = poolGuid,
            // Other response fields
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error creating pool");
        return Result<PoolCreationResponse>.Error($"Error creating pool: {ex.Message}");
    }
}
```

## Benefits of This Refactor

1. **Improved Maintainability**: Clear separation of concerns makes the code easier to maintain
2. **Enhanced Testability**: Interface-based design enables proper unit testing with mocks
3. **Reduced Coupling**: Dependency on abstractions rather than concrete implementations
4. **Better Error Handling**: Consistent approach across all services
5. **More Efficient Resource Usage**: Event-based monitoring instead of timer-based polling
6. **Reduced System Command Execution**: Targeted commands instead of scanning everything
7. **Reliable Device Identification**: Consistent tracking even if device paths change
8. **Cleaner API Design**: Well-defined service contracts
9. **More Robust Logging**: Standardized logging throughout the application
10. **Better Platform Integration**: Using .NET APIs where possible instead of external commands

## Conclusion

This refactoring will significantly improve the maintainability, testability, and performance of the Backy.Agent codebase. By organizing services into logical layers with clear responsibilities and adopting an event-based approach to drive monitoring, the code will be more efficient and easier to extend in the future.

The implementation can be done incrementally, starting with the core infrastructure services and gradually refactoring each component while ensuring existing functionality remains unaffected. The focus on using .NET APIs where possible, combined with the transaction pattern for complex operations, will make the system more robust and reliable.