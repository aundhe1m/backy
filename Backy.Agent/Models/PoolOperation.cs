using System;
using System.Text.Json.Serialization;

namespace Backy.Agent.Models
{
    /// <summary>
    /// Represents a pool operation with tracking information
    /// </summary>
    public class PoolOperation
    {
        /// <summary>
        /// Unique identifier for the operation
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// The GUID of the pool group this operation is associated with
        /// </summary>
        public Guid PoolGroupGuid { get; set; }

        /// <summary>
        /// Type of the operation
        /// </summary>
        public PoolOperationType OperationType { get; set; }

        /// <summary>
        /// Current status of the operation
        /// </summary>
        public PoolOperationStatus Status { get; set; }

        /// <summary>
        /// Human-readable description of the operation
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Detailed status message providing more information about the current state
        /// </summary>
        public string? StatusMessage { get; set; }

        /// <summary>
        /// Result message when the operation is completed
        /// </summary>
        public string? ResultMessage { get; set; }

        /// <summary>
        /// Current progress percentage (0-100)
        /// </summary>
        public int? ProgressPercentage { get; set; }

        /// <summary>
        /// Time when the operation was created
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Time when the operation was started
        /// </summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>
        /// Time when the operation was completed
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Time when the operation was last updated
        /// </summary>
        public DateTime LastUpdatedAt { get; set; }

        /// <summary>
        /// Whether the operation completed successfully
        /// </summary>
        public bool? Success { get; set; }

        /// <summary>
        /// Detailed result data
        /// </summary>
        [JsonIgnore]
        public object? DetailedResult { get; set; }

        /// <summary>
        /// Serialized detailed result data
        /// </summary>
        public string? DetailedResultJson { get; set; }

        /// <summary>
        /// Whether the operation can be cancelled
        /// </summary>
        public bool CanBeCancelled { get; set; }

        /// <summary>
        /// Creates a new pool operation
        /// </summary>
        public PoolOperation()
        {
            Id = Guid.NewGuid();
            CreatedAt = DateTime.UtcNow;
            LastUpdatedAt = DateTime.UtcNow;
            Status = PoolOperationStatus.Pending;
        }

        /// <summary>
        /// Creates a new pool operation with the specified parameters
        /// </summary>
        public PoolOperation(Guid poolGroupGuid, PoolOperationType operationType, string description)
            : this()
        {
            PoolGroupGuid = poolGroupGuid;
            OperationType = operationType;
            Description = description;
        }
    }
}