using Backy.Agent.Models;
using Backy.Agent.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backy.Agent.Endpoints;

public static class PoolEndpoints
{
    public static void MapPoolEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/pools")
            .WithTags("Pools");

        // GET /api/v1/pools - List all existing mdadm pools
        group.MapGet("/", async (IPoolService poolService) =>
        {
            try
            {
                var pools = await poolService.GetAllPoolsAsync();
                return Results.Ok(pools);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Error retrieving pools",
                    statusCode: 500);
            }
        })
        .WithName("GetAllPools")
        .WithOpenApi(operation =>
        {
            operation.Summary = "List all existing mdadm pools";
            operation.Description = @"Returns information about all mdadm RAID arrays on the system.

Each pool includes the following information:
- poolGroupGuid: Stable identifier for the pool across reboots
- label: A user-friendly name for the pool
- status: Current status (Active, Degraded, etc.)
- mountPath: Current mount path (if mounted)
- isMounted: Whether the pool is currently mounted
- drives: Array of drives in the pool with serial numbers and connection status";
            return operation;
        });

        // POST /api/v1/pools - Create a new RAID1 pool (async)
        group.MapPost("/", async ([FromBody] PoolCreationRequestExtended request, IPoolOperationManager poolOperationManager) =>
        {
            try
            {
                // Basic validation 
                if (string.IsNullOrWhiteSpace(request.Label))
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "VALIDATION_ERROR",
                            Message = "Pool label is required"
                        }
                    });
                }

                if (request.DriveSerials == null || !request.DriveSerials.Any())
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "VALIDATION_ERROR",
                            Message = "At least one drive is required to create a pool"
                        }
                    });
                }

                if (string.IsNullOrWhiteSpace(request.MountPath))
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "VALIDATION_ERROR",
                            Message = "Mount path is required"
                        }
                    });
                }

                if (!request.MountPath.StartsWith("/"))
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "VALIDATION_ERROR",
                            Message = "Mount path must be absolute"
                        }
                    });
                }

                // Start asynchronous pool creation
                var poolGroupGuid = await poolOperationManager.StartPoolCreationAsync(request);

                // Return immediate response with the operation ID (poolGroupGuid)
                return Results.Accepted(
                    $"/api/v1/pools/{poolGroupGuid}", 
                    new PoolCreationResponse
                    {
                        Success = true,
                        PoolGroupGuid = poolGroupGuid,
                        Status = "creating"
                    });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Error initiating pool creation",
                    statusCode: 500);
            }
        })
        .WithName("CreatePool")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Create a new RAID1 pool (asynchronously)";
            operation.Description = @"Creates a new RAID1 array using the specified drives and mounts it at the provided path.
This is an asynchronous operation that returns immediately with a status of 'creating'.
Use the standard pool details endpoint to check the progress and final result.

Example for a single drive with poolGroupGuid:
```json
{
  ""label"": ""backup-1"",
  ""driveSerials"": [""WD-WCAV5L386641""],
  ""driveLabels"": {
    ""WD-WCAV5L386641"": ""backup-drive-1""
  },
  ""mountPath"": ""/mnt/backy/backup-1"",
  ""poolGroupGuid"": ""550e8400-e29b-41d4-a716-446655440000""
}
```

Example for multiple drives:
```json
{
  ""label"": ""pool1"",
  ""driveSerials"": [""drive-scsi1"", ""drive-scsi2""],
  ""driveLabels"": {
    ""drive-scsi1"": ""pool1-disk1"",
    ""drive-scsi2"": ""pool1-disk2""
  },
  ""mountPath"": ""/mnt/backy/3fa85f64-5717-4562-b3fc-2c963f66afa6"",
  ""poolGroupGuid"": ""3fa85f64-5717-4562-b3fc-2c963f66afa6""
}
```

