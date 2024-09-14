using System;
using System.IO;
using System.Diagnostics;

namespace DriveScanner
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Scanning for connected drives (Serial Number Mode)...\n");

            // Get all block devices from /sys/class/block
            string sysBlockPath = "/sys/class/block";
            var devices = Directory.GetDirectories(sysBlockPath);

            foreach (var devicePath in devices)
            {
                var deviceName = Path.GetFileName(devicePath);

                // Ignore loop devices and device-mapper (dm) devices
                if (deviceName.StartsWith("loop") || deviceName.StartsWith("sr") || deviceName.StartsWith("dm"))
                    continue;

                var sizeFile = Path.Combine(devicePath, "size");
                string size = File.Exists(sizeFile) ? ReadSize(sizeFile) : "Unknown size";

                // Try to get serial number using udevadm
                string serial = GetSerialFromUdev(deviceName);
                if (string.IsNullOrEmpty(serial))
                {
                    serial = "No Serial Number";
                }

                Console.WriteLine($"Name: {deviceName}, Size: {size}, Serial: {serial}");
            }

            Console.WriteLine("\nScanning complete.");
        }

        // Method to get Serial Number using udevadm
        static string GetSerialFromUdev(string deviceName)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "udevadm",
                        Arguments = $"info --query=property --name=/dev/{deviceName}",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // Search for ID_SERIAL property
                foreach (var line in output.Split('\n'))
                {
                    if (line.StartsWith("ID_SERIAL="))
                    {
                        return line.Split('=')[1].Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting Serial using udevadm for {deviceName}: {ex.Message}");
            }

            return null;
        }

        // Convert the size from blocks to GB or MB (size is in 512-byte blocks)
        static string ReadSize(string filePath)
        {
            try
            {
                long blocks = long.Parse(File.ReadAllText(filePath).Trim());
                long bytes = blocks * 512; // Convert blocks to bytes
                if (bytes >= 1_000_000_000)
                {
                    return $"{bytes / 1_000_000_000.0:F2} GB";
                }
                else if (bytes >= 1_000_000)
                {
                    return $"{bytes / 1_000_000.0:F2} MB";
                }
                else
                {
                    return $"{bytes / 1_000.0:F2} KB";
                }
            }
            catch (Exception ex)
            {
                return $"Error reading size: {ex.Message}";
            }
        }
    }
}
