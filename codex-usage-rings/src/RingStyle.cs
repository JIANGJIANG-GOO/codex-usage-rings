using System;
using System.Drawing;
namespace CodexUsageRings
{
    public sealed class RingVisual
    {
        public static readonly Color Green = Color.FromArgb(49, 196, 141);
        public static readonly Color Red = Color.FromArgb(255, 77, 88);
        public bool Known;
        public double Remaining;
        public float SweepAngle;
        public Color Color;
        public string Reason;
        public static RingVisual From(UsageSnapshot snapshot, QuotaWindow quota, DateTime now)
        {
            string reason = null;
            if (quota == null) reason = "账户未提供此周期额度";
            else if (double.IsNaN(quota.UsedPercent) || double.IsInfinity(quota.UsedPercent) || quota.UsedPercent < 0 || quota.UsedPercent > 100) reason = "额度数据无效";
            else if (snapshot == null || (now - snapshot.CapturedAtUtc).TotalSeconds > 180 || (snapshot.CapturedAtUtc - now).TotalSeconds > 30) reason = "数据已过期，等待更新";
            else if (quota.ResetsAtUtc.HasValue && now >= quota.ResetsAtUtc.Value) reason = "周期已到期，等待服务器更新";
            if (reason != null) return new RingVisual { Known = false, Color = Color.Gray, Reason = reason };
            double remaining = 100 - quota.UsedPercent;
            return new RingVisual { Known = true, Remaining = remaining, SweepAngle = (float)(remaining * 3.6), Color = remaining > 15 ? Green : Red, Reason = "" };
        }
    }
    public sealed class RingLayout
    {
        public Rectangle Bounds;
        public RectangleF Week;
        public RectangleF FiveHour;
        public float Scale;
        public static RingLayout FromAvatar(Rectangle avatar)
        {
            float scale = avatar.Width / 24f;
            float centerX = avatar.Left + avatar.Width / 2f;
            float centerY = avatar.Top + avatar.Height / 2f;
            return new RingLayout {
                Scale = scale,
                Bounds = new Rectangle((int)Math.Round(centerX - 18 * scale), (int)Math.Round(centerY - 98 * scale), (int)Math.Ceiling(36 * scale), (int)Math.Ceiling(76 * scale)),
                Week = new RectangleF(4 * scale, 4 * scale, 28 * scale, 28 * scale),
                FiveHour = new RectangleF(4 * scale, 44 * scale, 28 * scale, 28 * scale)
            };
        }
    }
}
