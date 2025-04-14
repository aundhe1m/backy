# Backy Agent Documentation

The Backy Agent is a standalone API service that manages drive operations for the Backy backup solution. It runs separately from the main Backy application and provides a RESTful API for drive and pool management operations.

## Overview

The Backy Agent provides system-level access to drives and RAID operations through a secure API. It allows the main Backy application to:

- List and get status of available drives
- Create, mount, unmount, and remove RAID1 pools
- Monitor processes using mount points
- Kill processes when necessary
- Track resync progress for RAID arrays

The Agent is designed to be run with elevated privileges (typically as root) since it needs to interact with system devices and read system files like `/proc/mdstat` and `/sys/block`.

## Building the Agent

### Prerequisites

- .NET 8.0 SDK
- A Linux-based OS
- Necessary system utilities: `mdadm`, `lsblk`, `df`, `mount`, etc.

### Build Steps

1. Navigate to the agent directory:
   ```bash
   cd /path/to/backy/Backy.Agent
   ```

2. Build the project:
   ```bash
   dotnet build
   ```

3. To create a release build:
   ```bash
   dotnet publish -c Release
   ```
   
   This will create a release build in the `/path/to/backy/Backy.Agent/bin/Release/net8.0/publish` directory.

## Running the Agent

### Development Mode

During development, you can run the agent with:

```bash
cd /path/to/backy/Backy.Agent
sudo dotnet run
```

The `sudo` command is necessary because the agent needs elevated privileges to perform drive operations and read system files.

With the default development settings, the agent will:
- Run on port 5151
- Have API authentication disabled for easier development
- Exclude the system drive (/dev/sda) from operations
- Output debug-level logs

### Using Visual Studio Code

If you're using VSCode, you can create a `.vscode/launch.json` file with appropriate settings to run the agent with elevated privileges:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Backy Agent",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/Backy.Agent/bin/Debug/net8.0/Backy.Agent.dll",
      "args": [],
      "cwd": "${workspaceFolder}/Backy.Agent",
      "stopAtEntry": false,
      "console": "internalConsole",
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      },
      "pipeTransport": {
        "pipeCwd": "${workspaceFolder}",
        "pipeProgram": "sudo",
        "pipeArgs": ["dotnet"]
      }
    }
  ]
}
```

## Installation

The Backy Agent includes an installation script that sets up the agent as a systemd service for easy management.

### Installation Steps

1. Build the agent (see [Building the Agent](#building-the-agent))

2. Make the installation script executable:
   ```bash
   chmod +x /path/to/backy/Backy.Agent/install.sh
   ```

3. Run the installation script with root privileges:
   ```bash
   sudo /path/to/backy/Backy.Agent/install.sh
   ```

4. Follow the prompts to configure:
   - API Port (default: 5151)
   - API Key (generates a random key by default)
   - Whether to disable API authentication (not recommended for production)
   - Excluded drives (e.g., /dev/sda,/dev/sdb)

### Post-Installation

After installation:
- The agent is installed in `/opt/backy/agent/`
- A systemd service named `backy-agent` is created and enabled
- The agent is started automatically

### Managing the Installed Service

```bash
# Check status
sudo systemctl status backy-agent

# View logs
sudo journalctl -u backy-agent -f

# Restart the service
sudo systemctl restart backy-agent

# Stop the service
sudo systemctl stop backy-agent
```

## Configuration

The agent is configured through `appsettings.json` and `appsettings.Development.json` files.

### Main Configuration Options

| Setting | Description | Default |
|---------|-------------|---------|
| `AgentSettings:ApiPort` | Port for the API to listen on | 5151 |
| `AgentSettings:ApiKey` | API key for authentication | default-api-key-change-me-in-production |
| `AgentSettings:ExcludedDrives` | Array of drive paths to exclude | ["/dev/sda"] |
| `AgentSettings:DisableApiAuthentication` | Whether to disable API authentication | false (true in Development) |
| `AgentSettings:FileCacheTimeToLiveSeconds` | How long to cache file reads | 5 |

### Logging Configuration

The agent uses structured logging with different log levels:

| Log Level | Purpose | Examples |
|-----------|---------|----------|
| Debug | Detailed troubleshooting information | File parsing details, command execution details |
| Information | Normal operational events | Pool creation, raid state changes |
| Warning | Potential issues that don't cause failures | Metadata inconsistencies, temporary file access issues |
| Error | Actual error conditions | File read failures, parsing errors |

Configure log levels in appsettings.json:

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning",
    "Backy.Agent.Services.FileMonitoring": "Debug"
  }
}
```

### Development vs. Production

The development configuration (`appsettings.Development.json`) has:
- More verbose logging
- API authentication disabled by default
- A development API key

For production, authentication should be enabled and a strong API key should be used.

### Changing Configuration After Installation

To change the configuration after installation:

