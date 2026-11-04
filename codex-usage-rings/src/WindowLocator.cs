using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;

namespace CodexUsageRings
{
    public sealed class AnchorResult
    {
        public bool Found;
        public Rectangle Avatar;
        public Rectangle HostBounds;
        public IntPtr Hwnd;
        public float Scale;
        public string Reason;
    }

    public sealed class WindowLocator
    {
        private const int CallerWaitMilliseconds = 1500;
        private const int ScanBudgetMilliseconds = 1200;
        private const int MaximumNodes = 280;
        private const int MaximumDepth = 64;
        private static readonly object ScanLock = new object();
        private static ScanJob activeScan;

        private sealed class ScanJob
        {
            internal Thread Worker;
            internal AnchorResult Result;
            internal readonly Stopwatch Timer = Stopwatch.StartNew();
            internal volatile int Nodes;
            internal volatile int Depth;
            internal volatile int RegionNodes;
        }

        private sealed class Branch
        {
            internal AutomationElement Element;
            internal int Depth;
        }

        internal sealed class Candidate
        {
            internal Rectangle Bounds;
            internal string Name;
            internal string AutomationId;
            internal bool IsButton;
        }

        internal static Rectangle SelectCandidate(IList<Candidate> candidates, Rectangle host, float scale, out string reason)
        {
            reason = "No trusted account control";
            if (candidates == null || host.Width <= 0 || host.Height <= 0 || float.IsNaN(scale) || scale < 0.75f || scale > 5f)
                return Rectangle.Empty;

            List<PointF> centers = new List<PointF>();
            foreach (Candidate candidate in candidates)
            {
                if (candidate == null || !candidate.IsButton || !HasAccountSemantics(candidate.Name, candidate.AutomationId)) continue;
                Rectangle bounds = candidate.Bounds;
                if (bounds.Width < 16 * scale || bounds.Height < 16 * scale || bounds.Width > 48 * scale || bounds.Height > 48 * scale) continue;
                if (!host.Contains(bounds) || bounds.Width / (double)bounds.Height < 0.6 || bounds.Width / (double)bounds.Height > 1.67) continue;
                PointF center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
                float leftInset = center.X - host.Left;
                float bottomInset = host.Bottom - center.Y;
                if (leftInset < 8 * scale || leftInset > 48 * scale || bottomInset < 8 * scale || bottomInset > 168 * scale) continue;

                bool duplicate = false;
                foreach (PointF prior in centers)
                    if (Math.Abs(prior.X - center.X) <= 3 * scale && Math.Abs(prior.Y - center.Y) <= 3 * scale) duplicate = true;
                if (!duplicate) centers.Add(center);
            }

            if (centers.Count != 1)
            {
                if (centers.Count > 1) reason = "Account anchor is ambiguous";
                return Rectangle.Empty;
            }

            // UIA generally reports the padded hit target, not the painted avatar.
            // The renderer receives a 24-DIP avatar centered on that target.
            int diameter = (int)Math.Round(24 * scale);
            reason = "Unique semantic account control in the lower-left rail";
            return new Rectangle((int)Math.Round(centers[0].X - diameter / 2f),
                (int)Math.Round(centers[0].Y - diameter / 2f), diameter, diameter);
        }

        private static bool HasAccountSemantics(string name, string automationId)
        {
            string label = (name ?? "").Trim().ToLowerInvariant();
            string id = Regex.Replace((automationId ?? "").ToLowerInvariant(), "[-_]+", " ");
            if (Regex.IsMatch(label, @"\b(add|create|new|delete|remove)\s+(an?\s+)?account\b|\b(log|sign)\s*in\b") ||
                label.Contains("添加账户") || label.Contains("添加账号") || label.Contains("登录")) return false;
            bool labelMatch = Regex.IsMatch(label, @"\b(account|profile)\b|\buser\s+(menu|profile)\b") ||
                label.Contains("账户") || label.Contains("账号") || label.Contains("帐号") || label.Contains("个人资料") ||
                label.Contains("用户菜单") || label.Contains("アカウント") || label.Contains("プロフィール") ||
                label.Contains("계정") || label.Contains("프로필");
            bool idMatch = Regex.IsMatch(id, @"\b(account|profile)\b|\buser\s+menu\b") ||
                id.Contains("accountbutton") || id.Contains("accountmenubutton") || id.Contains("profilebutton") || id.Contains("usermenubutton");
            return labelMatch || idMatch;
        }

