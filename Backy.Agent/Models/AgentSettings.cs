namespace Backy.Agent.Models
{
    public class AgentSettings
    {
        public int ApiPort { get; set; } = 5151;
        public string ApiKey { get; set; } = string.Empty;
        public List<string> ExcludedDrives { get; set; } = new List<string>();
        public bool DisableApiAuthentication { get; set; } = false;
        public int FileCacheTimeToLiveSeconds { get; set; } = 5;
    }
}