1. Edit the configuration file:
   ```bash
   sudo nano /opt/backy/agent/appsettings.json
   ```

2. Restart the service:
   ```bash
   sudo systemctl restart backy-agent
   ```

## API Reference

The Backy Agent provides the following API endpoints:

### Drive Endpoints

#### GET /api/v1/drives
Lists all available drives, excluding specified ones. Only returns devices of type "disk" (excludes ROM drives, etc.).

**Response:**
```json
{
  "blockdevices": [
    {
      "name": "sdc",
      "size": 2147483648,
      "type": "disk",
      /* other drive properties */
      "children": [/* array of partitions */]
    }
  ]
}
```

#### GET /api/v1/drives/{serial}/status
Get detailed status of a specific drive.

**Response:**
```json
{
  "status": "available",
  "inPool": false,
  "poolId": null,
  "mountPoint": null
}
```

### Pool Endpoints

#### GET /api/v1/pools
Lists all existing mdadm pools on the system.

**Response:**
```json
[
  {
    "poolId": "md0",
    "poolGroupGuid": "550e8400-e29b-41d4-a716-446655440000",
    "label": "backup-1",
    "status": "active",
    "mountPath": "/mnt/backy/backup-1",
    "isMounted": true,
    "drives": [
      {
        "serial": "WD-123456",
        "label": "backup-1-1",
        "isConnected": true
      },
      {
        "serial": "WD-789012",
        "label": "backup-1-2",
        "isConnected": true
      }
    ],
    "resyncPercentage": 12.6,
    "resyncTimeEstimate": 132.2
  }
]
```

The `resyncPercentage` property can be null if a resync operation is not in progress.

#### POST /api/v1/pools
Create a new RAID1 pool.

**Request:**
```json
{
  "label": "backup-1",
  "driveSerials": ["WD-123456", "WD-789012"],
  "driveLabels": {
    "WD-123456": "backup-1-1",
    "WD-789012": "backup-1-2"
  },
  "mountPath": "/mnt/backy/backup-1",
  "poolGroupGuid": "550e8400-e29b-41d4-a716-446655440000"
}
```

`poolGroupGuid` field to maintain consistent pool identification across system reboots.

**Response:**
```json
{
  "success": true,
  "poolId": "md0",
  "mountPath": "/mnt/backy/backup-1",
  "status": "Active",
  "commandOutputs": ["command outputs"]
}
```

#### GET /api/v1/pools/{poolGroupGuid}
Get pool status and details. Returns a 404 status if the pool doesn't exist.

**Response:**
```json
{
  "status": "Active",
  "size": 1000204851200,
  "used": 52428800,
  "available": 947776051200,
  "usePercent": "5%",
  "mountPath": "/mnt/backy/backup-1",
  "resyncPercentage": 12.6,
  "resyncTimeEstimate": 132.2,
  "drives": [{"serial": "WD-123456", "label": "backup-1-1", "status": "active"}]
}
```

#### POST /api/v1/pools/{poolGroupGuid}/mount
Mount a pool.

**Request:**
```json
{
  "mountPath": "/mnt/backy/backup-1"
}
```

**Response:**
```json
{
  "success": true,
  "commandOutputs": ["command outputs"]
}
```

#### POST /api/v1/pools/{poolGroupGuid}/unmount
Unmount a pool.

**Response:**
```json
{
  "success": true,
  "busy": false,
  "commandOutputs": ["command outputs"]
}
```

#### DELETE /api/v1/pools/{poolGroupGuid}
Remove a pool completely, including its metadata.

**Response:**
```json
{
  "success": true,
  "busy": false,
  "commandOutputs": ["command outputs"]
}
```

The `busy` value is set to `true` if the removal was unsuccessful because it was busy.

> Note: The legacy POST endpoint `/api/v1/pools/{poolGroupGuid}/remove` can be removed.

#### DELETE /api/v1/pools/{poolGroupGuid}/metadata
Remove pool metadata mapping information.

**Request:**
```json
{
  "poolGroupGuid": "550e8400-e29b-41d4-a716-446655440000"
}
```

At least one of the identifier fields or `removeAll` must be specified.

**Response:**
```json
{
  "success": true,
  "message": "Metadata removed successfully"
}
```

#### DELETE /api/v1/pools/metadata
Remove all pool metadata mapping information.

**Response:**
```json
{
  "success": true,
  "message": "Metadata removed successfully"
}
```

> Note: The legacy POST endpoint `/api/v1/pools/metadata/remove` can be removed.


#### POST /api/v1/pools/metadata/validate
Validate and update pool metadata by checking if mdadm device names match the actual system devices.

This endpoint is particularly useful after system reboots when mdadm device names might change (e.g., md0 might become md127).

**Response:**
```json
{
  "success": true,
  "message": "Pool metadata validation complete. Fixed 1 entries.",
  "fixedEntries": 1
}
```

