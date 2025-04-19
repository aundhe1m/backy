# Refactor Pool Creating Routine

Improve the current code that creates pools using `/api/v1/pool`.

## Current solution

When the Backy Frontend wants to create a pool, it will send a POST request at `/api/v1/pool`, resulting in the Backy Agent to run the necessary mdadm commands in order to create a pool.
Example request body:
```json
{
  "label": "pool1",
  "driveSerials": ["drive-scsi1", "drive-scsi2"],
  "driveLabels": {
    "drive-scsi1": "pool1-disk1",
    "drive-scsi2": "pool1-disk2"
  },
  "mountPath": "/mnt/backy/3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "poolGroupGuid": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

When the pool is created, it will send a response that looks like this:
```json
{
  "success": true,
  "poolGroupGuid": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "mdDeviceName": null,
  "mountPath": "/mnt/backy/3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Active",
  "commandOutputs": [
    "$ sudo mdadm --create /dev/md0 --level=1 --raid-devices=2 /dev/disk/by-id/scsi-0QEMU_QEMU_HARDDISK_drive-scsi1 /dev/disk/by-id/scsi-0QEMU_QEMU_HARDDISK_drive-scsi2 --run --force",
    "mdadm: Defaulting to version 1.2 metadata\nmdadm: array /dev/md0 started.\nmdadm: /dev/disk/by-id/scsi-0QEMU_QEMU_HARDDISK_drive-scsi1 appears to be part of a raid array:\n       level=raid1 devices=2 ctime=Fri Apr 18 16:33:42 2025\nmdadm: Note: this array has metadata at the start and\n    may not be suitable as a boot device.  If you plan to\n    store '/boot' on this device please ensure that\n    your boot-loader understands md/v1.x metadata, or use\n    --metadata=0.90\nmdadm: /dev/disk/by-id/scsi-0QEMU_QEMU_HARDDISK_drive-scsi2 appears to be part of a raid array:\n       level=raid1 devices=2 ctime=Fri Apr 18 16:33:42 2025",
    "$ sudo mkfs.ext4 -F /dev/md0",
    "Discarding device blocks:         0/97672161638058496/97672161                  done\nCreating filesystem with 976721616 4k blocks and 244187136 inodes\nFilesystem UUID: 3181c4d8-df7e-4875-89dc-755810d5abc6\nSuperblock backups stored on blocks:\n\t32768, 98304, 163840, 229376, 294912, 819200, 884736, 1605632, 2654208,\n\t4096000, 7962624, 11239424, 20480000, 23887872, 71663616, 78675968,\n\t102400000, 214990848, 512000000, 550731776, 644972544\n\nAllocating group tables:     0/2980          done\nWriting inode tables:     0/2980          done\nCreating journal (262144 blocks): done\nWriting superblocks and filesystem accounting information:     0/2980  244/2980          done\n\nmke2fs 1.47.0 (5-Feb-2023)",
    "$ mkdir -p /mnt/backy/3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "",
    "$ sudo mount /dev/md0 /mnt/backy/3fa85f64-5717-4562-b3fc-2c963f66afa6",
    ""
  ]
}
```

## Issues With The Current Implementation

In the example above, the request took 29245ms, equal to 29 seconds.
This can result in the Frontend timing out, which is not ideal. As well as making the user experience pretty bad.

## Revised Solution

We'll implement an asynchronous pool creation process with improved user experience:

1. When the Backy Frontend sends a pool creation request:
   - The Backy Agent validates the request but doesn't wait for the pool creation to complete
   - The Agent immediately responds with a status of "creating" and the poolGroupGuid
   - The Agent spawns a background task to handle the actual pool creation

2. Frontend handling:
   - When receiving the initial response, the create pool modal closes
   - A placeholder card appears in the UI showing a spinner instead of a drive icon
   - Buttons on this placeholder card are disabled/grayed out
   - The placeholder indicates the pool is in the process of being created

3. Progress monitoring:
   - The frontend polls `/api/v1/pools/{poolGroupGuid}` at intervals with exponential backoff
   - Maximum timeout set at 10 minutes
   - Frontend checks for status changes from "creating" to "active" or other final states

4. Backend processing:
   - The Agent stores command outputs in memory during pool creation
   - When complete, the Agent updates the pool status from "creating" to "active" or appropriate state
   - All command outputs are preserved for later retrieval

5. Completion handling:
   - When the frontend detects the status is no longer "creating", it replaces the placeholder with a regular PoolGroupDriveCard
   - A toast notification appears: "Pool {label} successfully created" with a [Display Output] button
   - If [Display Output] is clicked, the frontend requests `/api/v1/pools/{poolGroupGuid}/output`
   - The Agent returns the stored command outputs for display in a modal

6. Error handling:
   - If command outputs are unavailable (e.g., agent restart), the frontend handles the 404 gracefully
   - If pool creation fails, the status will indicate "failed"
   - The frontend shows an error toast with a [Display Output] button to see command outputs
   - The placeholder card is removed, and no permanent database entry is created
   - Before reporting failure, the agent attempts to clean up any partial state to avoid leaving the system in an inconsistent state

## Implementation Details

### Memory Management
- Command outputs will be stored in memory only, with no persistent storage
- If the agent restarts, the output history will be lost, but the frontend will handle this gracefully
- Each pool creation is expected to generate a limited amount of output, making memory consumption minimal
- Recreating a pool with the same GUID will simply overwrite any existing output data

### Error Handling
- If pool creation fails, the agent will:
  1. Attempt to clean up any partially created resources (stop MD arrays, unmount filesystems)
  2. Set the pool status to "failed"
  3. Not save any entries to the pool-metadata.json file
  4. Preserve command outputs including error messages
- The frontend will:
  1. Display a failure toast notification
  2. Remove the placeholder card
  3. Not create any database entries for the failed pool
  4. Allow the user to view command outputs to diagnose the issue

## Expected API Responses

### Initial Response (POST /api/v1/pools)
```json
{
  "success": true,
  "poolGroupGuid": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "creating"
}
```

### Pool Status Response (GET /api/v1/pools/{poolGroupGuid})
During creation:
```json
{
  "status": "creating",
  "drives": []
}
```

After successful creation:
```json
{
  "status": "active",
  "size": 1000204851200,
  "used": 52428800,
  "available": 947776051200,
  "usePercent": "5%",
  "mountPath": "/mnt/backy/3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "drives": [{"serial": "drive-scsi1", "label": "pool1-disk1", "status": "active"}]
}
```

After failed creation:
```json
{
  "status": "failed",
  "errorMessage": "Failed to create RAID array"
}
```

### Command Output Response (GET /api/v1/pools/{poolGroupGuid}/output)
```json
{
  "outputs": [
    "$ sudo mdadm --create /dev/md0 --level=1 --raid-devices=2 /dev/disk/by-id/scsi-0QEMU_QEMU_HARDDISK_drive-scsi1 /dev/disk/by-id/scsi-0QEMU_QEMU_HARDDISK_drive-scsi2 --run --force",
    "mdadm: Defaulting to version 1.2 metadata\nmdadm: array /dev/md0 started.",
    "$ sudo mkfs.ext4 -F /dev/md0",
    "Creating filesystem with 976721616 4k blocks and 244187136 inodes...",
    "$ mkdir -p /mnt/backy/3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "",
    "$ sudo mount /dev/md0 /mnt/backy/3fa85f64-5717-4562-b3fc-2c963f66afa6",
    ""
  ]
}
```

## Related Files

### Backy.Agent Files
- `/home/aundhe1m/backy/Backy.Agent/Endpoints/PoolEndpoints.cs` - API endpoints for pool operations
- `/home/aundhe1m/backy/Backy.Agent/Services/PoolService.cs` - Service for pool-related operations
- `/home/aundhe1m/backy/Backy.Agent/Models/DriveModels.cs` - Model definitions for pools and drives

### Backy Frontend Files
- `/home/aundhe1m/backy/Backy/Components/PoolGroupDriveCard.razor` - Component for displaying pool details
- `/home/aundhe1m/backy/Backy/Components/Pages/DriveManagement.razor` - Page containing pool creation flow
- `/home/aundhe1m/backy/Backy/Services/BackyAgentClient.cs` - Client for communicating with Backy Agent
- `/home/aundhe1m/backy/Backy/Services/AgentDriveService.cs` - Service that handles pool operations

## Implementation Plan

### Phase 1: Backy Agent (Backend) Implementation
1. Create a background task service for pool operations
   - Create `PoolOperationManager.cs` service
   - Add in-memory storage for command outputs
   - Implement background task execution

2. Modify pool creation endpoint
   - Update `PoolEndpoints.cs` to start background task
   - Add status "creating" to response
   - Return immediately after validation

3. Add command output endpoint
   - Create new GET endpoint for `/api/v1/pools/{poolGroupGuid}/output`
   - Return stored command outputs

4. Update error handling
   - Ensure cleanup of partially created resources
   - Add "failed" status handling

### Phase 2: Backy Frontend Implementation
1. Create placeholder pool card component
   - Create or modify `PoolGroupDriveCard.razor` to handle "creating" state
   - Add spinner and disabled buttons

2. Implement polling mechanism
   - Add exponential backoff polling to `AgentDriveService.cs`
   - Add timeout handling

3. Create output display modal
   - Add modal for displaying command outputs
   - Implement success/failure toasts with [Display Output] button

4. Update pool creation flow
   - Modify `DriveManagement.razor` to handle async creation
   - Add error handling for failed creation

## Verification Steps

To verify the implementation is working correctly:

1. Backy Agent Verification:
   - [x] Build the project successfully: `cd /home/aundhe1m/backy/Backy.Agent && sudo dotnet build`
   - [ ] Verify initial response returns quickly with "creating" status
   - [ ] Verify pool creation continues in background
   - [ ] Verify `/api/v1/pools/{poolGroupGuid}` returns correct status during and after creation
   - [ ] Verify `/api/v1/pools/{poolGroupGuid}/output` returns command outputs
   - [ ] Verify error handling works with appropriate cleanup

2. Backy Frontend Verification:
   - [x] Build the project successfully: `cd /home/aundhe1m/backy/Backy && dotnet build`
   - [ ] Verify create pool modal closes immediately after submission
   - [ ] Verify placeholder card appears during creation
   - [ ] Verify card transitions to regular card after completion
   - [ ] Verify success toast appears with [Display Output] button
   - [ ] Verify output modal displays command outputs correctly
   - [ ] Verify error handling shows appropriate notifications

## Instructions for AI Implementation

When implementing this solution, follow these guidelines:

1. Start with the Backy.Agent implementation first, completing Phase 1 before moving to the frontend.

2. For each file modification:
   - Understand the current code structure and patterns
   - Preserve existing code style and naming conventions
   - Use existing error handling patterns
   - Add clear comments for new functionality

3. After each significant change, build the project to verify it compiles:
   - For Backy.Agent: `cd /home/aundhe1m/backy/Backy.Agent && sudo dotnet build`
   - For Backy Frontend: `cd /home/aundhe1m/backy/Backy && dotnet build`
   - Fix any build errors or warnings immediately

4. When implementing background tasks:
   - Use .NET's built-in background task capabilities (IHostedService or BackgroundService)
   - Ensure proper cancellation support
   - Handle exceptions gracefully

5. For frontend components:
   - Match existing styling patterns
   - Reuse existing components where possible
   - Maintain accessibility features

6. Test edge cases:
   - What happens if pool creation takes longer than expected?
   - What happens if the agent restarts during pool creation?
   - How is error state handled?

7. Document all new API endpoints thoroughly in [backy-agent.md](./docs/development/backy-agent.md)

8. Provide a summary of changes made after each implementation phase.
