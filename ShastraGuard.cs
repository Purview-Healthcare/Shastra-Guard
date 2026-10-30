// Shastra Guard
// Runs quietly in the background from login.
//  - Windows + L: the first press brings Purview Shastra to the front with a reminder,
//    the second press (or "Lock now") locks.
//  - Ctrl + Alt + L: same reminder, then lock.
//  - At login and unlock: brings Purview Shastra to the front (opens it if closed).
//  - Shut down / restart / sign out: the first try is paused. Windows shows its own page with
//    the Purview Shastra reminder; after Cancel, Shastra and a reminder box are waiting.
//    Only "Shut down now" / "Restart now" in the box (or Windows' "Shut down anyway") goes ahead.
//    "Shut down anyway" is recorded in ShastraPunchIn-missed-punch-outs.txt.
//  - Every minute it re-arms its keyboard watch, and Windows restarts it if it ever stops.
//  - Whenever this program is not running, Windows + L locks normally.
//  - Checks GitHub for a newer version at start and every 6 hours, and updates itself quietly.
// Log: C:\Users\Public\Documents\ShastraPunchIn-log.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// The name Windows shows on its "preventing shut down" page
[assembly: System.Reflection.AssemblyTitle("Purview Shastra")]
[assembly: System.Reflection.AssemblyProduct("Purview Shastra")]
[assembly: System.Reflection.AssemblyCompany("Purview")]
[assembly: System.Reflection.AssemblyVersion("1.5.2.0")]