### System Operations

#### GET /api/v1/mounts/{path}
Get processes using a mount point.

**Response:**
```json
{
  "processes": [
    {
      "pid": 1234,
      "command": "rsync",
      "user": "backup",
      "path": "/mnt/backy/backup-1/data"
    }
  ]
}
```

#### POST /api/v1/processes/kill
Kill specified processes.

**Request:**
```json
{
  "pids": [1234, 5678],
  "poolGroupGuid": "550e8400-e29b-41d4-a716-446655440000"
}
```

The `poolGroupGuid` parameter is optional and is used for tracking context only.

**Response:**
```json
{
  "success": true,
  "commandOutputs": [
    "$ kill -9 1234",
    "Process 1234: Killed",
    "$ kill -9 5678",
    "Process 5678: Killed"
  ]
}
```

### Authentication

All API endpoints require an API key passed in the `X-Api-Key` header, unless authentication is disabled in the configuration.

Example:
```
X-Api-Key: your-api-key-here
```

### Error Responses

Error responses follow this format:

```json
{
  "error": {
    "code": "ERROR_CODE",
    "message": "Error message",
    "details": "Optional details"
  }
}
```

Common error codes:
- `UNAUTHORIZED`: Invalid or missing API key
- `VALIDATION_ERROR`: Invalid request data
- `SYSTEM_ERROR`: Error executing system command
- `NOT_FOUND`: Requested resource not found

## Using Swagger

The Backy Agent includes Swagger UI for API exploration and testing.

### Accessing Swagger UI

