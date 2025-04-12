using Backy.Agent.Models;
using Backy.Agent.Services;

namespace Backy.Agent.Endpoints;

public static class SystemEndpoints
{
    public static void MapSystemEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1")
            .WithTags("System Operations");

        // GET /api/v1/mounts/{path}/processes - Get processes using a mount point
        group.MapGet("/mounts/{*path}", async (string path, IDriveService driveService) =>
        {
            try
            {
                if (string.IsNullOrEmpty(path))
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

                // Normalize path (ensure it starts with /)
                path = path.StartsWith("/") ? path : "/" + path;

                var processes = await driveService.GetProcessesUsingMountPointAsync(path);
                return Results.Ok(new ProcessesResponse
                {
                    Processes = processes
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Error retrieving processes",
                    statusCode: 500);
            }
        })
        .WithName("GetProcessesUsingMountPoint")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get processes using a mount point";
            operation.Description = "Returns a list of processes that are using the specified mount point.";
            return operation;
        });

        // GET /api/v1/pools/guid/{poolGroupGuid}/processes - Get processes using a pool by GUID
        group.MapGet("/pools/guid/{poolGroupGuid}/processes", async (Guid poolGroupGuid, IPoolService poolService, IDriveService driveService) =>
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

                // Get the pool info to find the mount path
                var poolResult = await poolService.GetPoolDetailByGuidAsync(poolGroupGuid);
                if (!poolResult.Success)
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "NOT_FOUND",
                            Message = poolResult.Message
                        }
                    });
                }

                // Check if the pool is mounted
                if (string.IsNullOrEmpty(poolResult.PoolDetail?.MountPath))
                {
                    return Results.Ok(new ProcessesResponse
                    {
                        Processes = new List<ProcessInfo>()
                    });
                }

                var processes = await driveService.GetProcessesUsingMountPointAsync(poolResult.PoolDetail.MountPath);
                return Results.Ok(new ProcessesResponse
                {
                    Processes = processes
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Error retrieving processes",
                    statusCode: 500);
            }
        })
        .WithName("GetProcessesUsingPoolByGuid")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get processes using a pool by GUID";
            operation.Description = "Returns a list of processes that are using the specified pool, identified by its poolGroupGuid. " +
                                   "This is the recommended approach for the frontend application as it provides consistent identification across system reboots.";
            return operation;
        });

        // POST /api/v1/processes/kill - Kill specified processes
        group.MapPost("/processes/kill", async (ProcessesRequest request, IDriveService driveService) =>
        {
            try
            {
                if (request.Pids == null || !request.Pids.Any())
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "VALIDATION_ERROR",
                            Message = "Process IDs are required"
                        }
                    });
                }

                var result = await driveService.KillProcessesAsync(request);
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
                    title: "Error killing processes",
                    statusCode: 500);
            }
        })
        .WithName("KillProcesses")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Kill specified processes";
            operation.Description = @"Kills the specified processes by their process IDs (PIDs).

Request Example:
```json
{
  ""pids"": [1234, 5678],
  ""poolGroupGuid"": ""550e8400-e29b-41d4-a716-446655440000""
}
```

Notes:
- The `pids` array must contain at least one process ID
- The `poolGroupGuid` parameter is optional and is used for tracking context only";
            return operation;
        });
    }
}