class ShastraGuard : Form
{
    // ---------- Windows API ----------
    delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte sc, uint f, UIntPtr e);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern bool LockWorkStation();
    // GetLastInputInfo reports only the TIME of the last mouse or keyboard activity,
    // never what was typed or clicked. It is used to measure how long the desk has been idle.
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool ShutdownBlockReasonCreate(IntPtr h, string r);
    [DllImport("user32.dll")] static extern bool ShutdownBlockReasonDestroy(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool SetProcessShutdownParameters(uint level, uint flags);

    const string LogPath = @"C:\Users\Public\Documents\ShastraPunchIn-log.txt";
    public const string CurrentVersion = "1.8.2";
    const string UpdateBase = "https://raw.githubusercontent.com/Purview-Healthcare/Shastra-Guard/main/";
    static bool updating = false;
    // ---- activity record (no-movement and screen-locked periods) ----
    const string ActivityCsv = @"C:\Users\Public\Documents\ShastraGuard-activity.csv";
    const int IdleMinutes = 3;                 // a no-movement period starts after this many minutes
    static DateTime idleStart = DateTime.MinValue;   // start of the current no-movement period, if any
    static DateTime lockStart = DateTime.MinValue;   // when the screen was locked, if it is

    // ---- send activity to Purview Shastra ----
    const string GuardUrl = "https://qhbgshnqugxqiwdywsas.supabase.co/rest/v1/rpc/guard_ping";
    const string GuardKey = "sb_publishable_2aOo5c5pq3J33nK8hVIb0w_gjj6A4n7"; // Shastra's public key (same one the Shastra website uses)
    const string TokenPath = @"C:\Users\Public\Documents\ShastraGuard-id.txt"; // this computer's token, shared by every user on it
    static string machineToken = null;

    static uint IdleSeconds()
    {
        LASTINPUTINFO lii = new LASTINPUTINFO();
        lii.cbSize = (uint)Marshal.SizeOf(lii);
        if (!GetLastInputInfo(ref lii)) return 0;
        return unchecked((uint)Environment.TickCount - lii.dwTime) / 1000;
    }

    static void WriteActivityRow(DateTime start, DateTime end, string type)
    {
        int mins = (int)Math.Round((end - start).TotalMinutes);
        if (mins < 1) return;
        try
        {
            if (!File.Exists(ActivityCsv))
                File.AppendAllText(ActivityCsv, "Date,Start,End,Minutes,Type,User\r\n");
            File.AppendAllText(ActivityCsv, string.Format("{0},{1},{2},{3},{4},{5}\r\n",
                start.ToString("yyyy-MM-dd"), start.ToString("HH:mm:ss"), end.ToString("HH:mm:ss"),
                mins, type, Environment.UserName));
        }
        catch { }
        // Also send it to Purview Shastra. "No movement" becomes idle, "Screen locked" becomes locked.
        string kind = type == "No movement" ? "idle" : (type == "Screen locked" ? "locked" : null);
        if (kind != null) GuardPing(kind, end, start, end, mins, null);
    }

    // ---------------- Send activity to Purview Shastra (guard_ping) ----------------
    // Guard reports its own computer to Shastra. Each computer has a random token kept in
    // ShastraGuard-id.txt; Shastra ties the first token it sees to this computer's name, so no
    // other computer can report as this one. These calls never block the app and never show an error.
    static string MachineToken()
    {
        if (machineToken != null) return machineToken;
        try
        {
            if (!File.Exists(TokenPath))
            {
                string t = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"); // 64 characters
                try { using (FileStream fs = new FileStream(TokenPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { byte[] b = Encoding.ASCII.GetBytes(t); fs.Write(b, 0, b.Length); } }
                catch { } // another user on this PC created it at the same moment: read theirs below
            }
            string got = File.ReadAllText(TokenPath).Trim();
            if (got.Length >= 20) machineToken = got;
        }
        catch { }
        return machineToken;
    }

    static string Iso(DateTime dt) { return dt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"); }

    static string ShortText(string s, int n) { if (s == null) return ""; s = s.Replace("\r", " ").Replace("\n", " "); return s.Length > n ? s.Substring(0, n) : s; }

    static string JsonStr(string s)
    {
        if (s == null) return "null";
        StringBuilder b = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c == '\n') b.Append("\\n");
            else if (c == '\r') b.Append("\\r");
            else if (c == '\t') b.Append("\\t");
            else if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        b.Append("\"");
        return b.ToString();
    }

    static string BuildPingJson(string tok, string kind, DateTime? at, DateTime? from, DateTime? to, int? minutes, string detail)
    {
        StringBuilder j = new StringBuilder();
        j.Append("{");
        j.Append("\"p_machine\":").Append(JsonStr(Environment.MachineName));
        j.Append(",\"p_user\":").Append(JsonStr(Environment.UserName));
        j.Append(",\"p_token\":").Append(JsonStr(tok));
        j.Append(",\"p_version\":").Append(JsonStr(CurrentVersion));
        j.Append(",\"p_kind\":").Append(JsonStr(kind));
        if (at.HasValue) j.Append(",\"p_at\":").Append(JsonStr(Iso(at.Value)));
        if (from.HasValue) j.Append(",\"p_from\":").Append(JsonStr(Iso(from.Value)));
        if (to.HasValue) j.Append(",\"p_to\":").Append(JsonStr(Iso(to.Value)));
        if (minutes.HasValue) j.Append(",\"p_minutes\":").Append(minutes.Value);
        if (!string.IsNullOrEmpty(detail)) j.Append(",\"p_detail\":").Append(JsonStr(detail));
        j.Append("}");
        return j.ToString();
    }

    static void PostPing(string json, int timeoutMs, string kind)
    {
        try
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            using (TimedWebClient wc = new TimedWebClient())
            {
                wc.TimeoutMs = timeoutMs;
                wc.Headers["apikey"] = GuardKey;
                wc.Headers["Authorization"] = "Bearer " + GuardKey;
                wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                string resp = wc.UploadString(GuardUrl, "POST", json);
                if (kind != "alive") Log("Sent to Shastra (" + kind + "): " + ShortText(resp, 160));
            }
        }
        catch (Exception e) { if (kind != "alive") Log("Could not send " + kind + " to Shastra: " + e.Message); }
    }

    // Fire and forget: used for start, alive, idle and locked.
    static void GuardPing(string kind, DateTime? at, DateTime? from, DateTime? to, int? minutes, string detail)
    {
        string tok = MachineToken();
        if (string.IsNullOrEmpty(tok)) return;
        string json = BuildPingJson(tok, kind, at, from, to, minutes, detail);
        ThreadPool.QueueUserWorkItem(delegate { PostPing(json, 8000, kind); });
    }

    // Waits for the send (up to 4 seconds): used at shut down, when the app is about to close.
    static void GuardPingNow(string kind, DateTime? at, DateTime? from, DateTime? to, int? minutes, string detail)
    {
        string tok = MachineToken();
        if (string.IsNullOrEmpty(tok)) return;
        PostPing(BuildPingJson(tok, kind, at, from, to, minutes, detail), 4000, kind);
    }

    // ---- link this computer to whoever is signed in to Shastra ----
    // When someone signs in, the Shastra page puts a short code in its window title, written with
    // invisible characters after the words "Purview Shastra". Guard reads it and sends it to Shastra,
    // which links this computer to that person. A different person signing in sends a new code.
    static string lastClaim = null;
    static DateTime lastClaimAt = DateTime.MinValue;
    static string ReadLinkCode()
    {
        string found = null;
        EnumWindows(delegate (IntPtr h, IntPtr l)
        {
            if (!IsWindowVisible(h)) return true;
            StringBuilder t = new StringBuilder(512); GetWindowText(h, t, 512);
            string title = t.ToString();
            if (!title.StartsWith("Purview Shastra", StringComparison.OrdinalIgnoreCase)) return true;
            int i = title.IndexOf('\u2063');
            if (i < 0) return true;
            StringBuilder bits = new StringBuilder();
            for (int k = i + 1; k < title.Length; k++)
            {
                if (title[k] == '\u200B') bits.Append('0');
                else if (title[k] == '\u200C') bits.Append('1');
                else break;
            }
            if (bits.Length != 32) return true;
            StringBuilder hex = new StringBuilder();
            for (int k = 0; k < 32; k += 4) hex.Append(Convert.ToInt32(bits.ToString(k, 4), 2).ToString("x"));
            found = hex.ToString();
            return false;   // first Shastra window with a code is enough
        }, IntPtr.Zero);
        return found;
    }

    static void CheckLinkCode()
    {
        try
        {
            string code = ReadLinkCode();
            if (code == null) return;
            if (code == lastClaim && (DateTime.Now - lastClaimAt).TotalMinutes < 30) return;
            lastClaim = code; lastClaimAt = DateTime.Now;
            GuardPing("claim", DateTime.Now, null, null, null, code);
        }
        catch { }
    }

    // Runs every 30 seconds: notices when the desk has had no movement for 3 minutes or more,
    // and records the period (date, start, end, minutes) once movement comes back.
    static void CheckActivity()
    {
        if (lockStart != DateTime.MinValue) return;   // the lock/unlock path records locked time
        uint idle = IdleSeconds();
        if (idle >= IdleMinutes * 60)
        {
            if (idleStart == DateTime.MinValue) idleStart = DateTime.Now.AddSeconds(-idle);
        }
        else if (idleStart != DateTime.MinValue)
        {
            DateTime end = DateTime.Now.AddSeconds(-idle);
            DateTime start = idleStart;
            WriteActivityRow(start, end, "No movement");
            idleStart = DateTime.MinValue;
            // Time spent on a break in Shastra is not idle: ask Shastra how much of this period was
            // outside a break, and only show the box for that part (nothing if it was all break).
            ThreadPool.QueueUserWorkItem(delegate {
                int net = NetIdleMinutes(start, end);
                if (net < 0) net = (int)Math.Round((end - start).TotalMinutes);   // Shastra not reachable: show it as before
                if (net < IdleMinutes) { Log("No movement " + start.ToString("HH:mm") + "-" + end.ToString("HH:mm") + " was during a break: no box shown"); return; }
                try { if (Instance != null) Instance.BeginInvoke((MethodInvoker)delegate { Instance.ShowAwayBox(start, end, net); }); } catch { }
            });
        }
    }

    // Minutes of a no-movement period that fell outside a Shastra break. -1 if Shastra could not be asked.
    const string BreakCheckUrl = "https://qhbgshnqugxqiwdywsas.supabase.co/rest/v1/rpc/guard_break_check";
    static int NetIdleMinutes(DateTime from, DateTime to)
    {
        string tok = MachineToken();
        if (string.IsNullOrEmpty(tok)) return -1;
        try
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            string json = "{\"p_machine\":" + JsonStr(Environment.MachineName) + ",\"p_token\":" + JsonStr(tok)
                + ",\"p_from\":" + JsonStr(Iso(from)) + ",\"p_to\":" + JsonStr(Iso(to)) + "}";
            using (TimedWebClient wc = new TimedWebClient())
            {
                wc.TimeoutMs = 6000;
                wc.Headers["apikey"] = GuardKey;
                wc.Headers["Authorization"] = "Bearer " + GuardKey;
                wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                string resp = wc.UploadString(BreakCheckUrl, "POST", json);
                if (resp.IndexOf("\"linked\": true") < 0 && resp.IndexOf("\"linked\":true") < 0) return -1;
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(resp, "\"net_min\"\\s*:\\s*(-?\\d+)");
                if (m.Success) return int.Parse(m.Groups[1].Value);
            }
        }
        catch (Exception e) { Log("Break check failed: " + e.Message); }
        return -1;
    }

    NoticeForm awayBox;
    // Information only: the period is already over, so there is nothing to mark. OK closes it.
    void ShowAwayBox(DateTime start, DateTime end, int netMins)
    {
        try
        {
            int mins = (int)Math.Round((end - start).TotalMinutes);
            string away = netMins == 1 ? "1 minute" : netMins + " minutes";
            string text = "You were away from " + start.ToString("h:mm tt") + " to " + end.ToString("h:mm tt")
                + (netMins < mins ? "\n(" + away + " outside your break)." : "\n(" + away + ").")
                + "\n\nThis is recorded as idle time in your day,\nseparate from breaks and time on floor.";
            if (awayBox != null && !awayBox.IsDisposed) awayBox.Close();
            awayBox = new NoticeForm("No movement recorded", text, Color.FromArgb(220, 38, 38), Color.White, "OK", null);
            awayBox.Show();
            awayBox.Activate();
        }
        catch { }
    }

    NoticeForm welcomeBox;
    // Shown at login and on unlock: end a break or punch in. Open Shastra brings Shastra forward.
    void ShowWelcomeBox()
    {
        try
        {
            if (welcomeBox != null && !welcomeBox.IsDisposed) welcomeBox.Close();
            welcomeBox = new NoticeForm("Welcome back",
                "Back from a break?\nEnd your break in Purview Shastra.\n\nStarting your shift?\nPunch in.",
                Color.FromArgb(232, 184, 74), Color.FromArgb(24, 24, 24), "Open Shastra",
                delegate { ThreadPool.QueueUserWorkItem(delegate { ShowShastra("Welcome"); }); });
            welcomeBox.Show();
            ThreadPool.QueueUserWorkItem(delegate {
                Thread.Sleep(1500);   // after Shastra has come forward, put the box back on top
                try { BeginInvoke((MethodInvoker)delegate { if (welcomeBox != null && !welcomeBox.IsDisposed) { welcomeBox.TopMost = true; welcomeBox.Activate(); } }); } catch { }
            });
        }
        catch { }
    }
    static System.Windows.Forms.Timer updTimer;
    static bool quietStart = false;
    static DateTime lastShutdownAsk = DateTime.MinValue;
    static DateTime allowEndUntil = DateTime.MinValue;   // set by "Shut down now" / "Restart now"
    IntPtr myHandle;
    static bool locking = false;
    static DateTime lastBeat = DateTime.MinValue;
    const string ChromeExe = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    const string AppArgs = "--profile-directory=Default --app-id=mfkmeohdndbongnjcidbneopbgmcljbl";
    static DateTime lastOpen = DateTime.MinValue;
    static ShastraGuard Instance;
    static bool hotkeyOk;
    static bool winLControlled = false;
    static DateTime lastWinL = DateTime.MinValue;
    static HookProc hookDelegate;
    static IntPtr hookHandle = IntPtr.Zero;
    const string PolicyKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    static uint LockMsg = RegisterWindowMessage("PurviewShastraGuardLock");
    ReminderForm reminder;

    static void Log(string m)
    {
        try { FileInfo fi = new FileInfo(LogPath); if (fi.Exists && fi.Length > 1000000) File.Delete(LogPath); } catch { }
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + m + Environment.NewLine); } catch { }
    }

    // ---------- Windows + L on/off ----------
    // Returns true if the setting could be changed (needs the one-time admin permission).
    static bool SetWinLDisabled(bool disabled)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(PolicyKey, RegistryKeyPermissionCheck.ReadWriteSubTree,
                       System.Security.AccessControl.RegistryRights.SetValue | System.Security.AccessControl.RegistryRights.QueryValues))
            {
                if (k == null) { Log("Windows + L setting not found"); return false; }
                if (disabled) k.SetValue("DisableLockWorkstation", 1, RegistryValueKind.DWord);
                else k.DeleteValue("DisableLockWorkstation", false);
                return true;
            }
        }
        catch (Exception e) { Log("Windows + L setting not changed: " + e.Message); return false; }
    }

    static IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && winLControlled && (wParam == (IntPtr)0x100 || wParam == (IntPtr)0x104))
        {
            KBDLLHOOKSTRUCT k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
            if (k.vkCode == 0x4C)
            {
                bool win = (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
                if (win && Instance != null)
                {
                    keybd_event(0xFF, 0, 0, UIntPtr.Zero);   // stops the Start menu opening when Windows key is released
                    keybd_event(0xFF, 0, 2, UIntPtr.Zero);
                    Instance.BeginInvoke((MethodInvoker)delegate { Instance.OnWinL(); });
                    return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(hookHandle, code, wParam, lParam);
    }

    static void ArmHook()
    {
        if (hookHandle != IntPtr.Zero) UnhookWindowsHookEx(hookHandle);
        if (hookDelegate == null) hookDelegate = KeyboardHook;
        hookHandle = SetWindowsHookEx(13, hookDelegate, GetModuleHandle(null), 0);
    }

    // Runs every minute: re-arms the keyboard watch and keeps Windows + L under control.
    void Heartbeat()
    {
        if (!winLControlled || locking) return;
        ArmHook();
        if (hookHandle == IntPtr.Zero) { SetWinLDisabled(false); Log("Keyboard watch could not be re-armed; Windows + L back to normal"); return; }
        SetWinLDisabled(true);
        if ((DateTime.Now - lastBeat).TotalMinutes >= 30) { lastBeat = DateTime.Now; Log("Still running"); }
    }

    // First Windows + L: show Shastra and the reminder. Second one (within 10 minutes): lock.
    void OnWinL()
    {
        bool reminderOpen = reminder != null && !reminder.IsDisposed;
        if (reminderOpen || (DateTime.Now - lastWinL).TotalMinutes < 10)
        {
            if (reminderOpen) reminder.Close();
            lastWinL = DateTime.MinValue;
            Log("Windows + L pressed again: locking");
            LockNow();
            return;
        }
        lastWinL = DateTime.Now;
        Log("Windows + L pressed: showing Shastra reminder");
        ShowReminder();
    }

    // ---------- Finding Shastra ----------
    static string ProcName(IntPtr h)
    {
        uint pid; GetWindowThreadProcessId(h, out pid);
        try { return Process.GetProcessById((int)pid).ProcessName; } catch { return "?"; }
    }

    static List<IntPtr> FindShastra()
    {
        List<IntPtr> found = new List<IntPtr>();
        EnumWindows(delegate (IntPtr h, IntPtr l)
        {
            if (!IsWindowVisible(h)) return true;
            StringBuilder t = new StringBuilder(512); GetWindowText(h, t, 512);
            string title = t.ToString();
            // Only the Shastra app window: its title starts with "Purview Shastra" and,
            // unlike a normal browser tab, does not end with the browser name.
            if (!title.StartsWith("Purview Shastra", StringComparison.OrdinalIgnoreCase)) return true;
            if (title.EndsWith("Google Chrome") || title.EndsWith("Microsoft Edge") || title.EndsWith("Microsoft\u200b Edge")) return true;
            string p = ProcName(h).ToLowerInvariant();
            if (p == "chrome" || p == "msedge" || p == "chrome_proxy") found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    static void LogWindows(string why)
    {
        StringBuilder sb = new StringBuilder();
        EnumWindows(delegate (IntPtr h, IntPtr l)
        {
            if (!IsWindowVisible(h)) return true;
            StringBuilder t = new StringBuilder(512); GetWindowText(h, t, 512);
            if (t.Length == 0) return true;
            StringBuilder c = new StringBuilder(256); GetClassName(h, c, 256);
            sb.Append(" [" + ProcName(h) + " | " + c + " | " + t + "]");
            return true;
        }, IntPtr.Zero);
        Log("DIAG " + why + ":" + sb.ToString());
    }

    static string ShortcutPath()
    {
        string[] places = {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"OneDrive\Desktop"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Chrome Apps"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Edge Apps")
        };
        foreach (string d in places)
        {
            string p = Path.Combine(d, "Purview Shastra.lnk");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    static void FocusWindow(IntPtr h)
    {
        SetWindowPos(h, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);  // briefly on top of everything
        SetWindowPos(h, (IntPtr)(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);  // then back to normal
        if (IsIconic(h)) ShowWindow(h, 9);
        keybd_event(0x12, 0, 0, UIntPtr.Zero);   // tap Alt so Windows allows the switch
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        BringWindowToTop(h);
        SetForegroundWindow(h);
    }

    // Opens or brings Shastra forward. Runs on a background thread.
    static void ShowShastra(string why)
    {
        List<IntPtr> wins = FindShastra();
        if (wins.Count > 0) { FocusWindow(wins[0]); Log(why + ": brought Shastra to the front"); return; }

        if (why == "Unlock") Thread.Sleep(1500); // let the desktop finish coming back
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            if (attempt == 1 && (DateTime.Now - lastOpen).TotalSeconds < 8) { }
            else
            {
                lastOpen = DateTime.Now;
                try
                {
                    // During a shut down Windows ignores the desktop icon, so open Chrome directly first.
                    bool direct = (why == "Shut down") ? attempt == 1 : attempt == 2;
                    string lnk = ShortcutPath();
                    if (!direct && lnk != null) Process.Start("explorer.exe", "\"" + lnk + "\"");
                    else if (File.Exists(ChromeExe)) Process.Start(ChromeExe, AppArgs);
                    else if (lnk != null) Process.Start("explorer.exe", "\"" + lnk + "\"");
                    Log(why + ": starting Purview Shastra (try " + attempt + ")");
                }
                catch (Exception e) { Log(why + ": could not open Shastra (" + e.Message + ")"); }
            }
            int tries = attempt == 1 ? 16 : 40;   // 4 seconds, then 10 seconds
            for (int i = 0; i < tries; i++)
            {
                Thread.Sleep(250);
                wins = FindShastra();
                if (wins.Count > 0)
                {
                    Thread.Sleep(400); // let the window finish drawing
                    FocusWindow(wins[0]);
                    Log(why + ": Shastra is open and in front");
                    return;
                }
            }
        }
        Log(why + ": Shastra window did not appear");
        LogWindows(why);
    }

    static void OnArrive(string why)
    {
        ThreadPool.QueueUserWorkItem(delegate { ShowShastra(why); });
        if (Instance != null) { try { Instance.BeginInvoke((MethodInvoker)delegate { Instance.ShowWelcomeBox(); }); } catch { } }
    }

    // ---------- Lock reminder ----------
    void ShowReminder()
    {
        if (reminder != null && !reminder.IsDisposed) { reminder.Activate(); return; }
        reminder = new ReminderForm(this);
        reminder.Show();
        ThreadPool.QueueUserWorkItem(delegate {
            ShowShastra("Lock key");
            Thread.Sleep(300);
            BeginInvoke((MethodInvoker)delegate { if (reminder != null && !reminder.IsDisposed) { reminder.TopMost = true; reminder.Activate(); } });
        });
    }

    ReminderForm shutdownBox;
    void ShowShutdownReminder()
    {
        if (shutdownBox != null && !shutdownBox.IsDisposed) { shutdownBox.Activate(); return; }
        shutdownBox = new ReminderForm(this, true);
        shutdownBox.FormClosed += delegate { ClearBlock(); };
        shutdownBox.Show();
        shutdownBox.Refresh();
        ThreadPool.QueueUserWorkItem(delegate {
            Thread.Sleep(1200);
            BeginInvoke((MethodInvoker)delegate { if (shutdownBox != null && !shutdownBox.IsDisposed) { shutdownBox.TopMost = true; shutdownBox.Activate(); } });
        });
    }

    public void OpenShastraFromBox()
    {
        foreach (ReminderForm f in new ReminderForm[] { reminder, shutdownBox })
            if (f != null && !f.IsDisposed) f.Compact();
        ThreadPool.QueueUserWorkItem(delegate { ShowShastra("Box"); });
    }

    public void PowerNow(bool restart)
    {
        Log(restart ? "Restart now clicked" : "Shut down now clicked");
        allowEndUntil = DateTime.Now.AddSeconds(90);   // let the shut down we are about to start go ahead
        ClearBlock();
        if (winLControlled) SetWinLDisabled(false);
        try { Process.Start(new ProcessStartInfo("shutdown.exe", restart ? "/r /t 0" : "/s /t 0") { CreateNoWindow = true, UseShellExecute = false }); }
        catch (Exception e) { Log("Could not start shut down: " + e.Message); }
    }

    public void LockNow()
    {
        Log("Locking");
        lastWinL = DateTime.MinValue;
        if (winLControlled)
        {
            locking = true;
            SetWinLDisabled(false);
            Thread.Sleep(100);
            if (!LockWorkStation()) { Thread.Sleep(300); LockWorkStation(); }
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 3000;
            t.Tick += delegate { t.Stop(); t.Dispose(); locking = false; SetWinLDisabled(true); };
            t.Start();
        }
        else LockWorkStation();
    }

    // ---------- Self update ----------
    static int VersionNumber(string v)
    {
        try { string[] p = v.Trim().Split('.'); return int.Parse(p[0]) * 10000 + int.Parse(p[1]) * 100 + (p.Length > 2 ? int.Parse(p[2]) : 0); }
        catch { return -1; }
    }

    static void CheckForUpdate()
    {
        if (updating) return;
        try
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            string upd = Path.Combine(dir, "update");
            using (WebClient wc = new WebClient())
            {
                wc.Headers["Cache-Control"] = "no-cache";
                string latest = wc.DownloadString(UpdateBase + "version.txt?t=" + DateTime.Now.Ticks).Trim();
                if (VersionNumber(latest) <= VersionNumber(CurrentVersion)) return;
                Log("Update available: " + latest + " (running " + CurrentVersion + ")");
                updating = true;
                Directory.CreateDirectory(upd);
                string src = Path.Combine(upd, "ShastraGuard.cs");
                // commit.txt names the exact release copy, which GitHub never serves stale
                string commit = "";
                try { commit = wc.DownloadString(UpdateBase + "commit.txt?t=" + DateTime.Now.Ticks).Trim(); } catch { }
                string srcUrl = (commit.Length >= 7)
                    ? "https://raw.githubusercontent.com/Purview-Healthcare/Shastra-Guard/" + commit + "/ShastraGuard.cs"
                    : UpdateBase + "ShastraGuard.cs?t=" + DateTime.Now.Ticks;
                wc.DownloadFile(srcUrl, src);
                string text = File.ReadAllText(src);
                if (text.IndexOf("class ShastraGuard") < 0 || text.IndexOf("CurrentVersion = \"" + latest + "\"") < 0)
                {
                    Log("Update file does not match version " + latest + "; will try again in 10 minutes");
                    updating = false;
                    if (updTimer != null) { updTimer.Stop(); updTimer.Interval = 10 * 60 * 1000; updTimer.Start(); }
                    return;
                }
                string net = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework64\v4.0.30319");
                string newExe = Path.Combine(upd, "ShastraGuard.exe");
                if (File.Exists(newExe)) File.Delete(newExe);
                ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(net, "csc.exe"),
                    "/nologo /target:winexe /out:\"" + newExe + "\" /lib:\"" + net + "\\WPF\" /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll \"" + src + "\"");
                psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                Process b = Process.Start(psi);
                string outp = b.StandardOutput.ReadToEnd() + b.StandardError.ReadToEnd();
                b.WaitForExit(120000);
                if (!File.Exists(newExe) || new FileInfo(newExe).Length < 20000) { Log("Update build failed: " + outp.Trim()); updating = false; return; }
                string swap = Path.Combine(upd, "swap.cmd");
                File.WriteAllText(swap,
                    "@echo off\r\n" +
                    "timeout /t 2 /nobreak >nul\r\n" +
                    "taskkill /im ShastraGuard.exe /f >nul 2>&1\r\n" +
                    "timeout /t 2 /nobreak >nul\r\n" +
                    "copy /y \"" + dir + "\\ShastraGuard.exe\" \"" + dir + "\\ShastraGuard.prev.exe\" >nul\r\n" +
                    "copy /y \"" + newExe + "\" \"" + dir + "\\ShastraGuard.exe\" >nul\r\n" +
                    "start \"\" \"" + dir + "\\ShastraGuard.exe\" --quiet\r\n");
                Log("Installing update " + latest + " and restarting");
                ProcessStartInfo sp = new ProcessStartInfo("cmd.exe", "/c \"" + swap + "\"");
                sp.CreateNoWindow = true; sp.UseShellExecute = false; sp.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(sp);
            }
        }
        catch (Exception e) { Log("Update check: " + e.Message); updating = false; }
    }

    // ---------- Form ----------
    public ShastraGuard()
    {
        Instance = this;
        Text = "Shastra Guard";
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        Size = new Size(1, 1);
        Opacity = 0;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        myHandle = Handle;
        winLControlled = SetWinLDisabled(true);
        if (winLControlled)
        {
            ArmHook();
            if (hookHandle == IntPtr.Zero) { SetWinLDisabled(false); winLControlled = false; }
        }
        Log("Windows + L reminder: " + (winLControlled ? "ON" : "off (admin permission not given yet, Windows + L locks normally)"));
        hotkeyOk = RegisterHotKey(Handle, 1, 0x0001 | 0x0002 | 0x4000, 0x4C); // Ctrl + Alt + L
        Log("Shastra Guard " + CurrentVersion + " started (Ctrl + Alt + L " + (hotkeyOk ? "ready" : "NOT available, another app uses it") + ")");
        GuardPing("start", DateTime.Now, null, null, null, "Shastra Guard " + CurrentVersion + " started");
        System.Windows.Forms.Timer beat = new System.Windows.Forms.Timer();
        beat.Interval = 60000;
        beat.Tick += delegate { Heartbeat(); };
        beat.Start();
        if (!quietStart) OnArrive("Login");
        else Log("Restarted in the background");
        updTimer = new System.Windows.Forms.Timer();
        updTimer.Interval = 90000;
        updTimer.Tick += delegate { updTimer.Interval = 6 * 60 * 60 * 1000; ThreadPool.QueueUserWorkItem(delegate { CheckForUpdate(); }); };
        updTimer.Start();
        System.Windows.Forms.Timer act = new System.Windows.Forms.Timer();
        act.Interval = 30000;
        act.Tick += delegate { CheckActivity(); CheckLinkCode(); };
        act.Start();
        System.Windows.Forms.Timer alive = new System.Windows.Forms.Timer();
        alive.Interval = 5 * 60 * 1000;   // tell Shastra this computer is still running, every 5 minutes
        alive.Tick += delegate { GuardPing("alive", null, null, null, null, null); };
        alive.Start();
    }

    void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.SessionLogon)
        {
            locking = false;
            if (lockStart != DateTime.MinValue) { WriteActivityRow(lockStart, DateTime.Now, "Screen locked"); lockStart = DateTime.MinValue; }
            idleStart = DateTime.MinValue;
            if (winLControlled) { ArmHook(); SetWinLDisabled(true); }
            lastWinL = DateTime.MinValue;
            OnArrive("Unlock");
        }
        else if (e.Reason == SessionSwitchReason.SessionLock)
        {
            // Close any open no-movement period at the lock time, then record the locked time separately.
            if (idleStart != DateTime.MinValue) { WriteActivityRow(idleStart, DateTime.Now, "No movement"); idleStart = DateTime.MinValue; }
            lockStart = DateTime.Now;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (winLControlled) SetWinLDisabled(false);
        base.OnFormClosed(e);
    }

    // First shut down / restart / sign out: pause it and bring Shastra forward.
    // Second one within 10 minutes: let it go ahead.
    const string ShutdownReason = "You are still punched in. Shutting down anyway is recorded as a missed punch out. Click Cancel, punch out in Purview Shastra, then shut down.";
    const string SkipLog = @"C:\Users\Public\Documents\ShastraPunchIn-missed-punch-outs.txt";
    IntPtr blockHandle = IntPtr.Zero;

    void ClearBlock()
    {
        if (blockHandle != IntPtr.Zero) { ShutdownBlockReasonDestroy(blockHandle); blockHandle = IntPtr.Zero; }
    }

    bool ShouldAllowEnd(long flags)
    {
        // Windows says it cannot wait (for example a critical update): never block.
        if ((flags & 0x40000000) != 0) { Log("Urgent shut down from Windows: allowed"); ClearBlock(); if (winLControlled) SetWinLDisabled(false); return true; }

        if (DateTime.Now < allowEndUntil)
        {
            Log("Shut down / restart allowed (chosen in the box)");
            ClearBlock();
            if (winLControlled) SetWinLDisabled(false);
            return true;
        }
        if ((DateTime.Now - lastShutdownAsk).TotalSeconds >= 3)   // not the same request arriving twice
        {
            lastShutdownAsk = DateTime.Now;
            // Show our box first, then hang the message on it, so Windows' own
            // "preventing shut down" page shows "Purview Shastra" with this reminder.
            try { ShowShutdownReminder(); } catch (Exception e) { Log("Box error: " + e.Message); }
            blockHandle = (shutdownBox != null && !shutdownBox.IsDisposed) ? shutdownBox.Handle : myHandle;
            ShutdownBlockReasonCreate(blockHandle, ShutdownReason);
            // Windows shows its own page as soon as we answer, so answer late: bring Shastra and
            // the box forward first, for up to 4 seconds (Windows allows 5).
            bool shastraShown = false;
            ThreadPool.QueueUserWorkItem(delegate { ShowShastra("Shut down"); shastraShown = true; });
            DateTime until = DateTime.Now.AddSeconds(4);
            while (DateTime.Now < until && !shastraShown) { Application.DoEvents(); Thread.Sleep(50); }
            if (shutdownBox != null && !shutdownBox.IsDisposed) { shutdownBox.TopMost = true; shutdownBox.Activate(); Application.DoEvents(); }
            Log("Shut down / restart paused: showing Shastra reminder" + (shastraShown ? "" : " (Shastra still opening)"));
            return false;
        }
        return false;
    }

    const int WM_QUERYENDSESSION = 0x11, WM_ENDSESSION = 0x16, WM_HOTKEY = 0x312;
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY || (uint)m.Msg == LockMsg) { ShowReminder(); return; }
        if (m.Msg == WM_QUERYENDSESSION)
        {
            Log("Windows is asking to end the session (flags " + m.LParam.ToInt64().ToString("X") + ")");
            m.Result = ShouldAllowEnd(m.LParam.ToInt64()) ? (IntPtr)1 : IntPtr.Zero;
            return;
        }
        if (m.Msg == WM_ENDSESSION && m.WParam != IntPtr.Zero)
        {
            if (DateTime.Now >= allowEndUntil && blockHandle != IntPtr.Zero)
            {
                Log("Shut down anyway was used: Shastra reminder skipped");
                try { File.AppendAllText(SkipLog, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + Environment.UserName + "  shut down anyway, Shastra reminder skipped" + Environment.NewLine); } catch { }
                GuardPingNow("missed_punch_out", DateTime.Now, null, null, null, "shut down or signed out while still punched in");
            }
            else Log("Session is ending now");
        }
        if (m.Msg == WM_ENDSESSION && m.WParam == IntPtr.Zero)
        {
            Log("Shut down was cancelled");
            ClearBlock();  // shut down was cancelled: back to normal
            if (winLControlled) SetWinLDisabled(true);
        }
        base.WndProc(ref m);
    }

    [STAThread]
    static void Main()
    {
        bool created;
        Mutex mx = new Mutex(true, "PurviewShastraGuard", out created);
        string[] args = Environment.GetCommandLineArgs();
        bool lockMode = args.Length > 1 && args[1].ToLowerInvariant() == "--lock";
        quietStart = args.Length > 1 && args[1].ToLowerInvariant() == "--quiet";
        if (!created)
        {
            if (lockMode) PostMessage((IntPtr)0xFFFF, LockMsg, IntPtr.Zero, IntPtr.Zero);
            return;
        }
        if (lockMode) { Log("Lock icon used but Shastra Guard was not running; locking directly"); LockWorkStation(); }
        AppDomain.CurrentDomain.ProcessExit += delegate { if (winLControlled) SetWinLDisabled(false); };
        AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs ex) { if (winLControlled) SetWinLDisabled(false); Log("Crashed: " + ex.ExceptionObject); };
        SetProcessShutdownParameters(0x3FF, 0);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += delegate (object s, ThreadExceptionEventArgs ex) { Log("Error (kept running): " + ex.Exception.Message); };
        Application.EnableVisualStyles();
        Application.Run(new ShastraGuard());
        GC.KeepAlive(mx);
    }
}

