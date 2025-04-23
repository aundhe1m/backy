# Backy Agent Mock Mode Design Document

## Overview

The Backy Agent Mock Mode is designed to simulate the hardware-dependent operations of the Backy Agent without requiring actual physical drives or RAID configurations. This feature will enable faster and more predictable frontend development by providing consistent responses to API requests, simulating real-world scenarios including edge cases and failure modes.

## Goals

1. Create a "mock mode" that simulates the behavior of real hardware
2. Allow configuration of the mock environment (number of drives, sizes, etc.)
3. Support simulated failures and edge cases
4. Maintain compatibility with the existing codebase
5. Enable development in containerized environments
6. Make the mock mode controllable through environment variables

## Architecture

### Core Approach: The Decorator Pattern

We will implement the mock mode using the **Decorator Pattern**, which will allow us to:

1. Wrap existing service implementations with mock versions
2. Selectively intercept and simulate responses
3. Pass through to the real implementations for non-mocked functionality
4. Maintain the original code structure

### Mock Mode Configuration

A new `MockSettings` class will be added to control the mock environment:

```csharp
public class MockSettings
{
    public bool EnableMockMode { get; set; } = false;
    public int NumberOfDrives { get; set; } = 5;
    public long DefaultDriveSize { get; set; } = 1024 * 1024 * 1024 * 500; // 500GB
    public List<string> DriveSerials { get; set; } = new List<string>();
    public List<string> DriveModels { get; set; } = new List<string>();
    public int PoolCreationDelaySeconds { get; set; } = 5;
    public int PoolResyncPercent { get; set; } = 0; // 0 means no resync
    public int PoolResyncDurationSeconds { get; set; } = 30;
    
    // Specific failures that can be toggled
    public bool EnableDriveDisconnectionFailure { get; set; } = false;
    public bool EnablePoolCreationFailure { get; set; } = false;
    public bool EnableMountFailure { get; set; } = false;
    public bool EnableUnmountFailure { get; set; } = false;
    public bool EnableRemoveFailure { get; set; } = false;
}
```

This class will be registered in the DI container and populated from configuration (either appsettings.json or environment variables).

### System Command Service Mock

The `SystemCommandService` is the core component to mock as it handles executing system commands. We'll create a mock version:

```csharp
public class MockSystemCommandService : ISystemCommandService
{
    private readonly ILogger<MockSystemCommandService> _logger;
    private readonly MockSettings _mockSettings;
    private readonly ISystemCommandService _realService;
    private readonly IMockStateManager _mockStateManager;

    public MockSystemCommandService(
        ILogger<MockSystemCommandService> logger,
        IOptions<MockSettings> options,
        IMockStateManager mockStateManager,
        SystemCommandService realService) // Inject the real service
    {
        _logger = logger;
        _mockSettings = options.Value;
        _mockStateManager = mockStateManager;
        _realService = realService;
    }

    public async Task<CommandResult> ExecuteCommandAsync(string command, bool sudo = false)
    {
        if (!_mockSettings.EnableMockMode)
        {
            return await _realService.ExecuteCommandAsync(command, sudo);
        }

        _logger.LogDebug("Mock executing command: {Command}", command);

        // Handle different command types
        if (command.StartsWith("lsblk"))
        {
            return MockLsblkCommand(command);
        }
        else if (command.StartsWith("mdadm"))
        {
            return MockMdadmCommand(command);
        }
        else if (command.StartsWith("mount"))
        {
            return MockMountCommand(command);
        }
        // etc.

        // Default mock response for unhandled commands
        return new CommandResult
        {
            Success = true,
            ExitCode = 0,
            Output = $"Mock output for command: {command}",
            Command = command
        };
    }

    // Private methods for mocking different commands...
}
```

### Mock State Manager

To maintain state between API calls, we'll create a `MockStateManager` that keeps track of the simulated environment:

