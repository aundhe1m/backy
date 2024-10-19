using System;

namespace Backy.Helpers
{
    public static class SizeFormatter
    {
        /// <summary>
        /// Formats the size from bytes to a human-readable string with one decimal place.
        /// Supports Bytes (B), Kilobytes (KB), Megabytes (MB), Gigabytes (GB), and Terabytes (TB).
        /// </summary>
        /// <param name="sizeInBytes">Size in bytes.</param>
        /// <returns>Formatted size string.</returns>
        public static string FormatSize(long sizeInBytes)
        {
            const double KB = 1024;
            const double MB = KB * 1024;
            const double GB = MB * 1024;
            const double TB = GB * 1024;

            double size = sizeInBytes;
            string unit = "B";

            if (sizeInBytes >= TB)
            {
                size = sizeInBytes / TB;
                unit = "TB";
            }
            else if (sizeInBytes >= GB)
            {
                size = sizeInBytes / GB;
                unit = "GB";
            }
            else if (sizeInBytes >= MB)
            {
                size = sizeInBytes / MB;
                unit = "MB";
            }
            else if (sizeInBytes >= KB)
            {
                size = sizeInBytes / KB;
                unit = "KB";
            }

            // Round to one decimal place
            size = Math.Round(size, 1, MidpointRounding.AwayFromZero);

            return $"{size} {unit}";
        }
    }
}