using System;
using System.Drawing;
using CodexUsageRings;

public static class RenderingTests
{
    static int count;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
    public static int Run()
    {
        count = 0;
        DateTime now = new DateTime(2026, 11, 4, 7, 0, 0, DateTimeKind.Utc);
        UsageSnapshot snapshot = new UsageSnapshot { CapturedAtUtc = now, Source = "app-server" };
        QuotaWindow quota = new QuotaWindow { UsedPercent = 84, DurationMinutes = 10080, ResetsAtUtc = now.AddHours(1) };
        RingVisual v = RingVisual.From(snapshot, quota, now);
        Check(v.Known && v.Remaining == 16 && v.Color == RingVisual.Green, "16 percent remaining must be green");
        quota.UsedPercent = 85;
        v = RingVisual.From(snapshot, quota, now);
        Check(v.Known && v.Remaining == 15 && v.Color == RingVisual.Red, "15 percent boundary must be red");
        quota.UsedPercent = 100;
        Check(RingVisual.From(snapshot, quota, now).SweepAngle == 0, "fully used quota has no remaining arc");
        quota.UsedPercent = 0;
        Check(RingVisual.From(snapshot, quota, now).SweepAngle == 360, "unused quota has full remaining ring");
        Check(!RingVisual.From(snapshot, null, now).Known, "missing five-hour quota must be unknown");
        Check(!RingVisual.From(snapshot, quota, now.AddMinutes(4)).Known, "stale data cannot look current");
        quota.ResetsAtUtc = now.AddSeconds(-1);
        Check(!RingVisual.From(snapshot, quota, now).Known, "reset expiry cannot imply zero use");
        quota.ResetsAtUtc = now.AddHours(1);
        quota.UsedPercent = double.NaN;
        Check(!RingVisual.From(snapshot, quota, now).Known, "invalid quota cannot be drawn");
        quota.UsedPercent = 101;
        Check(!RingVisual.From(snapshot, quota, now).Known, "out of range quota cannot be drawn");
        foreach (double scale in new double[] { 1, 1.25, 1.5, 2 })
        {
            Rectangle avatar = new Rectangle(-1500, 800, (int)(24 * scale), (int)(24 * scale));
            RingLayout layout = RingLayout.FromAvatar(avatar);
            double center = avatar.Left + avatar.Width / 2.0;
            Check(Math.Abs(layout.Bounds.Left + layout.Week.Left + layout.Week.Width / 2 - center) <= 1, "week horizontal center at DPI " + scale);
            Check(Math.Abs(layout.Week.Left - layout.FiveHour.Left) < .01, "both rings align");
            Check(Math.Abs(layout.FiveHour.Top - layout.Week.Top - 40 * scale) <= 1, "scaled spacing at DPI " + scale);
        }
        return count;
    }
}