```csharp
public interface IMockStateManager
{
    List<MockDrive> GetDrives();
    List<MockPool> GetPools();
    MockDrive? GetDriveBySerial(string serial);
    MockPool? GetPoolByGuid(Guid poolGroupGuid);
    MockPool? GetPoolByMdDevice(string mdDeviceName);
    
    Task<MockDrive> AddDriveAsync(MockDrive drive);
    Task<MockPool> CreatePoolAsync(MockPoolCreationRequest request);
    Task<bool> RemoveDriveAsync(string serial);
    Task<bool> DisconnectDriveAsync(string serial);
    Task<bool> ReconnectDriveAsync(string serial);
    Task<bool> MountPoolAsync(Guid poolGroupGuid, string mountPath);
    Task<bool> UnmountPoolAsync(Guid poolGroupGuid);
    Task<bool> RemovePoolAsync(Guid poolGroupGuid);
    Task<bool> FailDriveAsync(string serial);
    
    Task ResetMockStateAsync();
    Task SaveStateAsync();
    Task LoadStateAsync();
}
```

This service will be implemented as a singleton to maintain state across requests.

### Mock Data Models

We'll create models for our mock state:

```csharp
public class MockDrive
{
    public string Serial { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty; // e.g. sdb, sdc, etc.
    public string DevPath { get; set; } = string.Empty; // e.g. /dev/sdb
    public string Model { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Type { get; set; } = "disk";
    public bool IsConnected { get; set; } = true;
    public bool IsFailed { get; set; } = false;
    public Guid? PoolGroupGuid { get; set; }
    public string? MdDeviceName { get; set; }
    public string? Label { get; set; }
}

public class MockPool
{
    public Guid PoolGroupGuid { get; set; }
    public string MdDeviceName { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public List<string> DriveSerials { get; set; } = new();
    public Dictionary<string, string> DriveLabels { get; set; } = new();
    public string State { get; set; } = "creating";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? MountPath { get; set; }
    public bool IsMounted { get; set; } = false;
    public double? ResyncPercentage { get; set; }
    public DateTime? ResyncStartedAt { get; set; }
    public DateTime? ResyncEstimatedCompletion { get; set; }
    public long Size { get; set; }
    public long Used { get; set; } = 0;
}
```

### Realistic Drive Serial and Device Paths

When generating mock drives, we'll use realistic serial numbers and device paths:

```csharp
public class MockDriveGenerator
{
    private static readonly Random _random = new Random();
    
    // Real-world drive serial number formats
    private static readonly List<string> SerialFormats = new List<string>
    {
        "WD-WCC{0}{1}{2}{3}{4}{5}{6}", // Western Digital
        "S{0}{1}{2}{3}NX0{4}{5}{6}{7}{8}", // Samsung
        "ZA{0}{1}{2}{3}{4}{5}", // Seagate
        "BTLA{0}{1}{2}{3}{4}{5}{6}{7}", // Intel SSD
        "TW{0}{1}{2}{3}{4}{5}{6}{7}", // Toshiba
        "{0}{1}{2}{3}{4}{5}{6}{7}", // Generic
    };
    
    // Common drive vendors and models
    private static readonly Dictionary<string, List<string>> VendorModels = new Dictionary<string, List<string>>
    {
        ["Western Digital"] = new List<string> { "WD Blue", "WD Red", "WD Black", "WD Purple", "WD Gold" },
        ["Samsung"] = new List<string> { "870 EVO", "870 QVO", "980 PRO", "990 PRO", "860 EVO" },
        ["Seagate"] = new List<string> { "BarraCuda", "IronWolf", "FireCuda", "Exos", "SkyHawk" },
        ["Intel"] = new List<string> { "SSD 670p", "SSD 760p", "Optane 905P", "SSD 660p" },
        ["Toshiba"] = new List<string> { "X300", "N300", "P300", "S300", "MG09" },
        ["Crucial"] = new List<string> { "MX500", "BX500", "P5", "P3", "P2" }
    };
    
    public static List<MockDrive> GenerateMockDrives(int count, long defaultSize)
    {
        var drives = new List<MockDrive>();
        
        // Start with sdb (sda is usually the system drive)
        char driveLetter = 'b';
        
        for (int i = 0; i < count; i++)
        {
            // Select a random vendor
            var vendor = VendorModels.Keys.ElementAt(_random.Next(VendorModels.Keys.Count));
            var models = VendorModels[vendor];
            var model = models[_random.Next(models.Count)];
            
            // Generate serial using one of the formats
            var format = SerialFormats[_random.Next(SerialFormats.Count)];
            var serialDigits = Enumerable.Range(0, 9).Select(_ => _random.Next(10).ToString()).ToArray();
            var serial = string.Format(format, serialDigits);
            
            // Create the device path
            var devPath = $"/dev/sd{driveLetter}";
            var name = $"sd{driveLetter}";
            
            // Add some variance to the drive size (±10%)
            var sizeVariance = (long)(defaultSize * (_random.NextDouble() * 0.2 + 0.9)); // 90-110% of default
            
            drives.Add(new MockDrive
            {
                Serial = serial,
                Name = name,
                DevPath = devPath,
                Model = model,
                Vendor = vendor,
                Size = sizeVariance,
                Type = "disk",
                IsConnected = true,
                IsFailed = false
            });
            
            // Move to next drive letter
            driveLetter++;
        }
        
        return drives;
    }
}
```