class ReminderForm : Form
{
    public ReminderForm(ShastraGuard owner) : this(owner, false) { }

    public ReminderForm(ShastraGuard owner, bool shutdown)
    {
        Text = "Purview Shastra";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        TopMost = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 270);
        BackColor = Color.FromArgb(24, 24, 24);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 12f);

        // Big red exclamation mark
        Bitmap icon = new Bitmap(110, 110);
        using (Graphics g = Graphics.FromImage(icon))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.FillEllipse(new SolidBrush(Color.FromArgb(220, 38, 38)), 3, 3, 104, 104);
            using (Font f = new Font("Segoe UI", 54f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString("!", f, Brushes.White, new RectangleF(3, 5, 104, 104), sf);
            }
        }
        pic = new PictureBox();
        pic.Image = icon;
        pic.SizeMode = PictureBoxSizeMode.AutoSize;
        pic.Location = new Point(32, 40);

        title = new Label();
        title.Text = shutdown ? "Before you shut down" : "Before you lock";
        title.Font = new Font("Segoe UI", 20f, FontStyle.Bold);
        title.ForeColor = Color.FromArgb(232, 184, 74);
        title.SetBounds(170, 36, 450, 44);

        msg = new Label();
        msg.Text = shutdown ? "Punch out in Purview Shastra first.\nThen choose what you want to do." : "Going on a break or logging out?\nMark it in Purview Shastra first.";
        msg.Font = new Font("Segoe UI", 13f);
        msg.SetBounds(172, 88, 450, 64);

        Controls.Add(pic); Controls.Add(title); Controls.Add(msg);
        int right = ClientSize.Width - 28, top = 190, h = 48;

        openBtn = MakeButton("Open Shastra", false);
        openBtn.SetBounds(32, top, 170, h);
        openBtn.Click += delegate { owner.OpenShastraFromBox(); };
        Controls.Add(openBtn);

        if (shutdown)
        {
            Button restartBtn = MakeButton("Restart now", true);
            restartBtn.SetBounds(right - 150, top, 150, h);
            restartBtn.Click += delegate { Close(); owner.PowerNow(true); };
            Button offBtn = MakeButton("Shut down now", true);
            offBtn.SetBounds(right - 150 - 14 - 170, top, 170, h);
            offBtn.Click += delegate { Close(); owner.PowerNow(false); };
            Controls.Add(restartBtn); Controls.Add(offBtn);
        }
        else
        {
            Button lockBtn = MakeButton("Lock now", true);
            lockBtn.SetBounds(right - 150, top, 150, h);
            lockBtn.Click += delegate { Close(); owner.LockNow(); };
            Controls.Add(lockBtn);
            AcceptButton = lockBtn;
        }
    }

    PictureBox pic; Label title; Label msg; Button openBtn;
    bool compact = false;

    // Shrink to a small strip in the bottom-right corner with just the action buttons,
    // so Shastra can be used while the box stays one click away.
    public void Compact()
    {
        if (compact) return;
        compact = true;
        pic.Visible = false; title.Visible = false; msg.Visible = false; openBtn.Visible = false;
        Label hint = new Label();
        hint.Text = "Done in Shastra?";
        hint.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        hint.ForeColor = Color.FromArgb(232, 184, 74);
        hint.AutoSize = true;
        Controls.Add(hint);
        int x = 20;
        hint.Location = new Point(x, 22);
        x += hint.PreferredWidth + 16;
        foreach (Control c in Controls)
        {
            Button b = c as Button;
            if (b == null || !b.Visible) continue;
            b.SetBounds(x, 12, b.Width, 40);
            x += b.Width + 10;
        }
        ClientSize = new Size(x + 10, 64);
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);
        TopMost = true;
    }

    static Button MakeButton(string text, bool primary)
    {
        Button b = new Button();
        b.Text = text;
        b.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        b.FlatStyle = FlatStyle.Flat;
        if (primary) { b.BackColor = Color.FromArgb(232, 184, 74); b.ForeColor = Color.Black; }
        else { b.ForeColor = Color.FromArgb(232, 184, 74); b.FlatAppearance.BorderColor = Color.FromArgb(232, 184, 74); }
        return b;
    }
}