        public AnchorResult FindAnchor()
        {
            return FindAnchor(false);
        }

        // Background inspection is intended for the explicit read-only diagnostic.
        // The ordinary renderer uses the foreground-only overload above.
        public AnchorResult FindAnchor(bool allowBackground)
        {
            ScanJob job;
            lock (ScanLock)
            {
                // A stuck accessibility provider must not create one leaked thread
                // on every refresh. Only one outstanding UIA call chain is allowed.
                if (activeScan != null && activeScan.Worker.IsAlive)
                    return Missing("Accessibility scan is still busy; overlay hidden", activeScan);
                job = new ScanJob();
                activeScan = job;
                Thread worker = new Thread(delegate()
                {
                    IntPtr previousDpi = EnterPhysicalCoordinates();
                    try { job.Result = ScanWindow(job, allowBackground); }
                    catch (Exception ex) { job.Result = Missing("Accessibility provider unavailable (" + ex.GetType().Name + "); overlay hidden", job); }
                    finally { LeavePhysicalCoordinates(previousDpi); }
                });
                worker.IsBackground = true;
                worker.Name = "Codex usage account anchor";
                worker.SetApartmentState(ApartmentState.MTA);
                job.Worker = worker;
                worker.Start();
            }
            if (!job.Worker.Join(CallerWaitMilliseconds))
                return Missing("Accessibility scan timed out; overlay hidden", job);
            return job.Result ?? Missing("Accessibility scan returned no anchor", job);
        }

        private static AnchorResult ScanWindow(ScanJob job, bool allowBackground)
        {
            string selectionReason;
            IntPtr hwnd = FindHostWindow(allowBackground, out selectionReason);
            if (hwnd == IntPtr.Zero) return Missing(selectionReason, job);
            Rectangle host = GetHostBounds(hwnd);
            float scale = GetScale(hwnd);
            if (host.Width < 180 * scale || host.Height < 240 * scale) return Missing("Codex client area is unavailable or too small");

            Stopwatch timer = job.Timer;
            AutomationElement root = AutomationElement.FromHandle(hwnd);
            if (root == null) return Missing("Codex accessibility root is unavailable");
            CacheRequest cache = new CacheRequest();
            cache.TreeScope = TreeScope.Element;
            cache.Add(AutomationElement.NameProperty);
            cache.Add(AutomationElement.AutomationIdProperty);
            cache.Add(AutomationElement.BoundingRectangleProperty);
            cache.Add(AutomationElement.ControlTypeProperty);
            cache.Add(AutomationElement.IsOffscreenProperty);
            TreeWalker walker = TreeWalker.ControlViewWalker;
            Rectangle search = new Rectangle(host.Left, Math.Max(host.Top, host.Bottom - (int)Math.Ceiling(200 * scale)),
                Math.Min(host.Width, (int)Math.Ceiling(72 * scale)), Math.Min(host.Height, (int)Math.Ceiling(200 * scale)));
            List<Candidate> candidates = new List<Candidate>();
            Stack<Branch> pending = new Stack<Branch>();
            pending.Push(new Branch { Element = root, Depth = 0 });
            while (pending.Count != 0)
            {
                if (timer.ElapsedMilliseconds > ScanBudgetMilliseconds) return Missing("Accessibility time budget exceeded; overlay hidden", job);
                Branch branch = pending.Pop();
                AutomationElement child = walker.GetFirstChild(branch.Element, cache);
                while (child != null)
                {
                    job.Nodes++;
                    job.Depth = Math.Max(job.Depth, branch.Depth + 1);
                    if (job.Nodes > MaximumNodes) return Missing("Accessibility node budget exceeded; overlay hidden", job);
                    if (timer.ElapsedMilliseconds > ScanBudgetMilliseconds)
                        return Missing("Accessibility time budget exceeded; overlay hidden", job);
                    AutomationElement.AutomationElementInformation info = child.Cached;
                    Rectangle bounds = ToRectangle(info.BoundingRectangle);
                    bool intersects = !bounds.IsEmpty && bounds.IntersectsWith(search);
                    bool visible = !info.IsOffscreen;
                    if (visible && intersects)
                    {
                        job.RegionNodes++;
                        candidates.Add(new Candidate
                        {
                            Bounds = bounds,
                            Name = info.Name,
                            AutomationId = info.AutomationId,
                            IsButton = info.ControlType == ControlType.Button || info.ControlType == ControlType.MenuItem
                        });
                    }
                    // Invisible and non-overlapping branches are pruned. Unknown
                    // container bounds are tolerated only near the window root.
                    bool descend = visible && (intersects || (bounds.IsEmpty && branch.Depth < 3));
                    if (descend && branch.Depth + 1 < MaximumDepth)
                        pending.Push(new Branch { Element = child, Depth = branch.Depth + 1 });
                    else if (descend && branch.Depth + 1 >= MaximumDepth)
                    {
                        // A leaf at the boundary cannot conceal another anchor.
                        // Previously it failed solely because of its depth.
                        if (walker.GetFirstChild(child, cache) != null)
                            return Missing("Accessibility depth budget exceeded by a non-leaf region node; overlay hidden", job);
                    }
                    child = walker.GetNextSibling(child, cache);
                }
            }

            string reason;
            Rectangle avatar = SelectCandidate(candidates, host, scale, out reason);
            if (avatar.IsEmpty) return Missing(reason, job);
            if ((!allowBackground && !IsForeground(hwnd)) || GetHostBounds(hwnd) != host || Math.Abs(GetScale(hwnd) - scale) > 0.01f)
                return Missing("Codex window moved during anchor discovery; retrying", job);
            return new AnchorResult { Found = true, Avatar = avatar, HostBounds = host, Hwnd = hwnd, Scale = scale, Reason = WithDiagnostics(reason, job) };
        }

