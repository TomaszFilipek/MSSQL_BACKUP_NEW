namespace MssqlBackup.Web.Helpers;

public static class TimeHelper
{
    private static TimeZoneInfo? _warsawZone;

    public static TimeZoneInfo WarsawZone
    {
        get
        {
            if (_warsawZone != null) return _warsawZone;
            try
            {
                _warsawZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
            }
            catch
            {
                try
                {
                    _warsawZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
                }
                catch
                {
                    _warsawZone = TimeZoneInfo.CreateCustomTimeZone("CEST", TimeSpan.FromHours(2), "CEST", "CEST");
                }
            }
            return _warsawZone;
        }
    }

    public static DateTime ToWarsawTime(DateTime dt)
    {
        // Treat Unspecified as UTC (records stored as UTC from old code)
        if (dt.Kind == DateTimeKind.Unspecified)
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        if (dt.Kind == DateTimeKind.Utc)
            return TimeZoneInfo.ConvertTimeFromUtc(dt, WarsawZone);
        // Local already - convert via UTC if server TZ is not Warsaw
        return TimeZoneInfo.ConvertTime(dt, TimeZoneInfo.Local, WarsawZone);
    }

    public static DateTime TodayWarsaw => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, WarsawZone).Date;
}