Notes:
- The driveSerials array must contain the serial numbers of physical disk drives
- The serial numbers must match exactly what is reported by the system
- driveLabels is a dictionary mapping serial numbers to user-friendly labels
- mountPath should be an absolute path where the pool will be mounted
- poolGroupGuid is optional and helps maintain consistent pool identification across system reboots
- The operation will be completed asynchronously to avoid timeouts";
            return operation;
        });

        // GET /api/v1/pools/{poolGroupGuid} - Get pool status and details by GUID
        group.MapGet("/{poolGroupGuid}", async (Guid poolGroupGuid, IPoolService poolService, IPoolOperationManager poolOperationManager) =>
        {
            try
            {
                if (poolGroupGuid == Guid.Empty)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "VALIDATION_ERROR",
                            Message = "Valid Pool GUID is required"
                        }
                    });
                }

                // First check if this is a pool that's currently being created or recently completed
                var operationStatus = await poolOperationManager.GetOperationStatusAsync(poolGroupGuid);
                if (operationStatus != null)
                {
                    // Return simplified pool detail for pools being created
                    if (operationStatus.Status == "creating")
                    {
                        return Results.Ok(new PoolDetailResponse
                        {
                            Status = "creating",
                            MountPath = operationStatus.MountPath ?? string.Empty,
                            Drives = new List<PoolDriveStatus>()
                        });
                    }
                    else if (operationStatus.Status == "failed")
                    {
                        return Results.BadRequest(new ErrorResponse
                        {
                            Error = new ErrorDetail
                            {
                                Code = "CREATION_FAILED",
                                Message = operationStatus.ErrorMessage ?? "Pool creation failed",
                                Details = "Use the pool outputs endpoint to see detailed error information"
                            }
                        });
                    }
                    else if (operationStatus.Status == "active")
                    {
                        // Pool was recently created, let's check if the metadata is available yet
                        var result = await poolService.GetPoolDetailByGuidAsync(poolGroupGuid);
                        
                        if (result.Success)
                        {
                            // Metadata is available, return the pool details
                            return Results.Ok(result.PoolDetail);
                        }
                        else
                        {
                            // Metadata not available yet, return basic active pool information from operation status
                            // This prevents the 404 during the small window after physical creation but before metadata is saved
                            return Results.Ok(new PoolDetailResponse
                            {
                                Status = "active",
                                MountPath = operationStatus.MountPath ?? string.Empty,
                                Drives = new List<PoolDriveStatus>() // Empty drives list as we don't have metadata yet
                            });
                        }
                    }
                }

                // If not in creation or creation has completed successfully, get the pool details
                var detailResult = await poolService.GetPoolDetailByGuidAsync(poolGroupGuid);
                if (!detailResult.Success)
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "NOT_FOUND",
                            Message = detailResult.Message
                        }
                    });
                }
                
                return Results.Ok(detailResult.PoolDetail);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Error retrieving pool details",
                    statusCode: 500);
            }
        })
        .WithName("GetPoolDetailsByGuid")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get pool status and details by pool GUID";
            operation.Description = @"Returns details about the pool including status, size, usage, and drive information
using the stable poolGroupGuid identifier.

This endpoint will return a 404 status code if the pool does not exist.

For pools that are currently being created, this endpoint will return a simplified response with status='creating'.
For pools that failed creation, this endpoint will return an error response with details about the failure.";
            return operation;
        });

        // Add new endpoint: GET /api/v1/pools/{poolGroupGuid}/outputs - Get pool creation outputs
        group.MapGet("/{poolGroupGuid}/outputs", async (Guid poolGroupGuid, IPoolOperationManager poolOperationManager) =>
        {
            try
            {
                if (poolGroupGuid == Guid.Empty)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "VALIDATION_ERROR",
                            Message = "Valid Pool GUID is required"
                        }
                    });
                }

                var outputs = await poolOperationManager.GetOperationOutputsAsync(poolGroupGuid);
                if (outputs == null)
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "NOT_FOUND",
                            Message = $"No operation found for pool GUID {poolGroupGuid}"
                        }
                    });
                }

                return Results.Ok(new PoolCommandOutputResponse
                {
                    Outputs = outputs
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Error retrieving pool creation outputs",
                    statusCode: 500);
            }
        })
        .WithName("GetPoolCreationOutputs")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get pool creation command outputs";
            operation.Description = @"Returns the command outputs from an asynchronous pool creation operation.
This is useful for debugging and monitoring the progress of the operation.";
            return operation;
        });

        // POST /api/v1/pools/{poolGroupGuid}/mount - Mount a pool by GUID
        group.MapPost("/{poolGroupGuid}/mount", async (Guid poolGroupGuid, [FromBody] MountRequest request, IPoolService poolService) =>
        {
            try
            {
                if (poolGroupGuid == Guid.Empty)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "VALIDATION_ERROR",
                            Message = "Valid Pool GUID is required"
                        }
                    });
                }

                var result = await poolService.MountPoolByGuidAsync(poolGroupGuid, request.MountPath);
                if (!result.Success)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "SYSTEM_ERROR",
                            Message = result.Message,
                            Details = string.Join("\n", result.Outputs)
                        }
                    });
                }

                return Results.Ok(new CommandResponse
                {
                    Success = true,
                    CommandOutputs = result.Outputs
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Error mounting pool",
                    statusCode: 500);
            }
        })
        .WithName("MountPoolByGuid")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Mount a pool by GUID";
            operation.Description = @"Assembles a RAID array and mounts it at the specified path using the stable poolGroupGuid identifier.

Request Example:
```json
{
  ""mountPath"": ""/mnt/backy/backup-1""
}
```";
            return operation;
        });

        // DELETE /api/v1/pools/{poolGroupGuid}/mount - Unmount a pool by GUID
        group.MapDelete("/{poolGroupGuid}/mount", async (Guid poolGroupGuid, IPoolService poolService) =>
        {
            try
            {
                if (poolGroupGuid == Guid.Empty)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "VALIDATION_ERROR",
                            Message = "Valid Pool GUID is required"
                        }
                    });
                }

                var result = await poolService.UnmountPoolByGuidAsync(poolGroupGuid);
                if (!result.Success)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "SYSTEM_ERROR",
                            Message = result.Message,
                            Details = string.Join("\n", result.Outputs)
                        }
                    });
                }

                return Results.Ok(new CommandResponse
                {
                    Success = true,
                    CommandOutputs = result.Outputs
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Error unmounting pool",
                    statusCode: 500);
            }
        })
        .WithName("UnmountPoolByGuid")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Unmount a pool by GUID";
            operation.Description = @"Unmounts a RAID array and stops it using the stable poolGroupGuid identifier.
