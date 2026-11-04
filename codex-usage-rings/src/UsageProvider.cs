using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CodexUsageRings
{
    /// <summary>Reads account quota windows without reading or retaining account credentials.</summary>
    public sealed class UsageProvider
    {
        private const int RpcTimeoutMilliseconds = 14000;
        private const int MaximumJsonCharacters = 1048576;
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private readonly bool allowSessionLogFallback;
        private readonly Func<UsageSnapshot> liveReader;
        private readonly Func<UsageSnapshot> logReader;
        private readonly object fetchGate = new object();
        private readonly object processGate = new object();
        private Process activeChild;
        private RpcInbox activeInbox;
        private bool canceled;

        public UsageProvider() : this(false) { }

        public UsageProvider(bool allowSessionLogFallback) : this(allowSessionLogFallback, null, null) { }

        internal UsageProvider(bool allowSessionLogFallback, Func<UsageSnapshot> liveReader, Func<UsageSnapshot> logReader)
        {
            this.allowSessionLogFallback = allowSessionLogFallback;
            this.liveReader = liveReader ?? FetchRpc;
            this.logReader = logReader ?? FetchRecentLog;
        }

        public UsageSnapshot Fetch()
        {
            lock (fetchGate)
            {
                if (IsCanceled) return CanceledSnapshot();
                UsageSnapshot live = liveReader();
                if (IsCanceled) return CanceledSnapshot();
                // A successful response is authoritative even when it reports no quota windows.
                if (live.Source == "app-server" || !allowSessionLogFallback) return live;
                UsageSnapshot fallback = logReader();
                if (IsCanceled) return CanceledSnapshot();
                return fallback.Week != null || fallback.FiveHour != null ? fallback : live;
            }
        }

        /// <summary>Permanently stops this provider and its own child; safe to repeat on UI exit.</summary>
        public void Cancel()
        {
            Process child;
            RpcInbox inbox;
            lock (processGate)
            {
                canceled = true;
                child = activeChild;
                inbox = activeInbox;
            }
            if (inbox != null) inbox.Stop();
            if (child != null)
            {
                try { if (!child.HasExited) child.Kill(); } catch (Exception) { }
                // The UI may exit immediately after Cancel returns. Observe termination here
                // instead of relying solely on a background Fetch finally block to finish.
                try { child.WaitForExit(500); } catch (Exception) { }
            }
        }

        private bool IsCanceled
        {
            get { lock (processGate) return canceled; }
        }

        private static UsageSnapshot CanceledSnapshot()
        {
            return Unavailable(DateTime.UtcNow, "额度读取已停止");
        }

        public static UsageSnapshot ParseRpc(string json, DateTime sampledAt)
        {
            DateTime sampledUtc = AsUtc(sampledAt);
            Dictionary<string, object> envelope = ReadObject(json);
            Dictionary<string, object> result = ObjectAt(envelope, "result");
            if (result == null || ValueAt(envelope, "error") != null)
                return Unavailable(sampledUtc, "无法读取当前额度");

            Dictionary<string, object> limits = ObjectAt(ObjectAt(result, "rateLimitsByLimitId"), "codex");
            if (limits == null)
            {
                Dictionary<string, object> legacy = ObjectAt(result, "rateLimits");
                if (String.Equals(ValueAt(legacy, "limitId") as string, "codex", StringComparison.Ordinal)) limits = legacy;
            }
            if (limits == null)
            {
                UsageSnapshot empty = Unavailable(sampledUtc, "未返回 Codex 额度");
                empty.Source = "app-server";
                return empty;
            }
            return ReadWindows(limits, sampledUtc, "app-server", false);
        }

        public static UsageSnapshot ParseLogLine(string line)
        {
            Dictionary<string, object> envelope = ReadObject(line);
            if (!String.Equals(ValueAt(envelope, "type") as string, "event_msg", StringComparison.Ordinal))
                return Unavailable(DateTime.MinValue, "无可用额度记录");
            Dictionary<string, object> payload = ObjectAt(envelope, "payload");
            if (!String.Equals(ValueAt(payload, "type") as string, "token_count", StringComparison.Ordinal))
                return Unavailable(DateTime.MinValue, "无可用额度记录");
            DateTime timestamp;
            if (!DateTime.TryParse(ValueAt(envelope, "timestamp") as string, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp))
                return Unavailable(DateTime.MinValue, "额度记录缺少时间");

            Dictionary<string, object> limits = ObjectAt(payload, "rate_limits");
            if (limits == null) return Unavailable(timestamp, "记录未包含额度");
            object limitId = ValueAt(limits, "limit_id");
            // Older token_count records omit limit_id. An explicit different limit is never used.
            if (limitId != null && !String.Equals(limitId as string, "codex", StringComparison.Ordinal))
                return Unavailable(timestamp, "记录不属于 Codex 额度");
            return ReadWindows(limits, timestamp, "session-log", true);
        }

        private static UsageSnapshot ReadWindows(Dictionary<string, object> limits, DateTime captured,
            string source, bool snakeCase)
        {
            UsageSnapshot result = new UsageSnapshot();
            result.CapturedAtUtc = captured;
            result.Source = source;
            foreach (string key in new string[] { "primary", "secondary" })
            {
                QuotaWindow window = ReadWindow(ObjectAt(limits, key), snakeCase);
                if (window == null) continue;
                if (window.DurationMinutes == 10080 && result.Week == null) result.Week = window;
                if (window.DurationMinutes == 300 && result.FiveHour == null) result.FiveHour = window;
            }
            if (result.Week == null && result.FiveHour == null)
            {
                if (snakeCase) result.Source = "unavailable";
                result.Status = "未返回有效的 7d / 5h 额度";
            }
            else if (snakeCase) result.Status = "会话日志数据，可能滞后";
            else result.Status = result.Week == null || result.FiveHour == null ? "已读取额度；部分窗口未返回" : "已读取当前额度";
            return result;
        }

        private static QuotaWindow ReadWindow(Dictionary<string, object> data, bool snakeCase)
        {
            double used;
            double duration;
            if (!TryNumber(ValueAt(data, snakeCase ? "used_percent" : "usedPercent"), out used) || used < 0 || used > 100)
                return null;
            if (!TryNumber(ValueAt(data, snakeCase ? "window_minutes" : "windowDurationMins"), out duration) ||
                (duration != 10080 && duration != 300)) return null;
            QuotaWindow window = new QuotaWindow();
            window.UsedPercent = used;
            window.DurationMinutes = (int)duration;
            double reset;
            if (TryNumber(ValueAt(data, snakeCase ? "resets_at" : "resetsAt"), out reset) && reset >= 0 && reset <= 253402300799)
            {
                try { window.ResetsAtUtc = UnixEpoch.AddSeconds(reset); }
                catch (ArgumentOutOfRangeException) { }
            }
            return window;
        }

        private static Dictionary<string, object> ReadObject(string json)
        {
            if (String.IsNullOrWhiteSpace(json) || json.Length > MaximumJsonCharacters) return null;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = MaximumJsonCharacters;
                serializer.RecursionLimit = 64;
                return serializer.DeserializeObject(json) as Dictionary<string, object>;
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (OverflowException) { return null; }
        }

        private static object ValueAt(Dictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? value : null;
        }

        private static Dictionary<string, object> ObjectAt(Dictionary<string, object> data, string key)
        {
            return ValueAt(data, key) as Dictionary<string, object>;
        }

        private static bool TryNumber(object value, out double number)
        {
            number = 0;
            if (!(value is byte || value is sbyte || value is short || value is ushort || value is int ||
                value is uint || value is long || value is ulong || value is float || value is double || value is decimal)) return false;
            try { number = Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch (Exception) { return false; }
            return !Double.IsNaN(number) && !Double.IsInfinity(number);
        }

        private static DateTime AsUtc(DateTime timestamp)
        {
            if (timestamp.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
            return timestamp.ToUniversalTime();
        }

        private static UsageSnapshot Unavailable(DateTime captured, string status)
        {
            UsageSnapshot result = new UsageSnapshot();
            result.CapturedAtUtc = captured;
            result.Source = "unavailable";
            result.Status = status;
            return result;
        }

        private UsageSnapshot FetchRpc()
        {
            DateTime sampled = DateTime.UtcNow;
            string executable = FindBundledCodex();
            if (executable == null) return Unavailable(sampled, "未找到 Codex 内置程序");
            Process process = null;
            RpcInbox inbox = new RpcInbox();
            Stopwatch timer = Stopwatch.StartNew();
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(executable,
                    "app-server --stdio -c analytics.enabled=false");
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardInput = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.StandardOutputEncoding = Encoding.UTF8;
                info.StandardErrorEncoding = Encoding.UTF8;
                info.WorkingDirectory = Path.GetDirectoryName(executable);
                process = new Process();
                process.StartInfo = info;
                process.OutputDataReceived += inbox.OnOutput;
                // Drain stderr without storing diagnostic text, account details, or secrets.
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args) { };
                lock (processGate)
                {
                    if (canceled) return CanceledSnapshot();
                    if (!process.Start()) return Unavailable(sampled, "无法启动额度读取程序");
                    activeChild = process;
                    activeInbox = inbox;
                }
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.StandardInput.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"codex-usage-rings\",\"version\":\"0.1.0\"}}}");
                process.StandardInput.Flush();
                string initialized = inbox.WaitFor(1, timer);
                Dictionary<string, object> initializeReply = ReadObject(initialized);
                if (initializeReply == null || ValueAt(initializeReply, "error") != null || ObjectAt(initializeReply, "result") == null)
                    return Unavailable(sampled, "额度连接未就绪或读取超时");
                process.StandardInput.WriteLine("{\"method\":\"initialized\"}");
                process.StandardInput.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":{\"excludeResetCreditDetails\":true,\"supportsLunaReserve\":false}}");
                process.StandardInput.Flush();
                string response = inbox.WaitFor(2, timer);
                return response == null ? Unavailable(sampled, "额度读取超时或连接已关闭") : ParseRpc(response, DateTime.UtcNow);
            }
            catch (Exception)
            {
                // Deliberately do not expose raw process or RPC errors in UI/status fields.
                return Unavailable(sampled, "暂时无法读取额度");
            }
            finally
            {
                if (process != null)
                {
                    // Keep this child registered through termination: concurrent Cancel must
                    // either capture it here or observe that cleanup already finished.
                    try { if (!process.HasExited) process.Kill(); } catch (Exception) { }
                    try { process.WaitForExit(250); } catch (Exception) { }
                }
                lock (processGate)
                {
                    if (Object.ReferenceEquals(activeChild, process))
                    {
                        activeChild = null;
                        activeInbox = null;
                    }
                }
                if (process != null)
                {
                    try { process.Dispose(); } catch (Exception) { }
                }
                inbox.Dispose();
            }
        }

        private static string FindBundledCodex()
        {
            try
            {
                string root = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin"));
                if (!Directory.Exists(root)) return null;
                string selected = null;
                DateTime newest = DateTime.MinValue;
                foreach (string candidate in Directory.GetFiles(root, "codex.exe", SearchOption.AllDirectories))
                {
                    string full = Path.GetFullPath(candidate);
                    if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                    FileInfo file = new FileInfo(full);
                    if (!file.Exists || file.Length < 2) continue;
                    // Verify the discovered candidate is an executable file before launching it.
                    using (FileStream stream = File.Open(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z') continue;
                    if (selected == null || file.LastWriteTimeUtc > newest)
                    {
                        selected = full;
                        newest = file.LastWriteTimeUtc;
                    }
                }
                return selected;
            }
            catch (Exception) { return null; }
        }

        private UsageSnapshot FetchRecentLog()
        {
            DateTime now = DateTime.UtcNow;
            UsageSnapshot latest = Unavailable(now, "无近期额度记录");
            try
            {
                string home = Environment.GetEnvironmentVariable("CODEX_HOME");
                if (String.IsNullOrWhiteSpace(home))
                    home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
                string sessions = Path.Combine(home, "sessions");
                HashSet<string> folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DateTime day in new DateTime[] { now.Date, now.Date.AddDays(-1), DateTime.Now.Date, DateTime.Now.Date.AddDays(-1) })
                    folders.Add(Path.Combine(sessions, day.ToString("yyyy", CultureInfo.InvariantCulture), day.ToString("MM", CultureInfo.InvariantCulture), day.ToString("dd", CultureInfo.InvariantCulture)));
                List<string> candidates = new List<string>();
                foreach (string folder in folders)
                    if (Directory.Exists(folder)) candidates.AddRange(Directory.GetFiles(folder, "*.jsonl", SearchOption.TopDirectoryOnly));
                // Session names contain their start time. mtime is never used as quota evidence.
                candidates.Sort(StringComparer.Ordinal);
                candidates.Reverse();
                Stopwatch scan = Stopwatch.StartNew();
                long bytesRead = 0;
                int filesRead = 0;
                DateTime earliest = now.Date.AddDays(-1);
                foreach (string file in candidates)
                {
                    if (IsCanceled || scan.ElapsedMilliseconds > 1500 || bytesRead >= 8388608 || filesRead >= 80) break;
                    filesRead++;
                    try
                    {
                        using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            long start = Math.Max(0, stream.Length - 524288);
                            stream.Seek(start, SeekOrigin.Begin);
                            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                            {
                                if (start > 0) reader.ReadLine(); // Discard a partial first JSON record.
                                string line;
                                while ((line = reader.ReadLine()) != null)
                                {
                                    bytesRead += Encoding.UTF8.GetByteCount(line) + 1;
                                    if (IsCanceled || scan.ElapsedMilliseconds > 1500 || bytesRead >= 8388608) break;
                                    UsageSnapshot candidate = ParseLogLine(line);
                                    if (candidate.Week == null && candidate.FiveHour == null) continue;
                                    if (candidate.CapturedAtUtc < earliest || candidate.CapturedAtUtc > now.AddMinutes(5)) continue;
                                    if (latest.Source == "unavailable" || candidate.CapturedAtUtc > latest.CapturedAtUtc) latest = candidate;
                                }
                            }
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (Exception) { }
            return latest;
        }

        private sealed class RpcInbox : IDisposable
        {
            private readonly object gate = new object();
            private readonly AutoResetEvent changed = new AutoResetEvent(false);
            private readonly Dictionary<int, string> replies = new Dictionary<int, string>();
            private bool closed;
            private bool disposed;

            public void OnOutput(object sender, DataReceivedEventArgs args)
            {
                Dictionary<string, object> envelope = ReadObject(args.Data);
                double id;
                bool hasId = TryNumber(ValueAt(envelope, "id"), out id) && (id == 1 || id == 2);
                lock (gate)
                {
                    if (disposed) return;
                    if (args.Data == null) closed = true;
                    if (hasId && !replies.ContainsKey((int)id)) replies[(int)id] = args.Data;
                    if (hasId || closed) changed.Set();
                }
            }

            public string WaitFor(int id, Stopwatch timer)
            {
                while (true)
                {
                    lock (gate)
                    {
                        string response;
                        if (replies.TryGetValue(id, out response)) return response;
                        if (closed || disposed) return null;
                    }
                    int remaining = RpcTimeoutMilliseconds - (int)timer.ElapsedMilliseconds;
                    if (remaining <= 0 || !changed.WaitOne(remaining)) return null;
                }
            }

            public void Stop()
            {
                lock (gate)
                {
                    if (disposed) return;
                    closed = true;
                    changed.Set();
                }
            }

            public void Dispose()
            {
                lock (gate)
                {
                    disposed = true;
                    changed.Dispose();
                }
            }
        }
    }
}
