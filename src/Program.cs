using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace PowerOffManager;

internal static class Program
{
    public static IconLoader Icons { get; } = new IconLoader();

    /// <summary>开机自启快捷方式携带此参数，用于区分「自启动」与「用户手动双击」。</summary>
    public const string AutoStartArgument = "--autostart";

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            Logger.Error("未捕获异常", e.ExceptionObject as Exception);
        Application.ThreadException += (s, e) =>
            Logger.Error("UI 线程异常", e.Exception);

        bool autoStart = Array.Exists(args,
            a => string.Equals(a, AutoStartArgument, StringComparison.OrdinalIgnoreCase));

        if (autoStart)
        {
            // 开机自启：创建窗体与消息循环，但不显示主界面，程序常驻右下角托盘。
            // 注意这里刻意使用 Application.Run()（无主窗体），避免框架自动 Show() 主窗口。
            var form = new MainForm(startToTray: true);
            var _ = form.Handle; // 强制创建句柄，确保跨线程封送与托盘气泡可用
            form.BeginInvoke(new Action(form.NotifyAutoStarted));
            Logger.Info("开机自启动：主界面不显示，程序在托盘运行");
            Application.Run();
        }
        else
        {
            // 用户手动双击：保持原行为，正常显示主界面
            Application.Run(new MainForm());
        }
    }
}

/// <summary>从程序集嵌入资源加载图标，保证单文件内可用。</summary>
internal sealed class IconLoader
{
    private readonly System.Drawing.Icon _app;

    public IconLoader()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("PowerOffManager.AppIcon");
            _app = stream != null
                ? new System.Drawing.Icon(stream)
                : System.Drawing.SystemIcons.Application;
        }
        catch
        {
            _app = System.Drawing.SystemIcons.Application;
        }
    }

    public System.Drawing.Icon App => _app;
}

/// <summary>极简文件日志。</summary>
internal static class Logger
{
    private static readonly object Lock = new();
    private static string LogDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PowerOffManager", "logs");

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg, Exception ex = null) =>
        Write("ERROR", msg + (ex != null ? " | " + ex : ""));

    private static void Write(string level, string msg)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(LogDir);
                var file = Path.Combine(LogDir, $"log-{DateTime.Now:yyyyMMdd}.txt");
                File.AppendAllText(file,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {msg}{Environment.NewLine}");
            }
        }
        catch { /* 日志失败不影响主功能 */ }
    }

    public static string ExportPath => LogDir;
}
