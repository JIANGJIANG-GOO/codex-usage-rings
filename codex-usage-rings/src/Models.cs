using System;

namespace CodexUsageRings
{
    public sealed class QuotaWindow
    {
        public double UsedPercent;
        public int DurationMinutes;
        public DateTime? ResetsAtUtc;
    }

    public sealed class UsageSnapshot
    {
        public QuotaWindow Week;
        public QuotaWindow FiveHour;
        public DateTime CapturedAtUtc;
        public string Source = "unavailable";
        public string Status = "尚未读取额度";
    }
}
