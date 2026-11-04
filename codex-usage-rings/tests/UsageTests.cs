using System;
using System.Threading;
using System.Threading.Tasks;
using CodexUsageRings;

public static class UsageTests
{
    public static int Run()
    {
        int passed = 0;
        DateTime sampled = new DateTime(2026, 11, 4, 8, 0, 0, DateTimeKind.Utc);
        UsageSnapshot snapshot = UsageProvider.ParseRpc(Rpc(Window(87, 10080), Window(29, 300)), sampled);
        Check(snapshot.Week != null && snapshot.FiveHour != null, "recognized durations should produce both quota windows");
        Equal(87, snapshot.Week.UsedPercent, "primary week maps by duration");
        Equal(29, snapshot.FiveHour.UsedPercent, "secondary five-hour maps by duration");
        Check(snapshot.Source == "app-server" && snapshot.CapturedAtUtc == sampled, "RPC source and capture time");
        passed++;

        snapshot = UsageProvider.ParseRpc(Rpc(Window(42, 300), Window(63, 10080)), sampled);
        Equal(42, snapshot.FiveHour.UsedPercent, "primary five-hour maps by duration");
        Equal(63, snapshot.Week.UsedPercent, "secondary week maps by duration");
        passed++;

        snapshot = UsageProvider.ParseRpc(Rpc(Window(87, 10080), "null"), sampled);
        Check(snapshot.Week != null && snapshot.FiveHour == null, "missing five-hour stays unavailable");
        passed++;

        snapshot = UsageProvider.ParseRpc(Rpc("null", "null"), sampled);
        Check(snapshot.Week == null && snapshot.FiveHour == null, "null windows stay unavailable");
        Check(!String.IsNullOrWhiteSpace(snapshot.Status), "unavailable windows have a status");
        Check(snapshot.Source == "app-server", "successful empty RPC remains authoritative");
        passed++;

        snapshot = UsageProvider.ParseRpc("{\"id\":2,\"result\":{\"rateLimitsByLimitId\":{}}}", sampled);
        Check(snapshot.Source == "app-server" && snapshot.Week == null && snapshot.FiveHour == null,
            "successful RPC without Codex bucket remains authoritative");
        passed++;

        string[] invalid = { "null", "-1", "101", "\"NaN\"", "\"Infinity\"", "\"75\"", "true", "{}" };
        foreach (string value in invalid)
        {
            snapshot = UsageProvider.ParseRpc(Rpc("{\"usedPercent\":" + value + ",\"windowDurationMins\":10080}", Window(25, 300)), sampled);
            Check(snapshot.Week == null, "invalid percent must not invent a value: " + value);
            Check(snapshot.FiveHour != null, "one bad window preserves other window: " + value);
            passed++;
        }

        snapshot = UsageProvider.ParseRpc(Rpc("{\"windowDurationMins\":10080}", "null"), sampled);
        Check(snapshot.Week == null, "missing percent must not become zero");
        passed++;

        snapshot = UsageProvider.ParseRpc(Rpc(Window(0, 10080), Window(100, 300)), sampled);
        Equal(0, snapshot.Week.UsedPercent, "real zero is accepted");
        Equal(100, snapshot.FiveHour.UsedPercent, "real hundred is accepted");
        passed++;

        snapshot = UsageProvider.ParseRpc(Rpc("{\"usedPercent\":42,\"windowDurationMins\":300.5}", Window(50, 60)), sampled);
        Check(snapshot.Week == null && snapshot.FiveHour == null, "unknown and fractional durations are not mapped");
        passed++;

        snapshot = UsageProvider.ParseRpc("{\"result\":{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":" + Window(87, 10080) + "}},\"rateLimits\":{\"limitId\":\"codex\",\"primary\":" + Window(3, 10080) + "}}}", sampled);
        Equal(87, snapshot.Week.UsedPercent, "codex keyed limit takes precedence");
        passed++;

        snapshot = UsageProvider.ParseRpc("{\"result\":{\"rateLimits\":{\"limitId\":\"codex\",\"primary\":" + Window(50, 10080) + "}}}", sampled);
        Equal(50, snapshot.Week.UsedPercent, "explicit legacy codex accepted");
        passed++;

        foreach (string limit in new string[] { "\"other\"", "null" })
        {
            snapshot = UsageProvider.ParseRpc("{\"result\":{\"rateLimits\":{\"limitId\":" + limit + ",\"primary\":" + Window(50, 10080) + "}}}", sampled);
            Check(snapshot.Week == null && snapshot.FiveHour == null, "unidentified or other legacy limits excluded");
            passed++;
        }

        foreach (string malformed in new string[] { null, "", "{", "[]", "null", "{}", "{\"error\":{\"message\":\"no session\"}}", Rpc("{\"usedPercent\":1e999,\"windowDurationMins\":10080}", "null") })
        {
            snapshot = UsageProvider.ParseRpc(malformed, sampled);
            Check(snapshot != null && snapshot.Week == null && snapshot.FiveHour == null, "malformed/missing RPC has no invented data");
            Check(snapshot.Source == "unavailable", "missing payload is unavailable");
            passed++;
        }

        snapshot = UsageProvider.ParseRpc(Rpc("{\"usedPercent\":31.5,\"windowDurationMins\":10080,\"resetsAt\":1793779200}", "null"), sampled);
        Equal(31.5, snapshot.Week.UsedPercent, "fractional percent accepted");
        Check(snapshot.Week.ResetsAtUtc == new DateTime(2026, 11, 4, 8, 0, 0, DateTimeKind.Utc), "Unix reset time preserved");
        passed++;

        snapshot = UsageProvider.ParseLogLine(Log("2026-11-04T08:00:00Z", "{\"primary\":{\"used_percent\":87,\"window_minutes\":10080,\"resets_at\":1793779200},\"secondary\":null}"));
        Equal(87, snapshot.Week.UsedPercent, "session log snake-case mapping");
        Check(snapshot.CapturedAtUtc == sampled && snapshot.Source == "session-log", "event timestamp retained");
        Check(snapshot.FiveHour == null && snapshot.Status.Contains("滞后"), "log explicitly identifies possible lag");
        passed++;

        snapshot = UsageProvider.ParseLogLine(Log("2026-11-04T17:00:00+09:00", "{\"primary\":{\"used_percent\":12,\"window_minutes\":300},\"secondary\":{\"used_percent\":73,\"window_minutes\":10080}}"));
        Check(snapshot.CapturedAtUtc == sampled, "log offset timestamp normalized to UTC");
        Equal(12, snapshot.FiveHour.UsedPercent, "log primary five-hour mapped");
        Equal(73, snapshot.Week.UsedPercent, "log secondary week mapped");
        passed++;

        foreach (string line in new string[] { null, "{", "{}", Log("not a timestamp", "{}"), Log("2026-11-04T08:00:00Z", "null"), "{\"timestamp\":\"2026-11-04T08:00:00Z\",\"type\":\"other\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"primary\":{\"used_percent\":40,\"window_minutes\":300}}}}", Log("2026-11-04T08:00:00Z", "{\"limit_id\":\"other\",\"primary\":{\"used_percent\":40,\"window_minutes\":300}}") })
        {
            snapshot = UsageProvider.ParseLogLine(line);
            Check(snapshot != null && snapshot.Week == null && snapshot.FiveHour == null, "irrelevant/malformed log has no quota");
            passed++;
        }
        int fallbackReads = 0;
        UsageSnapshot stale = new UsageSnapshot();
        stale.Source = "session-log";
        stale.Week = new QuotaWindow { UsedPercent = 99, DurationMinutes = 10080 };
        Func<UsageSnapshot> readStale = delegate { fallbackReads++; return stale; };
        foreach (string emptyReply in new string[] { Rpc("null", "null"), "{\"result\":{\"rateLimitsByLimitId\":{}}}" })
        {
            UsageSnapshot authoritative = UsageProvider.ParseRpc(emptyReply, sampled);
            UsageProvider provider = new UsageProvider(true, delegate { return authoritative; }, readStale);
            snapshot = provider.Fetch();
            Check(Object.ReferenceEquals(snapshot, authoritative) && fallbackReads == 0,
                "authoritative empty result must not be replaced by historical quota");
            passed++;
        }

        UsageSnapshot failed = UsageProvider.ParseRpc("{\"error\":{\"code\":-1}}", sampled);
        UsageProvider defaultPolicy = new UsageProvider(false, delegate { return failed; }, readStale);
        snapshot = defaultPolicy.Fetch();
        Check(Object.ReferenceEquals(snapshot, failed) && fallbackReads == 0, "disabled fallback never consults account-unbound logs");
        passed++;

        UsageProvider optionalFallback = new UsageProvider(true, delegate { return failed; }, readStale);
        snapshot = optionalFallback.Fetch();
        Check(Object.ReferenceEquals(snapshot, stale) && fallbackReads == 1, "opt-in fallback is used after live RPC failure");
        passed++;

        int liveReads = 0;
        UsageProvider canceled = new UsageProvider(true, delegate { liveReads++; return failed; }, readStale);
        canceled.Cancel();
        canceled.Cancel();
        snapshot = canceled.Fetch();
        Check(liveReads == 0 && fallbackReads == 1 && snapshot.Source == "unavailable", "permanent cancellation suppresses all subsequent reads");
        passed++;

        using (ManualResetEvent entered = new ManualResetEvent(false))
        using (ManualResetEvent release = new ManualResetEvent(false))
        {
            int canceledFallbackReads = 0;
            UsageProvider inFlight = new UsageProvider(true,
                delegate { entered.Set(); release.WaitOne(2000); return failed; },
                delegate { canceledFallbackReads++; return stale; });
            Task<UsageSnapshot> pending = Task.Factory.StartNew(delegate { return inFlight.Fetch(); });
            try
            {
                Check(entered.WaitOne(2000), "in-flight cancellation fixture reached live reader");
                inFlight.Cancel();
                release.Set();
                Check(pending.Wait(2000), "canceled in-flight fetch completes");
                Check(pending.Result.Source == "unavailable" && canceledFallbackReads == 0,
                    "cancellation during live read suppresses stale fallback");
            }
            finally
            {
                release.Set();
                pending.Wait(2500);
            }
        }
        passed++;
        return passed;
    }

    private static string Window(double percent, int duration)
    {
        return "{\"usedPercent\":" + percent.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"windowDurationMins\":" + duration + "}";
    }

    private static string Rpc(string primary, string secondary)
    {
        return "{\"id\":2,\"result\":{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":" + primary + ",\"secondary\":" + secondary + "}}}}";
    }

    private static string Log(string timestamp, string limits)
    {
        return "{\"timestamp\":\"" + timestamp + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":" + limits + "}}";
    }

    private static void Equal(double expected, double actual, string message)
    {
        Check(Math.Abs(expected - actual) < 0.00001, message + ": expected " + expected + ", got " + actual);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("UsageTests failed: " + message);
    }
}
