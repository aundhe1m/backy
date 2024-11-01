using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Backy.Services
{
    public interface ITimeZoneService
    {
        TimeZoneInfo GetConfiguredTimeZone();
        DateTime ConvertToConfiguredTimeZone(DateTime dateTime);
        DateTime ConvertToUtc(DateTime dateTime);
        DateTimeOffset ConvertToUtc(DateTimeOffset dateTime);
    }

    public class TimeZoneService : ITimeZoneService
    {
        private readonly TimeZoneInfo _configuredTimeZone;

        public TimeZoneService(IConfiguration configuration, ILogger<TimeZoneService> logger)
        {
            var timeZoneId = configuration["TimeZone"];
            if (string.IsNullOrEmpty(timeZoneId))
            {
                _configuredTimeZone = TimeZoneInfo.Local;
                logger.LogInformation($"Using system local time zone: {_configuredTimeZone.DisplayName}");
            }
            else
            {
                try
                {
                    _configuredTimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                    logger.LogInformation($"Using configured time zone: {_configuredTimeZone.DisplayName}");
                }
                catch (TimeZoneNotFoundException)
                {
                    logger.LogWarning($"Time zone '{timeZoneId}' not found. Falling back to system local time zone.");
                    _configuredTimeZone = TimeZoneInfo.Local;
                }
                catch (InvalidTimeZoneException)
                {
                    logger.LogWarning($"Time zone '{timeZoneId}' is invalid. Falling back to system local time zone.");
                    _configuredTimeZone = TimeZoneInfo.Local;
                }
            }
        }

        public TimeZoneInfo GetConfiguredTimeZone() => _configuredTimeZone;

        public DateTime ConvertToConfiguredTimeZone(DateTime dateTime)
        {
            return TimeZoneInfo.ConvertTime(dateTime, TimeZoneInfo.Utc, _configuredTimeZone);
        }

        public DateTime ConvertToUtc(DateTime dateTime)
        {
            return TimeZoneInfo.ConvertTimeToUtc(dateTime, _configuredTimeZone);
        }

        public DateTimeOffset ConvertToUtc(DateTimeOffset dateTimeOffset)
        {
            // Assume dateTimeOffset is in the configured time zone
            // Create a DateTimeOffset with the configured time zone's offset
            var sourceOffset = _configuredTimeZone.GetUtcOffset(dateTimeOffset);
            var sourceDateTimeOffset = new DateTimeOffset(dateTimeOffset.DateTime, sourceOffset);

            // Convert to UTC
            return sourceDateTimeOffset.ToUniversalTime();
        }
    }
}