### Mock Services Implementation

For each core service in the application, we'll create a mock decorator version:

1. `MockDriveService`
2. `MockPoolService`
3. `MockMdStatReader`
4. `MockFileSystemInfoService`
5. Etc.

Each mock service will:
1. Handle the specific API calls
2. Interact with the MockStateManager
3. Simulate real-world behaviors
4. Generate realistic responses
5. Apply configured delays and failures as needed

### Dependency Injection Setup

The most important part of the implementation is modifying the DI container setup in `Program.cs`:

```csharp
// Register mock services conditionally
var mockSettings = new MockSettings();
builder.Configuration.GetSection("MockSettings").Bind(mockSettings);
builder.Services.Configure<MockSettings>(builder.Configuration.GetSection("MockSettings"));

// Always register the real services
builder.Services.AddSingleton<SystemCommandService>();
builder.Services.AddSingleton<FileSystemInfoService>();

// Register mock state manager if mock mode is enabled
if (mockSettings.EnableMockMode)
{
    builder.Services.AddSingleton<IMockStateManager, MockStateManager>();
    
    // Register mock services with decorators
    builder.Services.AddSingleton<ISystemCommandService, MockSystemCommandService>();
    builder.Services.AddSingleton<IFileSystemInfoService, MockFileSystemInfoService>();
    builder.Services.AddScoped<IDriveInfoService, MockDriveInfoService>();
    builder.Services.AddSingleton<IMdStatReader, MockMdStatReader>();
    
    // Register additional mock endpoints
    builder.Services.AddSingleton<IMockControlService, MockControlService>();
}
else
{
    // Register real services directly
    builder.Services.AddSingleton<ISystemCommandService, SystemCommandService>();
    builder.Services.AddSingleton<IFileSystemInfoService, FileSystemInfoService>();
    builder.Services.AddScoped<IDriveInfoService, DriveInfoService>();
    builder.Services.AddSingleton<IMdStatReader, MdStatReader>();
}
```

## Mock Control API Endpoints

We'll add a new set of API endpoints specifically for controlling the mock environment:

### Mock Control Endpoints

```csharp
app.MapGroup("/api/v1/mock")
    .AddEndpointFilter<ApiKeyAuthFilter>()
    .WithTags("Mock Control")
    .MapMockControlEndpoints();
```

Including endpoints for:

1. `GET /api/v1/mock/status` - Get current mock configuration
2. `POST /api/v1/mock/reset` - Reset the mock state
3. `POST /api/v1/mock/drives/disconnect/{serial}` - Simulate drive disconnection
4. `POST /api/v1/mock/drives/reconnect/{serial}` - Reconnect a previously disconnected drive
5. `POST /api/v1/mock/drives/fail/{serial}` - Simulate drive failure
6. `POST /api/v1/mock/pools/resync/{poolGroupGuid}` - Trigger pool resync
7. `POST /api/v1/mock/settings` - Update mock settings dynamically

## Configuration via Environment Variables

To support containerized deployments, we'll configure the mock settings via environment variables:

```
MOCK_ENABLED=true
MOCK_NUM_DRIVES=5
MOCK_DRIVE_SIZE=536870912000
MOCK_POOL_CREATION_DELAY=5
MOCK_POOL_RESYNC_PERCENT=30
MOCK_POOL_RESYNC_DURATION=60
```

## Realistic Command Output Examples

### `lsblk` Command

When mocking the `lsblk` command, we'll generate output that closely resembles real system output:

#### Basic `lsblk` output:

```
NAME        MAJ:MIN RM   SIZE RO TYPE  MOUNTPOINTS
sda           8:0    0 465.8G  0 disk  
├─sda1        8:1    0   512M  0 part  /boot/efi
├─sda2        8:2    0     1G  0 part  /boot
└─sda3        8:3    0 464.2G  0 part  
  └─cryptroot 253:0  0 464.2G  0 crypt /
sdb           8:16   0   3.7T  0 disk  
sdc           8:32   0   3.7T  0 disk  
sdd           8:48   0   3.7T  0 disk  
└─md0         9:0    0  11.1T  0 raid5 /mnt/backy-pool-1
sde           8:64   0   3.7T  0 disk  
└─md0         9:0    0  11.1T  0 raid5 /mnt/backy-pool-1
sdf           8:80   0   3.7T  0 disk  
└─md0         9:0    0  11.1T  0 raid5 /mnt/backy-pool-1
sdg           8:96   0   3.7T  0 disk  
└─md0         9:0    0  11.1T  0 raid5 /mnt/backy-pool-1
```

#### `lsblk -o NAME,SIZE,MODEL,SERIAL` output:

```
NAME        SIZE MODEL                SERIAL
sda       465.8G Samsung SSD 980      S6PMNX0T123456
├─sda1      512M                      
├─sda2        1G                      
└─sda3    464.2G                      
  └─cryptroot 464.2G                      
sdb         3.7T WD Red               WD-WCC7K3ABCDEF
sdc         3.7T WD Red               WD-WCC7K3FEDCBA
sdd         3.7T WD Red               WD-WCC7K3123456
└─md0      11.1T                      
sde         3.7T WD Red               WD-WCC7K3654321
└─md0      11.1T                      
sdf         3.7T WD Red               WD-WCC7K3ABCABC
└─md0      11.1T                      
sdg         3.7T WD Red               WD-WCC7K3DEFDEF
└─md0      11.1T
```

#### `lsblk -J` (JSON output):

```json
{
   "blockdevices": [
      {
         "name": "sda",
         "size": 34359738368,
         "type": "disk",
         "mountpoint": null,
         "uuid": null,
         "serial": "drive-scsi0",
         "vendor": "QEMU    ",
         "model": "QEMU HARDDISK",
         "fstype": null,
         "path": "/dev/sda",
         "id-link": "scsi-0QEMU_QEMU_HARDDISK_drive-scsi0",
         "children": [
            {
               "name": "sda1",
               "size": 1048576,
               "type": "part",
               "mountpoint": null,
               "uuid": null,
               "serial": null,
               "vendor": null,
               "model": null,
               "fstype": null,
               "path": "/dev/sda1",
               "id-link": "scsi-0QEMU_QEMU_HARDDISK_drive-scsi0-part1"
            },{
               "name": "sda2",
               "size": 2147483648,
               "type": "part",
               "mountpoint": "/boot",
               "uuid": "8cbf66d7-15ea-4234-beca-f09118d5b6be",
               "serial": null,
               "vendor": null,
               "model": null,
               "fstype": "ext4",
               "path": "/dev/sda2",
               "id-link": "scsi-0QEMU_QEMU_HARDDISK_drive-scsi0-part2"
            },{
               "name": "sda3",
               "size": 32209108992,
               "type": "part",
               "mountpoint": null,
               "uuid": "UQPGSU-CsUQ-81Ob-42L2-Hfcp-59rR-71Tzdr",
               "serial": null,
               "vendor": null,
               "model": null,
               "fstype": "LVM2_member",
               "path": "/dev/sda3",
               "id-link": "scsi-0QEMU_QEMU_HARDDISK_drive-scsi0-part3",
               "children": [
                  {
                     "name": "ubuntu--vg-ubuntu--lv",
                     "size": 16101933056,
                     "type": "lvm",
                     "mountpoint": "/",
                     "uuid": "1ff4b576-90ef-4afe-87ad-61cfeecbae9d",
                     "serial": null,
                     "vendor": null,
                     "model": null,
                     "fstype": "ext4",
                     "path": "/dev/mapper/ubuntu--vg-ubuntu--lv",
                     "id-link": "dm-name-ubuntu--vg-ubuntu--lv"
                  }
               ]
            }
         ]
      },{
         "name": "sdb",
         "size": 4000787030016,
         "type": "disk",
         "mountpoint": null,
         "uuid": "f99b633e-3fcc-6cce-de22-265656264dcb",
         "serial": "drive-scsi1",
         "vendor": "QEMU    ",
         "model": "QEMU HARDDISK",
         "fstype": "linux_raid_member",
         "path": "/dev/sdb",
         "id-link": "scsi-0QEMU_QEMU_HARDDISK_drive-scsi1",
         "children": [
            {
               "name": "md0",
               "size": 4000651739136,
               "type": "raid1",
               "mountpoint": "/mnt/backy/375aa626-62f3-404e-b2a7-59acaed5e9c0",
               "uuid": "c2989adb-79e3-455d-9cb5-718bcbd34291",
               "serial": null,
               "vendor": null,
               "model": null,
               "fstype": "ext4",
               "path": "/dev/md0",
               "id-link": "md-name-backy:0"
            }
         ]
      },{
         "name": "sdc",
         "size": 4000787030016,
         "type": "disk",
         "mountpoint": null,
         "uuid": "f99b633e-3fcc-6cce-de22-265656264dcb",
         "serial": "drive-scsi2",
         "vendor": "QEMU    ",
         "model": "QEMU HARDDISK",
         "fstype": "linux_raid_member",
         "path": "/dev/sdc",
         "id-link": "scsi-0QEMU_QEMU_HARDDISK_drive-scsi2",
         "children": [
            {
               "name": "md0",
               "size": 4000651739136,
               "type": "raid1",
               "mountpoint": "/mnt/backy/375aa626-62f3-404e-b2a7-59acaed5e9c0",
               "uuid": "c2989adb-79e3-455d-9cb5-718bcbd34291",
               "serial": null,
               "vendor": null,
               "model": null,
               "fstype": "ext4",
               "path": "/dev/md0",
               "id-link": "md-name-backy:0"
            }
         ]
      },{
         "name": "sr0",
         "size": 3213064192,
         "type": "rom",
         "mountpoint": null,
         "uuid": "2025-02-16-22-49-22-00",
         "serial": "QM00003",
         "vendor": "QEMU    ",
         "model": "QEMU DVD-ROM",
         "fstype": "iso9660",
         "path": "/dev/sr0",
         "id-link": "ata-QEMU_DVD-ROM_QM00003"
      }
   ]
}
```