        private static IntPtr FindHostWindow(bool allowBackground, out string reason)
        {
            IntPtr foreground = Native.GetAncestor(Native.GetForegroundWindow(), 2);
            if (foreground != IntPtr.Zero && IsCodexDesktopWindow(foreground))
            {
                reason = "Foreground Codex desktop";
                return foreground;
            }
            if (!allowBackground)
            {
                reason = "Codex desktop is not the foreground window";
                return IntPtr.Zero;
            }

            HashSet<IntPtr> windows = new HashSet<IntPtr>();
            foreach (Process process in Process.GetProcessesByName("ChatGPT"))
            {
                try
                {
                    IntPtr hwnd = process.MainWindowHandle;
                    if (hwnd == IntPtr.Zero) continue;
                    hwnd = Native.GetAncestor(hwnd, 2);
                    if (hwnd != IntPtr.Zero && Native.IsWindowVisible(hwnd) && !Native.IsIconic(hwnd) && IsCodexDesktopWindow(hwnd))
                        windows.Add(hwnd);
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                finally { process.Dispose(); }
            }
            if (windows.Count == 1)
            {
                foreach (IntPtr hwnd in windows)
                {
                    reason = "Unique visible Codex desktop selected for background diagnostic";
                    return hwnd;
                }
            }
            reason = windows.Count == 0 ? "No visible non-minimized Codex desktop window" :
                "Background Codex desktop selection is ambiguous; visibleMainWindows=" + windows.Count;
            return IntPtr.Zero;
        }

        private static string WithDiagnostics(string reason, ScanJob job)
        {
            return reason + "; nodes=" + job.Nodes + ", depth=" + job.Depth +
                ", regionNodes=" + job.RegionNodes + ", elapsedMs=" + job.Timer.ElapsedMilliseconds;
        }

        private static AnchorResult Missing(string reason, ScanJob job)
        {
            return Missing(WithDiagnostics(reason, job));
        }

        private static AnchorResult Missing(string reason)
        {
            return new AnchorResult { Found = false, Scale = 1f, Reason = reason };
        }

        private static Rectangle ToRectangle(System.Windows.Rect bounds)
        {
            if (bounds.IsEmpty || double.IsInfinity(bounds.X) || double.IsInfinity(bounds.Y) || double.IsNaN(bounds.X) ||
                double.IsNaN(bounds.Y) || double.IsNaN(bounds.Width) || double.IsNaN(bounds.Height) ||
                Math.Abs(bounds.X) > 1000000 || Math.Abs(bounds.Y) > 1000000 ||
                bounds.Width <= 0 || bounds.Height <= 0 || bounds.Width > 100000 || bounds.Height > 100000)
                return Rectangle.Empty;
            return Rectangle.FromLTRB((int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
                (int)Math.Round(bounds.Right), (int)Math.Round(bounds.Bottom));
        }

        public static bool IsForeground(IntPtr hwnd)
        {
            return hwnd != IntPtr.Zero && Native.IsWindowVisible(hwnd) && !Native.IsIconic(hwnd) &&
                Native.GetAncestor(Native.GetForegroundWindow(), 2) == hwnd;
        }

        // These cheap helpers allow the renderer to translate a cached anchor while
        // a new semantic UIA scan runs. A changed scale requires a fresh anchor.
        public static Rectangle GetHostBounds(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return Rectangle.Empty;
            IntPtr oldDpi = EnterPhysicalCoordinates();
            try
            {
                Native.RECT rect;
                if (!Native.GetClientRect(hwnd, out rect)) return Rectangle.Empty;
                Native.POINT origin = new Native.POINT();
                if (!Native.ClientToScreen(hwnd, ref origin)) return Rectangle.Empty;
                return new Rectangle(origin.X, origin.Y, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
            }
            finally { LeavePhysicalCoordinates(oldDpi); }
        }

        public static bool IsMoving(IntPtr hwnd) {
            if(hwnd==IntPtr.Zero)return false;
            uint pid;uint thread=Native.GetWindowThreadProcessId(hwnd,out pid);
            Native.GUITHREADINFO info=new Native.GUITHREADINFO();info.cbSize=Marshal.SizeOf(info);
            return Native.GetGUIThreadInfo(thread,ref info) && (info.flags & 2)!=0 && info.hwndMoveSize==hwnd;
        }
        public static float GetScale(IntPtr hwnd)
        {
            try
            {
                uint dpi = Native.GetDpiForWindow(hwnd);
                return dpi >= 72 && dpi <= 480 ? dpi / 96f : 1f;
            }
            catch (EntryPointNotFoundException) { return 1f; }
        }

        private static IntPtr EnterPhysicalCoordinates()
        {
            try { return Native.SetThreadDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { return IntPtr.Zero; }
        }

        private static void LeavePhysicalCoordinates(IntPtr previous)
        {
            if (previous == IntPtr.Zero) return;
            try { Native.SetThreadDpiAwarenessContext(previous); }
            catch (EntryPointNotFoundException) { }
        }

        private static bool IsCodexDesktopWindow(IntPtr hwnd)
        {
            uint processId;
            Native.GetWindowThreadProcessId(hwnd, out processId);
            if (processId == 0) return false;
            IntPtr process = Native.OpenProcess(0x1000, false, processId);
            if (process == IntPtr.Zero) return false;
            try
            {
                StringBuilder path = new StringBuilder(32768);
                int length = path.Capacity;
                if (!Native.QueryFullProcessImageName(process, 0, path, ref length)) return false;
                string fullPath = path.ToString();
                return fullPath.IndexOf(@"\WindowsApps\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    fullPath.EndsWith(@"\app\ChatGPT.exe", StringComparison.OrdinalIgnoreCase);
            }
            finally { Native.CloseHandle(process); }
        }

        private static class Native
        {
            [StructLayout(LayoutKind.Sequential)] internal struct GUITHREADINFO {
                internal int cbSize; internal uint flags; internal IntPtr hwndActive,hwndFocus,hwndCapture,hwndMenuOwner,hwndMoveSize,hwndCaret;internal RECT rcCaret;
            }
            [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread,ref GUITHREADINFO info);
            [StructLayout(LayoutKind.Sequential)] internal struct RECT { internal int Left, Top, Right, Bottom; }
            [StructLayout(LayoutKind.Sequential)] internal struct POINT { internal int X, Y; }
            [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
            [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
            [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(IntPtr hwnd);
            [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsIconic(IntPtr hwnd);
            [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
            [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
            [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
            [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);
            [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(IntPtr handle);
        }
    }
}
