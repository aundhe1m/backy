using System;

namespace Backy.Models
{
    /// <summary>
    /// Configuration settings for the Backy Agent client.
    /// </summary>
    public class BackyAgentConfig
    {
        /// <summary>
        /// The base URL of the Backy Agent API.
        /// </summary>
        public string BaseUrl { get; set; } = "http://localhost:5151";
        
        /// <summary>
        /// The API key for authentication with the Backy Agent API.
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;
        
        /// <summary>
        /// Timeout in seconds for API requests.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 30;
        
        /// <summary>
        /// Maximum number of retry attempts for failed requests.
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;
        
        /// <summary>
        /// Circuit breaker failure threshold before opening the circuit.
        /// </summary>
        public int CircuitBreakerThreshold { get; set; } = 5;
        
        /// <summary>
        /// Circuit breaker duration in seconds for which the circuit will remain open.
        /// </summary>
        public int CircuitBreakerDurationSeconds { get; set; } = 30;
    }
}