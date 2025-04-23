# Code Improvements

This document outlines planned improvements for the Backy.Agent codebase, providing detailed implementation suggestions and a phased approach to enhance reliability, compatibility, and maintainability.

## lsblk Version Compatibility Issues

### Problem Description
There are significant differences between lsblk versions released with different Ubuntu releases:

- Ubuntu 20.04: util-linux 2.34
- Ubuntu 22.04: util-linux 2.37
- Ubuntu 24.04: util-linux 2.39

This creates a compatibility issue since lsblk with versions lower than util-linux 2.39 doesn't support the `ID-LINK` flag. When running on older Ubuntu versions, this causes command failures.

### Analysis of ID-LINK Usage
After reviewing the codebase, the ID-LINK property is a critical part of mounting the mdadm pool using `by-id`, while there is a backup using the device path, it is prefered to use `/dev/disk/by-id/`.

### Implementation Recommendation
Based on the analysis, **the safest approach is to completely remove the ID-LINK flag** from the lsblk command rather than implementing version detection. This will ensure compatibility across all Ubuntu versions without adding unnecessary complexity:

1. Remove the `ID-LINK` flag from the lsblk command in `DriveMonitoringService.RefreshDrivesAsync`:
   ```csharp
   // Change from:
   var result = await _commandService.ExecuteCommandAsync("lsblk -J -b -o NAME,SIZE,TYPE,MOUNTPOINT,UUID,SERIAL,VENDOR,MODEL,FSTYPE,PATH,ID-LINK");
   
   // To:
   var result = await _commandService.ExecuteCommandAsync("lsblk -J -b -o NAME,SIZE,TYPE,MOUNTPOINT,UUID,SERIAL,MODEL,FSTYPE,PATH");
   ```
> Note: In this example the `VENDOR` is removed in favor the the "## Vendor Value Improvement" feature

2. Add a method to the `DriveMonitoringService` to populate the by-id paths using the FileSymlinks method immediately after processing the lsblk output:
   ```csharp
   /// <summary>
   /// Populates the by-id paths for block devices
   /// </summary>
   private void PopulateByIdPaths(List<BlockDevice> devices)
   {
       const string byIdDir = "/dev/disk/by-id";
       
       // Skip if no devices or the by-id directory doesn't exist
       if (devices == null || !devices.Any() || !Directory.Exists(byIdDir))
           return;
       
       try
       {
           // Create a lookup dictionary of device paths
           var devicePaths = devices
               .Where(d => !string.IsNullOrEmpty(d.Path))
               .ToDictionary(d => d.Path, d => d);
           
           // Process each symlink in the by-id directory
           foreach (var linkPath in Directory.EnumerateFiles(byIdDir))
           {
               try
               {
                   var fi = new FileInfo(linkPath);
                   if ((fi.Attributes & FileAttributes.ReparsePoint) == 0) continue;
                   
                   string? target = fi.LinkTarget;
                   if (string.IsNullOrEmpty(target)) continue;
                   
                   // Convert relative to absolute path
                   string fullTarget = Path.GetFullPath(Path.Combine(byIdDir, target));
                   
                   // Check if this target matches one of our devices
                   if (devicePaths.TryGetValue(fullTarget, out var device))
                   {
                       // Store the by-id path in the IdLink property
                       device.IdLink = linkPath;
                   }
               }
               catch (Exception ex)
               {
                   _logger.LogWarning($"Error processing symlink {linkPath}: {ex.Message}");
               }
           }
       }
       catch (Exception ex)
       {
           _logger.LogError($"Error populating by-id paths: {ex.Message}");
       }
   }
   ```

3. Update the RefreshDrivesAsync method to call this new method right after lsblk JSON deserialization:
   ```csharp
   // After deserializing lsblk output
   if (lsblkOutput != null && lsblkOutput.Blockdevices != null)
   {
       // Filter to include only disk type devices
       lsblkOutput.Blockdevices = lsblkOutput.Blockdevices
           .Where(d => d.Type?.ToLowerInvariant() == "disk")
           .ToList();

       // Populate by-id paths since they're no longer provided by lsblk
       PopulateByIdPaths(lsblkOutput.Blockdevices);
       
       // Continue with existing exclusions
       // ...existing code...
   }
   ```

This approach has several key advantages:
1. **Minimal Complexity**: No regex pattern matching for disk types, just direct device path lookup
2. **Optimal Performance**: The FileSymlinks method is the fastest (~2ms in benchmarks)
3. **Direct Integration**: Works with your existing monitoring service architecture
4. **No Risk of Filtering Out Real Disks**: Uses the exact device paths from lsblk to match symlinks

### Code Example

INSERT EXAMPLE OF OPTIMAL METHOD HERE!


## Improved lsblk Handling

### Problem Description
Currently, multiple functions independently run the lsblk command, which is inefficient and can lead to inconsistent data.

### Implementation Suggestion
1. Strengthen the `GetCachedDrives` approach:
   - Update all functions that use lsblk to instead use `_driveMonitoringService.GetCachedDrives()`
   - Add an optional parameter to `GetCachedDrives()` to force refresh if needed
   ```csharp
   public LsblkOutput GetCachedDrives(bool forceRefresh = false)
   {
       if (forceRefresh)
       {
           var refreshTask = RefreshDrivesAsync();
           refreshTask.Wait();
       }
       return _cachedDrives;
   }
   ```
2. Create extension methods to simplify drive queries:
   ```csharp
   public static class DriveQueryExtensions
   {
       public static BlockDevice? FindBySerial(this LsblkOutput output, string serial)
       {
           return output.Blockdevices?.FirstOrDefault(d => 
               !string.IsNullOrEmpty(d.Serial) && 
               d.Serial.Equals(serial, StringComparison.OrdinalIgnoreCase));
       }
       
       public static BlockDevice? FindByPath(this LsblkOutput output, string path)
       {
           return output.Blockdevices?.FirstOrDefault(d => 
               !string.IsNullOrEmpty(d.Path) && 
               d.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
       }
   }
   ```

## Vendor Value Improvement

### Problem Description
The `vendor` field from lsblk is typically only populated with "ATA" for most drives, making it unhelpful for identifying actual drive manufacturers.

### Implementation Suggestion
1. Create a dedicated `VendorInfoService` to replace the reliance on lsblk's vendor field

2. Enhance the drive refresh process to populate the real vendor

3. Insert the real value into the `BlockDevice` model.

## Exclude Root Drive and Protected Drives

### Problem Description
Currently, the implementation to exclude the root drive simply excludes `/dev/sda`, which is not reliable as the root drive could have a different device path.

### Implementation Suggestion
- Create a REST API endpoint for protecting/unprotecting drives by sending the serial as identifier.
  - POST `/api/v1/drives/protect` to protect a drive
  - DELETE `/api/v1/drives/protect` to unprotect a drive
  - Add drives a `protectedDrives` object to the `pool-metadata.json` file

- Create a `DriveExcludeService.cs` that focus on replacing the current implementation (ExcludeDrive) found both in `DriveMonitoringService.cs` and `DriveService.cs`. The new implementation should do the following:
    - Automatically exclude the root drive, with a fallback to `/dev/sda` if failes.
    - Checks the `pool-metadata.json` file if there is any protected drives added by the `/api/v1/drives/protect` API
    - Checks the `appsettings.json` for multiple exclutions formats, for example
        ```json
        "AgentSettings": {
            "ExcludedDrives": [
                "/dev/sdb",
                "/dev/disk/by-id/some-specific-drive-id",
                "/dev/disk/by-uuid/some-specific-drive-id",
            ],
        }
        ```