This is the recommended approach for the frontend application.";
            return operation;
        });

        // DELETE /api/v1/pools/{poolGroupGuid} - Remove a pool by GUID
        group.MapDelete("/{poolGroupGuid}", async (Guid poolGroupGuid, IPoolService poolService) =>
        {
            try
            {
                if (poolGroupGuid == Guid.Empty)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "VALIDATION_ERROR",
                            Message = "Valid Pool GUID is required"
                        }
                    });
                }

                var result = await poolService.RemovePoolByGuidAsync(poolGroupGuid);
                if (!result.Success)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "SYSTEM_ERROR",
                            Message = result.Message,
                            Details = string.Join("\n", result.Outputs)
                        }
                    });
                }
                
                // After successfully removing the pool, also remove its metadata
                var metadataRequest = new PoolMetadataRemovalRequest { PoolGroupGuid = poolGroupGuid };
                await poolService.RemovePoolMetadataAsync(metadataRequest);

                return Results.Ok(new CommandResponse
                {
                    Success = true,
                    CommandOutputs = result.Outputs
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Error removing pool",
                    statusCode: 500);
            }
        })
        .WithName("RemovePoolByGuid")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Remove a pool by GUID";
            operation.Description = @"Unmounts a RAID array, stops it, and removes it completely using the stable poolGroupGuid identifier.
This is the recommended approach for the frontend application.

This operation automatically:
1. Unmounts the pool (if mounted)
2. Stops the RAID array
3. Wipes filesystem signatures from the member drives
4. Removes the RAID device
5. Removes the pool metadata";
            return operation;
        });

        // DELETE /api/v1/pools/metadata/{poolGroupGuid} - Remove specific pool metadata by GUID
        group.MapDelete("/metadata/{poolGroupGuid}", async (Guid poolGroupGuid, IPoolService poolService) =>
        {
            try
            {
                if (poolGroupGuid == Guid.Empty)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "VALIDATION_ERROR",
                            Message = "Valid Pool GUID is required"
                        }
                    });
                }

                var request = new PoolMetadataRemovalRequest
                {
                    PoolGroupGuid = poolGroupGuid,
                    RemoveAll = false
                };

                var result = await poolService.RemovePoolMetadataAsync(request);
                if (!result.Success)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "VALIDATION_ERROR",
                            Message = result.Message
                        }
                    });
                }

                return Results.Ok(new { success = true, message = result.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Error removing pool metadata",
                    statusCode: 500);
            }
        })
        .WithName("RemovePoolMetadataByGuid")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Remove specific pool metadata by GUID";
            operation.Description = @"Removes metadata entry for a specific pool identified by its GUID.
This operation only removes the metadata mapping and does not affect the actual RAID array.";
            return operation;
        });

        // DELETE /api/v1/pools/metadata - Remove all pool metadata
        group.MapDelete("/metadata", async (IPoolService poolService) =>
        {
            try
            {
                var request = new PoolMetadataRemovalRequest
                {
                    RemoveAll = true
                };

                var result = await poolService.RemovePoolMetadataAsync(request);
                if (!result.Success)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "VALIDATION_ERROR",
                            Message = result.Message
                        }
                    });
                }

                return Results.Ok(new { success = true, message = result.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Error removing pool metadata",
                    statusCode: 500);
            }
        })
        .WithName("RemoveAllPoolMetadata")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Remove all pool metadata";
            operation.Description = @"Removes all stored pool metadata entries.
This operation only removes the metadata mappings and does not affect the actual RAID arrays.";
            return operation;
        });

        // PUT /api/v1/pools/metadata/validate - Validate and update pool metadata
        group.MapPut("/metadata/validate", async (IPoolService poolService) =>
        {
            try
            {
                var result = await poolService.ValidateAndUpdatePoolMetadataAsync();
                
                if (!result.Success)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "VALIDATION_ERROR",
                            Message = result.Message
                        }
                    });
                }

                return Results.Ok(new 
                { 
                    success = true, 
                    message = result.Message, 
                    fixedEntries = result.FixedEntries 
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Error validating pool metadata",
                    statusCode: 500);
            }
        })
        .WithName("ValidatePoolMetadata")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Validate and update pool metadata";
            operation.Description = @"Validates and updates the pool metadata file by checking if the mdadm device names match the actual system devices.
This is useful after a system reboot when mdadm devices may be assigned different names (e.g., md0 becomes md127).

The endpoint will check each pool's drive serials against the current mdadm arrays and update the metadata accordingly.";
            return operation;
        });

    }
}