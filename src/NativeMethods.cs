using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PowerOffManager;

/// <summary>Windows 系统能力适配层：电源动作、空闲检测、会话检测。</summary>
internal static class NativeMethods
{
    // ---- 关机 / 重启 ----
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr hProcess, uint desiredAccess, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out long lpLuid);

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public int PrivilegeCount;
        public long Luid;
        public int Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll,
        ref TOKEN_PRIVILEGES newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ExitWindowsEx(uint flags, uint reason);

    private const uint EWX_LOGOFF = 0x00000000;
    private const uint EWX_SHUTDOWN = 0x00000001;
    private const uint EWX_REBOOT = 0x00000002;
    private const uint EWX_FORCE = 0x00000004;          // 强制结束未响应程序
    private const uint EWX_FORCEIFHUNG = 0x00000010;    // 仅强制挂起程序
    private const int ERROR_SUCCESS = 0;

    /// <summary>
    /// 启用关机权限。关键点：AdjustTokenPrivileges 返回 true 并不代表权限已授予，
    /// 必须再检查 GetLastError；同时必须关闭 token 句柄，否则每次执行都泄漏。
    /// </summary>
    private static bool EnableShutdownPrivilege()
    {
        IntPtr token = IntPtr.Zero;
        try
        {
            const uint TOKEN_QUERY = 0x0008;
            const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle,
                    TOKEN_QUERY | TOKEN_ADJUST_PRIVILEGES, out token))
                return false;

            if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out long luid))
                return false;

            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = 0x00000002 // SE_PRIVILEGE_ENABLED
            };
            if (!AdjustTokenPrivileges(token, false, ref tp,
                    Marshal.SizeOf(typeof(TOKEN_PRIVILEGES)), IntPtr.Zero, IntPtr.Zero))
                return false;

            // 关键修复：AdjustTokenPrivileges 即使未授予权限也可能返回 true
            return Marshal.GetLastWin32Error() == ERROR_SUCCESS;
        }
        finally
        {
            if (token != IntPtr.Zero) CloseHandle(token);
        }
    }

    // ---- 睡眠 / 休眠 ----
    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    // ---- 锁屏 / 关显示器 ----
    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int HWND_BROADCAST = 0xFFFF;
    private const uint WM_SYSCOMMAND = 0x0112;
    private const int SC_MONITORPOWER = 0xF170;

    // ---- 空闲检测 ----
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>返回自上次键鼠输入以来的秒数（用无符号差值正确处理 TickCount 回绕）。</summary>
    public static double GetIdleSeconds()
    {
        var lii = new LASTINPUTINFO();
        lii.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
        if (!GetLastInputInfo(ref lii)) return 0;
        uint tickCount = unchecked((uint)Environment.TickCount);
        // 无符号减法自动处理 49.7 天回绕
        uint elapsed = unchecked(tickCount - lii.dwTime);
        return elapsed / 1000.0;
    }

    // ---- 会话检测 ----
    [DllImport("kernel32.dll")]
    private static extern int WTSGetActiveConsoleSessionId();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private const int SM_REMOTESESSION = 0x1000;

    public static bool IsRemoteSession() => GetSystemMetrics(SM_REMOTESESSION) != 0;

    // ---- 阻止系统睡眠 ----
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;

    public static void KeepAwake(bool keep) =>
        SetThreadExecutionState(keep ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS);

    // ---- 系统命令兜底 ----
    /// <summary>
    /// 用 shutdown.exe 执行关机/重启/注销。这是最可靠的方式：
    /// 该命令由系统进程执行，自带权限，/f 强制关闭所有程序，绝不卡在 WM_QUERYENDSESSION。
    /// </summary>
    private static bool RunSystemShutdown(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("shutdown.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            return p != null;
        }
        catch (Exception ex)
        {
            Logger.Warn("shutdown.exe 调用失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 执行电源动作。本方法设计为在后台线程调用，绝不阻塞 UI。
    /// 关机/重启优先用系统 shutdown.exe（带 /f 强制），失败时回退到 API + 强制标志。
    /// </summary>
    public static string Execute(PowerAction action, bool force)
    {
        try
        {
            switch (action)
            {
                case PowerAction.Shutdown:
                    // /f 强制关闭未响应程序，保证 100% 执行；API 作为兜底
                    if (RunSystemShutdown("/s /f /t 0")) return "关机指令已发出";
                    EnableShutdownPrivilege();
                    return ExitWindowsEx(EWX_SHUTDOWN | EWX_FORCEIFHUNG | EWX_FORCE, 0)
                        ? "关机指令已发出（API）" : "关机失败：请检查权限";

                case PowerAction.Restart:
                    if (RunSystemShutdown("/r /f /t 0")) return "重启指令已发出";
                    EnableShutdownPrivilege();
                    return ExitWindowsEx(EWX_REBOOT | EWX_FORCEIFHUNG | EWX_FORCE, 0)
                        ? "重启指令已发出（API）" : "重启失败：请检查权限";

                case PowerAction.Logoff:
                    if (RunSystemShutdown("/l /f")) return "注销指令已发出";
                    return ExitWindowsEx(EWX_LOGOFF | EWX_FORCEIFHUNG | EWX_FORCE, 0)
                        ? "注销指令已发出（API）" : "注销失败";

                case PowerAction.Sleep:
                    // forceCritical=true 保证即使有程序未响应也能进入睡眠
                    return SetSuspendState(false, true, true)
                        ? "睡眠指令已发出" : "睡眠失败（系统可能不支持）";

                case PowerAction.Hibernate:
                    return SetSuspendState(true, true, true)
                        ? "休眠指令已发出" : "休眠失败（系统可能未启用休眠）";

                case PowerAction.Lock:
                    return LockWorkStation() ? "已锁定工作站" : "锁屏失败";

                case PowerAction.MonitorOff:
                    // SendMessage 到 HWND_BROADCAST 会等待所有窗口处理，可能卡住；
                    // 这里在后台线程调用，且用 PostMessage 语义的异步广播替代。
                    PostMessage((IntPtr)HWND_BROADCAST, WM_SYSCOMMAND,
                        (IntPtr)SC_MONITORPOWER, (IntPtr)2);
                    return "显示器已关闭（移动鼠标可唤醒）";

                default:
                    return "未知动作";
            }
        }
        catch (Exception ex)
        {
            Logger.Error("执行电源动作异常", ex);
            return "执行失败：" + ex.Message;
        }
    }

    /// <summary>异步广播消息，不等待任何窗口响应，避免广播卡死。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>供调度器在后台线程安全调用执行动作。</summary>
    public static Task<string> ExecuteAsync(PowerAction action, bool force) =>
        Task.Run(() => Execute(action, force));
}