1. Start the agent (see [Running the Agent](#running-the-agent))
2. Open a web browser and navigate to: `http://localhost:5151/swagger`
3. The Swagger UI will display all available endpoints with documentation

### Using Swagger with Authentication

If API authentication is enabled:

1. Click the "Authorize" button at the top of the Swagger UI
2. Enter your API key in the value field for `ApiKey`
3. Click "Authorize" and then "Close"

Now all requests made through the Swagger UI will include your API key.

### Testing Endpoints

1. Choose an endpoint from the list
2. Click "Try it out"
3. Fill in any required parameters
4. Click "Execute"
5. View the response below the request

## Implementation Details

### File-Based Monitoring

The Backy Agent uses direct file access from `/proc` and `/sys` directories to gather system information, rather than executing commands and parsing their outputs. This approach provides:

- Better performance (no process spawning overhead)
- More reliable data collection
- Real-time monitoring capabilities
- Reduced dependency on external tools

#### Key Files Monitored

1. **`/proc/mdstat`**
   - Contains information about all RAID arrays
   - Updated in real-time by the kernel
   - Provides status, component drives, and resync progress

2. **`/sys/block`**
   - Contains device information for all block devices
   - Provides device properties including size, model, vendor, and serial
   - Includes MD-specific information in the `/md` subdirectory for RAID arrays

### Pool Metadata System

#### Overview

The Backy Agent includes a metadata system that helps maintain persistent pool identification across system reboots. Since mdadm device numbers (md0, md1, etc.) can change when the system restarts, the agent stores metadata mappings between:

- mdadm device names (e.g., md0, md1)
- Stable identifier:  poolGroupGuid
- Drive serial numbers and labels

This metadata is stored in a JSON file at `/var/lib/backy/pool-metadata.json`.

#### Use Cases

The metadata system is particularly useful for:

1. **Pool Persistence**: Ensure pools can be identified even if device numbers change
2. **Drive Identification**: Associate specific physical drives with pools
3. **User-friendly Labels**: Maintain friendly names for drives and pools

#### Metadata Structure

```json
{
  "pools": [
    {
      "poolGroupGuid": "550e8400-e29b-41d4-a716-446655440000",
      "mdDeviceName": "md0",
      "label": "backup-1",
      "driveSerials": ["WD-123456", "WD-789012"],
      "driveLabels": {
        "WD-123456": "backup-1-1",
        "WD-789012": "backup-1-2"
      },
      "createdAt": "2025-04-12T13:45:00Z",
      "lastMountPath": "/mnt/backy/backup-1"
    }
  ],
  "lastUpdated": "2025-04-12T13:45:00Z"
}
```

#### Pool Metadata Validation

The Backy Agent automatically validates and fixes pool metadata at startup to ensure consistency between the metadata file and the actual system state. This is particularly important after system reboots when mdadm device numbers may change.

##### Automatic Validation

When the Backy Agent starts, it:
1. Reads the pool metadata from `/var/lib/backy/pool-metadata.json`
2. Scans the system for active mdadm arrays
3. Maps drive serials to their current mdadm devices
4. Updates the metadata if the mdadm device names have changed
5. Logs detailed information about any corrections made

This automation ensures that poolGroupGuid-based operations continue to work reliably even after system reboots or mdadm device name changes.

##### Manual Validation

You can manually trigger metadata validation through the API:
```bash
curl -X POST "http://localhost:5151/api/v1/pools/metadata/validate" -H "X-Api-Key: your-api-key"
```

### Enhanced Pool Management

#### Dynamic Device Reassignment

When mounting a pool by GUID, if the original mdadm device is no longer available, the agent will:
1. Find the next available mdadm device ID
2. Assemble the array using the RAID component drives identified by serial numbers
3. Mount the filesystem at the requested path
4. Update the metadata with the new device name

This creates a seamless experience when working with the GUID-based API, even when underlying device names change.

#### Resync Progress Monitoring

The agent tracks resync progress for RAID arrays by monitoring the `/proc/mdstat` file. When a resync operation is in progress, the `resyncPercentage` property is included in the pool API responses, allowing applications to display progress to users.

#### Mount Path Protection

The agent prevents mounting pools to paths already in use by other pools, avoiding potential data corruption or access issues.

## Troubleshooting

### Common Issues and Solutions

#### Agent won't start

**Issue**: The agent fails to start or crashes immediately.

**Solutions**:
- Check logs: `sudo journalctl -u backy-agent -f`
- Verify permissions: The agent needs to run as root
- Check if required tools are installed: `mdadm`, `lsblk`, etc.
- Check if port is already in use: `sudo lsof -i :5151`

#### Authentication issues

**Issue**: Getting "Unauthorized" responses from the API.

**Solutions**:
- Verify the API key is correct
- Check if authentication is disabled in configuration
- Ensure the X-Api-Key header is properly formatted
- Try using Swagger UI to test authentication

#### Drive not showing up in the API

**Issue**: A drive is connected but not showing in the `/api/v1/drives` endpoint.

**Solutions**:
- Check if the drive is in the excluded drives list
- Verify the drive is detected by the system: `lsblk`
- Ensure the drive has a serial number: `lsblk -o NAME,SERIAL`
- Check if the agent can read from `/sys/block/[device]`

#### Pool creation fails

**Issue**: Creating a new pool fails with an error.

**Solutions**:
- Check if drives are already in use by another pool
- Verify that drives are not protected in Backy
- Check system logs for mdadm errors: `dmesg | tail`
- Try creating the pool manually to see specific errors:
  ```bash
  sudo mdadm --create /dev/mdX --level=1 --raid-devices=2 /dev/sdY /dev/sdZ
  ```

#### Pool mounting issues

**Issue**: Cannot mount or unmount a pool.

**Solutions**:
- Check if the mount point exists and is writable
- Verify if any processes are using the mount point:
  ```bash
  lsof +f -- /mnt/backy/mdX
  ```
- Check if the pool is active: `cat /proc/mdstat`
- Try mounting/unmounting manually to see specific errors

#### Pool metadata issues

**Issue**: After system reboot, pools are not found or have different device names.

**Solutions**:
- Use the metadata validation endpoint: `POST /api/v1/pools/metadata/validate`
- Always use poolGroupGuid-based endpoints for stability across reboots:
  - `GET /api/v1/pools/guid/{poolGroupGuid}`
  - `POST /api/v1/pools/guid/{poolGroupGuid}/mount`
  - `POST /api/v1/pools/guid/{poolGroupGuid}/unmount`
  - `DELETE /api/v1/pools/guid/{poolGroupGuid}`
- Check if the metadata file exists and is valid: `/var/lib/backy/pool-metadata.json`
- Verify drive serials match what's in the metadata: `lsblk -o NAME,SERIAL`

### Debugging Tips

1. Enable debug logging:
   - Edit `appsettings.json` and set `Logging:LogLevel:Default` to `Debug`
   - Restart the agent

2. Check system logs:
   ```bash
   dmesg | tail
   sudo journalctl -u backy-agent -f
   ```

3. Verify drive status:
   ```bash
   lsblk -o NAME,SIZE,TYPE,MOUNTPOINT,SERIAL
   cat /proc/mdstat
   ```

4. Check system file contents directly:
   ```bash
   cat /proc/mdstat
   ls -l /sys/block/*/device/serial
   ```

5. Test API directly with curl:
   ```bash
   curl -X GET "http://localhost:5151/api/v1/drives" -H "X-Api-Key: your-api-key"
   ```

6. Manually execute commands to verify system functionality:
   ```bash
   sudo mdadm --detail /dev/mdX
   sudo mount | grep mdX
   ```

### Getting Help

If you encounter issues not covered here:

1. Check the application logs for detailed error messages
2. Consult the Linux documentation for specific tools (mdadm, mount, etc.)
3. Search for similar issues in the project repository
4. Reach out to the development team with:
   - Exact error messages
   - Steps to reproduce
   - System information (OS, kernel version)
   - Application logs