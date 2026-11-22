using System;
using System.IO;
using System.Text.Json;

namespace PowerOffManager;

internal enum TriggerMode { Countdown, Scheduled, Idle }
internal enum PowerAction { Shutdown, Restart, Sleep, Hibernate, Logoff, Lock, MonitorOff }
internal enum RepeatType { Once, Daily, Weekly }

internal sealed class AppConfig
{
    // 触发模式
    public TriggerMode Mode { get; set; } = TriggerMode.Countdown;

    // 倒计时
    public int CountdownMinutes { get; set; } = 10;
    public int CountdownSeconds { get; set; } = 0;

    // 时间点 / 周期
    public string ScheduledTime { get; set; } = "23:30";
    public RepeatType Repeat { get; set; } = RepeatType.Once;
    /// <summary>每周重复的星期：0=周日 ... 6=周六。</summary>
    public bool[] Weekdays { get; set; } = { false, true, true, true, true, true, false };

    // 空闲检测
    public int IdleMinutes { get; set; } = 15;

    // 电源动作
    public PowerAction Action { get; set; } = PowerAction.Shutdown;
    public bool Force { get; set; } = false;

    // 提醒
    public int ReminderSeconds { get; set; } = 60;

    // 偏好
    public bool StartWithWindows { get; set; } = false;
    public bool EnableLog { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;

    // 运行时激活状态（持久化以支持重启恢复）
    public bool TaskActive { get; set; } = false;
    public string ActivatedAt { get; set; } = "";
    public string TargetUtcTicks { get; set; } = "";
    public int SnoozeCount { get; set; } = 0;
}

internal static class ConfigService
{
    private static readonly JsonSerializerOptions JsonOpt = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PowerOffManager");
    public static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOpt);
                if (cfg != null)
                {
                    // 兼容旧配置：进程触发模式已下线，越界时回退到倒计时
                    if ((int)cfg.Mode > (int)TriggerMode.Idle) cfg.Mode = TriggerMode.Countdown;
                    return cfg;
                }
            }
        }
        catch (Exception ex) { Logger.Warn("读取配置失败，使用默认：" + ex.Message); }
        return new AppConfig();
    }

    public static void Save(AppConfig cfg)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, JsonOpt));
        }
        catch (Exception ex) { Logger.Warn("保存配置失败：" + ex.Message); }
    }

    /// <summary>
    /// 兼容旧版本：若启动目录已有自启快捷方式但缺少 --autostart 参数，自动重建修复。
    /// 这样用户升级新版后无需手动取消/重勾「开机自启」即可享受托盘启动。
    /// </summary>
    public static void EnsureAutoStartArgument()
    {
        try
        {
            var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var link = Path.Combine(startup, "准点定时关机管家.lnk");
            if (!File.Exists(link)) return;

            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return;
            object sh = Activator.CreateInstance(t);
            var sc = t.InvokeMember("CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { link });
            var args = sc.GetType().InvokeMember("Arguments",
                System.Reflection.BindingFlags.GetProperty, null, sc, null) as string;

            if (!string.Equals(args, Program.AutoStartArgument, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info("检测到旧版自启快捷方式，正在补充 --autostart 参数");
                SetStartWithWindows(true);
            }
        }
        catch (Exception ex) { Logger.Warn("检查自启快捷方式失败：" + ex.Message); }
    }

    /// <summary>开机自启快捷方式：带上 --autostart，使程序启动时只驻留托盘、不弹主界面。</summary>
    public static void SetStartWithWindows(bool enable)
    {
        try
        {
            var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var link = Path.Combine(startup, "准点定时关机管家.lnk");
            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;

                // 每次都重建快捷方式：兼容旧版本创建的无参数快捷方式，
                // 确保Arguments中一定带有 --autostart。
                if (File.Exists(link)) File.Delete(link);

                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;
                object sh = Activator.CreateInstance(t);
                var sc = t.InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { link });
                var scType = sc.GetType();
                void Set(string prop, object value) => scType.InvokeMember(prop,
                    System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { value });

                Set("TargetPath", exe);
                Set("Arguments", Program.AutoStartArgument);
                Set("WorkingDirectory", Path.GetDirectoryName(exe) ?? "");
                Set("IconLocation", exe + ",0");
                Set("Description", "准点 · 定时关机管家（开机后台运行）");
                scType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod,
                    null, sc, null);
                Logger.Info("开机自启快捷方式已更新（带 --autostart 参数）");
            }
            else if (File.Exists(link))
            {
                File.Delete(link);
            }
        }
        catch (Exception ex) { Logger.Warn("设置开机自启失败：" + ex.Message); }
    }
}