// A centered notice box: colored circle with "!", a title, a message, and one button.
// Used for "No movement recorded" (red, OK) and "Welcome back" (amber, Open Shastra).
class NoticeForm : Form
{
    public NoticeForm(string heading, string message, Color circle, Color mark, string buttonText, MethodInvoker onButton)
    {
        Text = "Purview Shastra";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        TopMost = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 330);
        BackColor = Color.FromArgb(24, 24, 24);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 12f);

        Bitmap icon = new Bitmap(140, 140);
        using (Graphics g = Graphics.FromImage(icon))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.FillEllipse(new SolidBrush(circle), 3, 3, 134, 134);
            using (Font bf = new Font("Segoe UI", 70f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush mb = new SolidBrush(mark))
            {
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString("!", bf, mb, new RectangleF(3, 6, 134, 134), sf);
            }
        }
        PictureBox pic = new PictureBox();
        pic.Image = icon; pic.SizeMode = PictureBoxSizeMode.AutoSize;
        pic.Location = new Point(40, 50);

        Label title = new Label();
        title.Text = heading;
        title.Font = new Font("Segoe UI", 22f, FontStyle.Bold);
        title.ForeColor = Color.FromArgb(232, 184, 74);
        title.SetBounds(210, 48, 520, 40);

        Label msg = new Label();
        msg.Text = message;
        msg.Font = new Font("Segoe UI", 15f);
        msg.SetBounds(212, 100, 520, 150);

        Controls.Add(pic); Controls.Add(title); Controls.Add(msg);

        Button b = new Button();
        b.Text = buttonText;
        b.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = Color.FromArgb(232, 184, 74); b.ForeColor = Color.Black;
        int w = buttonText.Length > 4 ? 220 : 150;
        b.SetBounds(ClientSize.Width - 34 - w, 256, w, 52);
        b.Click += delegate { Close(); if (onButton != null) onButton(); };
        Controls.Add(b);
        AcceptButton = b;
    }
}

// A WebClient with a short timeout, so a slow or missing network never holds up Shastra Guard.
class TimedWebClient : WebClient
{
    public int TimeoutMs = 100000;
    protected override WebRequest GetWebRequest(Uri address)
    {
        WebRequest w = base.GetWebRequest(address);
        if (w != null) w.Timeout = TimeoutMs;
        return w;
    }
}