### `mdadm` Command

#### `mdadm --detail /dev/md0`:

```
/dev/md0:
           Version : 1.2
     Creation Time : Sat Apr 19 10:30:45 2025
        Raid Level : raid5
        Array Size : 11634072576 (11.1 TiB 12.2 TB)
     Used Dev Size : 3877357568 (3.7 TiB 4.1 TB)
      Raid Devices : 4
     Total Devices : 4
       Persistence : Superblock is persistent

     Intent Bitmap : Internal

       Update Time : Sun Apr 20 15:45:32 2025
             State : clean
    Active Devices : 4
   Working Devices : 4
    Failed Devices : 0
     Spare Devices : 0

            Layout : left-symmetric
        Chunk Size : 512K

Consistency Policy : bitmap

              Name : backy-server:0
              UUID : e1a25fad:b1b194f4:161a8315:967bc0ea
            Events : 678

    Number   Major   Minor   RaidDevice State
       0       8       48        0      active sync   /dev/sdd
       1       8       64        1      active sync   /dev/sde
       2       8       80        2      active sync   /dev/sdf
       3       8       96        3      active sync   /dev/sdg
```

#### `mdadm --detail /dev/md0` (During Resync):

```
/dev/md0:
           Version : 1.2
     Creation Time : Sat Apr 19 10:30:45 2025
        Raid Level : raid5
        Array Size : 11634072576 (11.1 TiB 12.2 TB)
     Used Dev Size : 3877357568 (3.7 TiB 4.1 TB)
      Raid Devices : 4
     Total Devices : 4
       Persistence : Superblock is persistent

     Intent Bitmap : Internal

       Update Time : Sun Apr 20 15:45:32 2025
             State : clean, resyncing
    Active Devices : 4
   Working Devices : 4
    Failed Devices : 0
     Spare Devices : 0

            Layout : left-symmetric
        Chunk Size : 512K

Consistency Policy : bitmap

    Resync Status : 14% complete

              Name : backy-server:0
              UUID : e1a25fad:b1b194f4:161a8315:967bc0ea
            Events : 678

    Number   Major   Minor   RaidDevice State
       0       8       48        0      active sync   /dev/sdd
       1       8       64        1      active sync   /dev/sde
       2       8       80        2      active sync   /dev/sdf
       3       8       96        3      active sync   /dev/sdg
```

