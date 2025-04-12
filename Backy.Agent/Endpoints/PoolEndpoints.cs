using Backy.Agent.Models;
using Backy.Agent.Services;

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
- poolId: The mdadm device name (e.g., md0)
- poolGroupGuid: Stable identifier for the pool across reboots
- label: A user-friendly name for the pool
- status: Current status (Active, Degraded, etc.)
- mountPath: Current mount path (if mounted)
- isMounted: Whether the pool is currently mounted
- driveCount: Number of drives in the array";
            return operation;
        });

        // POST /api/v1/pools - Create a new RAID1 pool
        group.MapPost("/", async (PoolCreationRequestExtended request, IPoolService poolService) =>
        {
            try
            {
                var result = await poolService.CreatePoolAsync(request);
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

                return Results.Ok(new PoolCreationResponse
                {
                    Success = true,
                    PoolId = result.PoolId,
                    MountPath = result.MountPath,
                    Status = "Active",
                    CommandOutputs = result.Outputs
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Error creating pool",
                    statusCode: 500);
            }
        })
        .WithName("CreatePool")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Create a new RAID1 pool";
            operation.Description = @"Creates a new RAID1 array using the specified drives and mounts it at the provided path.

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
  ""label"": ""backup-2"",
  ""driveSerials"": [""WD-WCAV5L386641"", ""SanDisk-AA12345678""],
  ""driveLabels"": {
    ""WD-WCAV5L386641"": ""backup-2-disk1"",
    ""SanDisk-AA12345678"": ""backup-2-disk2""
  },
  ""mountPath"": ""/mnt/backy/backup-2""
}
```

Notes:
- The driveSerials array must contain the serial numbers of physical disk drives
- The serial numbers must match exactly what is reported by the system
- driveLabels is a dictionary mapping serial numbers to user-friendly labels
- mountPath should be an absolute path where the pool will be mounted
- poolGroupGuid is optional and helps maintain consistent pool identification across system reboots";
            return operation;
        });

        // GET /api/v1/pools/{poolId} - Get pool status and details by mdadm device
        group.MapGet("/{poolId}", async (string poolId, IPoolService poolService) =>
        {
            try
            {
                if (string.IsNullOrEmpty(poolId))
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "VALIDATION_ERROR",
                            Message = "Pool ID is required"
                        }
                    });
                }

                var result = await poolService.GetPoolDetailAsync(poolId);
                if (!result.Success)
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "NOT_FOUND",
                            Message = result.Message
                        }
                    });
                }
                
                return Results.Ok(result.PoolDetail);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Error retrieving pool details",
                    statusCode: 500);
            }
        })
        .WithName("GetPoolDetails")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get pool status and details by mdadm device ID";
            operation.Description = @"Returns details about the pool including status, size, usage, and drive information.
This endpoint will return a 404 status code if the pool does not exist.

Note that the pool ID in the URL refers to the mdadm device name (e.g., md0).
For stable identification across reboots, use the /guid/{poolGroupGuid} endpoint instead.";
            return operation;
        });

        // GET /api/v1/pools/guid/{poolGroupGuid} - Get pool status and details by GUID
        group.MapGet("/guid/{poolGroupGuid}", async (Guid poolGroupGuid, IPoolService poolService) =>
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

                var result = await poolService.GetPoolDetailByGuidAsync(poolGroupGuid);
                if (!result.Success)
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "NOT_FOUND",
                            Message = result.Message
                        }
                    });
                }
                
                return Results.Ok(result.PoolDetail);
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
using the stable poolGroupGuid identifier. This is the recommended approach for the frontend application
as it provides consistent identification across system reboots.

This endpoint will return a 404 status code if the pool does not exist.";
            return operation;
        });

        // POST /api/v1/pools/guid/{poolGroupGuid}/mount - Mount a pool by GUID
        group.MapPost("/guid/{poolGroupGuid}/mount", async (Guid poolGroupGuid, MountRequest request, IPoolService poolService) =>
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
This is the recommended approach for the frontend application.

Request Example:
```json
{
  ""mountPath"": ""/mnt/backy/backup-1""
}
```";
            return operation;
        });

        // POST /api/v1/pools/guid/{poolGroupGuid}/unmount - Unmount a pool by GUID
        group.MapPost("/guid/{poolGroupGuid}/unmount", async (Guid poolGroupGuid, IPoolService poolService) =>
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

        // POST /api/v1/pools/guid/{poolGroupGuid}/remove - Remove a pool by GUID
        group.MapPost("/guid/{poolGroupGuid}/remove", async (Guid poolGroupGuid, IPoolService poolService) =>
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
4. Removes the RAID device";
            return operation;
        });

        // POST /api/v1/pools/metadata/remove - Remove pool metadata
        group.MapPost("/metadata/remove", async (PoolMetadataRemovalRequest request, IPoolService poolService) =>
        {
            try
            {
                if (request == null)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "VALIDATION_ERROR",
                            Message = "Request body is required"
                        }
                    });
                }

                // Validate request - at least one identifier or removeAll must be specified
                if (!request.RemoveAll && 
                    string.IsNullOrEmpty(request.PoolId) && 
                    request.PoolGroupGuid == null)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "VALIDATION_ERROR",
                            Message = "Either removeAll must be true or at least one identifier (poolId or poolGroupGuid) must be specified"
                        }
                    });
                }

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
        .WithName("RemovePoolMetadata")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Remove pool metadata";
            operation.Description = @"Removes mapping metadata between poolId and poolGroupGuid. 
Use this endpoint to remove specific metadata entries or clear all metadata.

Request Examples:

Remove by specific pool ID:
```json
{
  ""poolId"": ""md0"",
  ""removeAll"": false
}
```

Remove by poolGroupGuid:
```json
{
  ""poolGroupGuid"": ""550e8400-e29b-41d4-a716-446655440000"",
  ""removeAll"": false
}
```

Remove all metadata:
```json
{
  ""removeAll"": true
}
```";
            return operation;
        });

        // POST /api/v1/pools/metadata/validate - Validate and update pool metadata
        group.MapPost("/metadata/validate", async (IPoolService poolService) =>
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

        // Keep the legacy endpoints for backward compatibility
        MapLegacyPoolEndpoints(app);
    }

    private static void MapLegacyPoolEndpoints(WebApplication app)
    {
        // Legacy endpoint implementations for backward compatibility
        var group = app.MapGroup("/api/v1/pools")
            .WithTags("Legacy Pools");

        // Add any legacy endpoints here if needed
        // This empty implementation fixes the compiler error while allowing for future additions
    }
}