using Backy.Agent.Models;
using Backy.Agent.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backy.Agent.Endpoints;

public static class DriveEndpoints
{
    public static void MapDriveEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/drives")
            .WithTags("Drives");

        // GET /api/v1/drives - List all available drives
        group.MapGet("/", async (IDriveService driveService) =>
        {
            try
            {
                var drives = await driveService.GetDrivesAsync();
                return Results.Ok(drives);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Error retrieving drives",
                    statusCode: 500);
            }
        })
        .WithName("GetDrives")
        .WithOpenApi(operation => 
        {
            operation.Summary = "Lists all available drives";
            operation.Description = "Returns a list of all physical drives in the system, excluding specified ones.";
            return operation;
        });

        // GET /api/v1/drives/{serial}/status - Get detailed status of a specific drive
        group.MapGet("/{serial}/status", async (string serial, IDriveService driveService) =>
        {
            try
            {
                if (string.IsNullOrEmpty(serial))
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "VALIDATION_ERROR",
                            Message = "Serial number is required"
                        }
                    });
                }

                var driveStatus = await driveService.GetDriveStatusAsync(serial);
                
                if (driveStatus.Status == "not_found")
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "NOT_FOUND",
                            Message = $"Drive with serial {serial} not found"
                        }
                    });
                }
                
                return Results.Ok(driveStatus);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    title: "Error retrieving drive status",
                    statusCode: 500);
            }
        })
        .WithName("GetDriveStatus")
        .WithOpenApi(operation => 
        {
            operation.Summary = "Get detailed status of a specific drive";
            operation.Description = "Returns information about a drive's status, whether it's part of a pool, and processes using it.";
            return operation;
        });
    }
}