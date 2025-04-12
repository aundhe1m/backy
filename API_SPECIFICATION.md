# Backy Agent API Specification

## Authentication
All endpoints require an API key passed in the `X-Api-Key` header.

## Base URL
Default: `http://localhost:5151/api/v1`

## Endpoints

### Drives

#### GET /drives
Lists all available drives, excluding specified ones (e.g., /dev/sda)

**Response:**
```json
{
  "blockdevices": [
    {
    "name": "sdc",
    "size": 2147483648,
    "type": "disk",
    "mountpoint": null,
    "uuid": null,
    "serial": "VB98af2843-a0df3159",
    "vendor": "ATA     ",
    "model": "VBOX HARDDISK",
    "fstype": null,
    "path": "/dev/sdc",
    "id-link": "ata-VBOX_HARDDISK_VB98af2843-a0df3159",
    "children": [
        {
            "name": "sdc1",
            "size": 1022951936,
            "type": "part",
            "mountpoint": null,
            "uuid": "9938049b-9aac-4025-8d86-2bff17762f85",
            "serial": null,
            "vendor": null,
            "model": null,
            "fstype": "ext4",
            "path": "/dev/sdc1",
            "id-link": "ata-VBOX_HARDDISK_VB98af2843-a0df3159-part1"
        },{
            "name": "sdc2",
            "size": 1123024896,
            "type": "part",
            "mountpoint": null,
            "uuid": null,
            "serial": null,
            "vendor": null,
            "model": null,
            "fstype": null,
            "path": "/dev/sdc2",
            "id-link": "ata-VBOX_HARDDISK_VB98af2843-a0df3159-part2"
        }
      ]
    }
  ]
}
```

#### GET /drives/{serial}/status
Get detailed status of a specific drive

**Response:**
```json
{
  "status": "available",
  "inPool": false,
  "poolId": null,
  "mountPoint": null,
  "processes": []
}
```

### Pools

#### POST /pools
Create a new RAID1 pool

**Request:**
```json
{
  "label": "backup-1",
  "driveSerials": ["WD-123456", "WD-789012"],
  "driveLabels": {
    "WD-123456": "backup-1-1",
    "WD-789012": "backup-1-2"
  },
  "mountPath": "/mnt/backy/backup-1"
}
```

**Response:**
```json
{
  "success": true,
  "poolId": "md0",
  "mountPath": "/mnt/backy/backup-1",
  "status": "Active",
  "commandOutputs": [
    "mdadm: array /dev/md0 started",
    "mkfs.ext4: filesystem created"
  ]
}
```

#### GET /pools/{poolId}
Get pool status and details

**Response:**
```json
{
  "status": "Active",
  "size": 1000204851200,
  "used": 52428800,
  "available": 947776051200,
  "usePercent": "5%",
  "mountPath": "/mnt/backy/backup-1",
  "drives": [
    {
      "serial": "WD-123456",
      "label": "backup-1-1",
      "status": "active"
    }
  ]
}
```

#### GET /pools
Lists all existing mdadm pools

**Response:**
```json
[
  {
    "poolId": "md0",
    "poolGroupGuid": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "label": "backup-1",
    "status": "Active",
    "mountPath": "/mnt/backy/backup-1",
    "isMounted": true,
    "driveCount": 2,
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
    ]
  }
]
```

#### POST /pools/{poolId}/mount
Mount a pool

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
  "mountPath": "/mnt/backy/backup-1",
  "commandOutputs": [
    "mdadm: array /dev/md0 started",
    "mount: /dev/md0 mounted on /mnt/backy/backup-1"
  ]
}
```

#### POST /pools/{poolId}/unmount
Unmount a pool

**Response:**
```json
{
  "success": true,
  "commandOutputs": [
    "umount: /mnt/backy/backup-1 unmounted",
    "mdadm: array /dev/md0 stopped"
  ]
}
```

#### POST /pools/{poolId}/remove
Remove a pool completely

**Response:**
```json
{
  "success": true,
  "commandOutputs": [
    "umount: /mnt/backy/backup-1 unmounted",
    "mdadm: array /dev/md0 stopped",
    "wipefs: signatures wiped"
  ]
}
```

#### POST /pools/metadata/validate
Validate and update pool metadata by checking if the MdDeviceName matches the actual system devices

**Response:**
```json
{
  "success": true,
  "message": "Pool metadata validation complete. Fixed 1 entries.",
  "fixedEntries": 1
}
```

#### POST /pools/metadata/remove
Remove pool metadata

**Request:**
```json
{
  "poolId": "md0",
  "poolGroupGuid": null,
  "removeAll": false
}
```

**Response:**
```json
{
  "success": true,
  "message": "Metadata removed successfully"
}
```

### System Operations

#### GET /mounts/{path}/processes
Get processes using a mount point

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

#### POST /processes/kill
Kill specified processes

**Request:**
```json
{
  "pids": [1234, 5678],
  "poolGroupGuid": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

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

### Security

## Error Responses
All endpoints may return these error responses:

```json
{
  "error": {
    "code": "UNAUTHORIZED",
    "message": "Invalid API key"
  }
}
```

```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Invalid mount path",
    "details": "Mount path must be absolute"
  }
}
```

```json
{
  "error": {
    "code": "SYSTEM_ERROR",
    "message": "Failed to execute command: mdadm --create",
    "details": "Device or resource busy"
  }
}
```

## Rate Limiting
- 100 requests per minute per API key
- Rate limit headers included in responses:
  - X-RateLimit-Limit
  - X-RateLimit-Remaining
  - X-RateLimit-Reset