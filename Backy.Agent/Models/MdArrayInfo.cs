using System.Text.Json.Serialization;

namespace Backy.Agent.Models;

public class MdArrayInfo
{
    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = string.Empty;
    
    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;
    
    [JsonPropertyName("state")]
    public string State { get; set; } = "unknown";
    
    [JsonPropertyName("devices")]
    public List<string> Devices { get; set; } = new();
    
    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }
    
    [JsonPropertyName("arraySize")]
    public long ArraySize { get; set; }
    
    [JsonPropertyName("totalDevices")]
    public int TotalDevices { get; set; }
    
    [JsonPropertyName("activeDevices")]
    public int ActiveDevices { get; set; }
    
    [JsonPropertyName("workingDevices")]
    public int WorkingDevices { get; set; }
    
    [JsonPropertyName("failedDevices")]
    public int FailedDevices { get; set; }
    
    [JsonPropertyName("spareDevices")]
    public int SpareDevices { get; set; }
    
    [JsonPropertyName("status")]
    public string[] Status { get; set; } = Array.Empty<string>();
    
    [JsonPropertyName("resyncInProgress")]
    public bool ResyncInProgress { get; set; }
    
    [JsonPropertyName("resyncPercentage")]
    public double? ResyncPercentage { get; set; }
    
    [JsonPropertyName("resyncTimeEstimate")]
    public double? ResyncTimeEstimate { get; set; }
    
    [JsonPropertyName("resyncSpeed")]
    public string ResyncSpeed { get; set; } = string.Empty;
}

public class MdStatInfo
{
    [JsonPropertyName("personalities")]
    public List<string> Personalities { get; set; } = new();
    
    [JsonPropertyName("arrays")]
    public Dictionary<string, MdArrayInfo> Arrays { get; set; } = new();
    
    [JsonPropertyName("unusedDevices")]
    public List<string> UnusedDevices { get; set; } = new();
}