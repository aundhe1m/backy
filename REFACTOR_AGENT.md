# Refactor Pool Creating Routine

Improve the current pool creation process in the Backy application by addressing timing issues between the Backy App and Backy Agent.

## Current State

The current pool creation process has significant asynchronous features already implemented, but still has a few areas that need improvement:

1. **Status Ambiguity**: There's ambiguity between operation statuses (creating, failed) and pool health statuses (active, degraded) because they both use a field called "status" in different contexts.

2. **Timing Issues**: While the frontend properly handles asynchronous pool creation with polling, there's a potential confusion in the backend when checking if a pool creation is complete.

3. **Persistent Storage of Failed Operations**: Failed pool creations might still be partially added to the pool-metadata.json file, potentially leading to an inconsistent state.

The current frontend implementation already has good support for asynchronous operations:
- The PoolGroupDriveCard component shows a "Creating" state with a progress bar
- The DriveManagement component maintains an in-memory list of creating pools
- The frontend polls the status endpoint with exponential backoff
- The UI properly disables operations on pools that are still being created

However, the backend API could be improved by:
- Clearly distinguishing between operation state and pool health status
- Ensuring failed operations aren't persisted in the metadata file
- Removing command outputs from the status response

## What We Want to Achieve

The refactoring aims to achieve the following:

1. **Clear Status Distinction**: Separate operational state from pool health status to avoid confusion.

2. **Better Failure Handling**: Implement a cleaner approach to handling failed pool creations.

3. **Clean Persistent Storage**: Avoid storing failed operations in the persistent metadata file.

The main improvements we want to make are:

1. Add a distinct "state" property to clearly separate operational status from pool health status
2. Rename current `status` to `poolStatus` which will reflect the MDADM status
3. Remove command outputs from status responses and keep them only in the dedicated endpoint
4. Prevent failed pool creations from being persisted in the pool-metadata.json file

## Implementation Details

### State vs. Status Distinction
- Introduce a new `state` property to distinguish operational status from pool health status
- `state` values: "creating", "ready", "failed", "error", "unmounted", "deleted"
- Rename current `status` to `poolStatus` which will reflect the MDADM status (e.g., "active", "resync")
- The `state` indicates operations initiated by users, while `poolStatus` reflects the health of the pool

### Error Handling
- If pool creation fails, the agent will:
  1. Attempt to clean up any partially created resources (stop MD arrays, unmount filesystems)
  2. Set the pool state to "failed"
  3. Not save any entries to the pool-metadata.json file for failed operations
  4. Preserve command outputs including error messages for debugging

### Persistent Storage Considerations
- Only successful pool creations should be written to the pool-metadata.json file
- Failed operations should remain in-memory only until the outputs are retrieved or the agent restarts
- This prevents bad states from being persisted and affecting system stability

## Expected API Responses

### Initial Response (POST /api/v1/pools)
```json
{
  "success": true,
  "poolGroupGuid": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "state": "creating"
}
```

### Pool Status Response (GET /api/v1/pools/{poolGroupGuid})
During creation:
```json
{
  "state": "creating",
  "poolStatus": null,
  "drives": []
}
```

After successful creation:
```json
{
  "state": "ready",
  "poolStatus": "active",
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
  "state": "failed",
  "errorMessage": "Failed to create RAID array"
}
```

## Implementation Plan

Given that the frontend already has good support for asynchronous pool creation, we'll focus on improving the backend API to better support the frontend:

1. **Update Model Classes (Backend):**
   - Modify `PoolOperationStatus` to rename `status` to `state`
   - Update `PoolDetailResponse` to include both `state` and `poolStatus` (renamed from `status`) properties

2. **Modify PoolOperationManager (Backend):**
   - Update the `StartPoolCreationAsync` method to use the new state terminology
   - Ensure failure handling correctly sets state to "failed"
   - Remove commandOutputs from the status response and keep them only in the outputs endpoint

3. **Update PoolEndpoints (Backend):**
   - Modify the `/api/v1/pools/{poolGroupGuid}` endpoint to return the proper state
   - Ensure the outputs endpoint only includes command outputs
   - Add appropriate error handling for all state transitions

4. **Update PoolService (Backend):**
   - Modify `CreatePoolAsync` to work with the new state model
   - Ensure failed pools aren't added to pool-metadata.json
   - Implement proper state transitions during pool lifecycles

5. **Testing:**
   - Test creation of single-drive pools
   - Test creation of multi-drive pools
   - Test failure scenarios (e.g., invalid drives, permission issues)
   - Test agent restart during pool creation

## Related Files

### Backy.Agent Files
- `/home/aundhe1m/backy/Backy.Agent/Endpoints/PoolEndpoints.cs` - API endpoints for pool operations
- `/home/aundhe1m/backy/Backy.Agent/Services/PoolService.cs` - Service for pool-related operations
- `/home/aundhe1m/backy/Backy.Agent/Services/PoolOperationManager.cs` - Manager for background pool operations
- `/home/aundhe1m/backy/Backy.Agent/Models/DriveModels.cs` - Model definitions for pools and drives

### Backy Frontend Files
- `/home/aundhe1m/backy/Backy/Components/PoolGroupDriveCard.razor` - Component for displaying pool details
- `/home/aundhe1m/backy/Backy/Components/Pages/DriveManagement.razor` - Page containing pool creation flow
- `/home/aundhe1m/backy/Backy/Services/BackyAgentClient.cs` - Client for communicating with Backy Agent
- `/home/aundhe1m/backy/Backy/Services/AgentDriveService.cs` - Service that handles pool operations
- `/home/aundhe1m/backy/Backy/Services/DriveRefreshService.cs` - Service that periodically refreshes drive information

## Instructions for AI Implementation

When implementing this solution, follow these guidelines:

- For each file modification:
   - Understand the current code structure and patterns
   - Preserve existing code style and naming conventions
   - Use existing error handling patterns
   - Add clear comments for new functionality

- After each significant change, build the project to verify it compiles:
   - For Backy.Agent: `cd /home/aundhe1m/backy/Backy.Agent && sudo dotnet build`
   - For Backy Frontend: `cd /home/aundhe1m/backy/Backy && dotnet build`
   - Fix any build errors or warnings immediately

- Document all new API endpoints thoroughly in [backy-agent.md](./docs/development/backy-agent.md)

- Provide a summary of changes made after each implementation phase.