#### `mdadm --detail /dev/md0` (With Failed Drive):

```
/dev/md0:
           Version : 1.2
     Creation Time : Sat Apr 19 10:30:45 2025
        Raid Level : raid5
        Array Size : 11634072576 (11.1 TiB 12.2 TB)
     Used Dev Size : 3877357568 (3.7 TiB 4.1 TB)
      Raid Devices : 4
     Total Devices : 3
       Persistence : Superblock is persistent

     Intent Bitmap : Internal

       Update Time : Sun Apr 20 15:45:32 2025
             State : clean, degraded
    Active Devices : 3
   Working Devices : 3
    Failed Devices : 1
     Spare Devices : 0

            Layout : left-symmetric
        Chunk Size : 512K

Consistency Policy : bitmap

              Name : backy-server:0
              UUID : e1a25fad:b1b194f4:161a8315:967bc0ea
            Events : 678

    Number   Major   Minor   RaidDevice State
       0       8       48        0      active sync   /dev/sdd
       1       8       64        1      active sync   /dev/sde
       2       8       80        2      active sync   /dev/sdf
       3       0        0        3      removed
```

#### `mdadm --examine /dev/sdd`:

```
/dev/sdd:
          Magic : a92b4efc
        Version : 1.2
    Feature Map : 0x1
     Array UUID : e1a25fad:b1b194f4:161a8315:967bc0ea
           Name : backy-server:0
  Creation Time : Sat Apr 19 10:30:45 2025
     Raid Level : raid5
   Raid Devices : 4

 Avail Dev Size : 7754715136 (3.7 TiB 4.1 TB)
     Array Size : 11634072576 (11.1 TiB 12.2 TB)
  Used Dev Size : 7754715136 (3.7 TiB 4.1 TB)
    Data Offset : 264192 sectors
   Super Offset : 8 sectors
   Unused Space : before=264112 sectors, after=0 sectors
          State : clean
    Device UUID : a7bc9ead:fa123c49:87d548ce:3a12f9b2

Internal Bitmap : 8 sectors from superblock
    Update Time : Sun Apr 20 15:45:32 2025
  Bad Block Log : 512 entries available at offset 72 sectors
       Checksum : ce35e25 - correct
         Events : 678

         Layout : left-symmetric
     Chunk Size : 512K

   Device Role : Active device 0
   Array State : AAAA ('A' == active, '.' == missing, 'R' == replacing)
```

#### `mdadm --create`:

```
mdadm: Defaulting to version 1.2 metadata
mdadm: array /dev/md0 started.
```

### `df` Command

#### `df -h /mnt/backy-pool-1`:

```
Filesystem      Size  Used Avail Use% Mounted on
/dev/md0         11T  5.2T  5.3T  50% /mnt/backy-pool-1
```

#### `df -h` (No Mount):

```
df: '/mnt/backy-pool-1': No such file or directory
```

### `mount` Command

#### Successful mount:

```
```

