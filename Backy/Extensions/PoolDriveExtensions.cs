using Backy.Models;

namespace Backy.Extensions
{
    public static class PoolDriveExtensions
    {
        /// <summary>
        /// Converts a PoolDrive instance to a Drive instance.
        /// </summary>
        /// <param name="poolDrive">The PoolDrive instance to convert.</param>
        /// <returns>A new Drive instance with mapped properties.</returns>
        public static Drive ToDrive(this PoolDrive poolDrive)
        {
            if (poolDrive == null)
                throw new ArgumentNullException(nameof(poolDrive));

            return new Drive
            {
                Name = poolDrive.Label,
                Label = poolDrive.Label,
                Serial = poolDrive.Serial,
                Vendor = poolDrive.Vendor,
                Model = poolDrive.Model,
                Size = poolDrive.Size,
                IsConnected = poolDrive.IsConnected,
                IsMounted = poolDrive.IsMounted,
                IdLink = poolDrive.DevPath,
                // Assuming Partitions are not directly mapped; adjust if necessary
                Partitions = new List<PartitionInfo>()
            };
        }
    }
}