(Note: Successful mount usually doesn't produce output)

#### Mount error:

```
mount: /mnt/backy-pool-1: special device /dev/md0 does not exist.
```

### `cat /proc/mdstat`

#### Normal Operation:

```
Personalities : [raid1] [raid0] [raid6] [raid5] [raid4] 
md0 : active raid5 sdg[3] sdf[2] sde[1] sdd[0]
      11634072576 blocks super 1.2 level 5, 512k chunk, algorithm 2 [4/4] [UUUU]
      bitmap: 0/45 pages [0KB], 65536KB chunk

unused devices: <none>
```

#### During Resync:

```
Personalities : [raid1] [raid0] [raid6] [raid5] [raid4] 
md0 : active raid5 sdg[3] sdf[2] sde[1] sdd[0]
      11634072576 blocks super 1.2 level 5, 512k chunk, algorithm 2 [4/4] [UUUU]
      [====>................]  resync = 21.2% (822594816/3877357568) finish=783.4min speed=65022K/sec
      bitmap: 29/45 pages [116KB], 65536KB chunk

unused devices: <none>
```

#### With Failed Drive:

```
Personalities : [raid1] [raid0] [raid6] [raid5] [raid4] 
md0 : active raid5 sdg[3] sdf[2] sde[1] sdd[0]
      11634072576 blocks super 1.2 level 5, 512k chunk, algorithm 2 [4/3] [UUU_]
      bitmap: 0/45 pages [0KB], 65536KB chunk

unused devices: <none>
```

## Simulating Realistic Behavior

### Simulating Pool Creation

1. When a pool creation request is received, the mock service will:
   - Validate that the drives exist in the mock state
   - Check for configured failures
   - Set the pool state to "creating"
   - Start a background task to simulate creation time
   - After the configured delay, move to "active" state
   - If resync is enabled, start with the configured resync percentage

### Simulating Drive Failures

1. When a drive failure is simulated:
   - Mark the drive as failed in the mock state
   - If the drive is part of a pool, update the pool status accordingly
   - Update the MD array status to reflect the failure

### Persistence

The mock state will be persisted to disk (in a configurable location) to allow the state to survive service restarts.

## Implementation Plan

### Phase 1: Core Infrastructure

1. Create MockSettings class
2. Implement MockStateManager
3. Create MockSystemCommandService
4. Modify DI registration in Program.cs
5. Add environment variable configuration

### Phase 2: Mock Services

1. Implement MockDriveService
2. Implement MockPoolService
3. Implement MockMdStatReader
4. Implement MockFileSystemInfoService

### Phase 3: Mock Control API

1. Create MockControlService
2. Add mock control endpoints
3. Implement mock state reset functionality

### Phase 4: Realistic Simulation

1. Implement realistic command output generation
2. Add timing simulation (delays, progress, etc.)
3. Implement failure scenarios
4. Add persistence of mock state

### Phase 5: Documentation & Testing

1. Add documentation on mock mode usage
2. Create sample configurations for common scenarios
3. Create automated tests for mock mode

## Docker Integration

For containerized deployment, we'll provide a Dockerfile configured for mock mode:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5151

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Backy.Agent/Backy.Agent.csproj", "Backy.Agent/"]
RUN dotnet restore "Backy.Agent/Backy.Agent.csproj"
COPY . .
WORKDIR "/src/Backy.Agent"
RUN dotnet build "Backy.Agent.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Backy.Agent.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV BACKY_MOCK_ENABLED=true
ENV BACKY_MOCK_NUM_DRIVES=5
ENV BACKY_MOCK_DRIVE_SIZE=536870912000
ENV BACKY_MOCK_POOL_CREATION_DELAY=5
ENV BACKY_MOCK_POOL_RESYNC_PERCENT=30
ENV BACKY_MOCK_POOL_RESYNC_DURATION=60
ENTRYPOINT ["dotnet", "Backy.Agent.dll"]
```

## Benefits

1. **Development Speed**: Frontend developers can work without needing actual hardware
2. **Consistency**: Predictable behavior during development
3. **Edge Case Testing**: Easy to simulate failure scenarios
4. **CI/CD Integration**: Automated tests can run in containers
5. **Demos**: Product demos can be run without hardware
6. **Backward Compatibility**: Original code remains intact
7. **Future-Proof**: New features will automatically be supported in mock mode

## Suggestions for Future Enhancements

1. **Random State Variations**: Generate random but realistic variations in drive sizes, models, etc.
2. **Network Latency Simulation**: Add configurable delays to simulate network latency
3. **Error Rate Profiles**: Create profiles for different types of system conditions
4. **Mock State Recording**: Record real system interactions and replay them later
5. **Mock UI**: Create a web UI for controlling the mock environment
6. **Mock Event Subscriptions**: Allow frontend to subscribe to simulated hardware events
7. **Performance Testing Mode**: Simulate high load scenarios
8. **Preset Scenarios**: Pre-configured scenarios for specific testing needs (e.g., "resync in progress", "drive failing")

## Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| Real commands might be executed accidentally in mock mode | Ensure command execution is fully intercepted in mock mode |
| Mock implementation might diverge from actual implementation | Regular validation of mock behavior against real systems |
| Performance overhead of mock layer | Use conditional compilation or runtime checks to minimize overhead |
| Complexity of maintaining both real and mock implementations | Use the decorator pattern to minimize code duplication |