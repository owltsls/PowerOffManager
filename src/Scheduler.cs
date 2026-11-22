using System;
using System.Diagnostics;
using System.Threading;
using System.Timers;

namespace PowerOffManager;

/// <summary>调度状态。</summary>
internal enum TaskState { Idle, Running, Reminding, Executed }

/// <summary>
/// 核心调度引擎：单一激活任务，统一处理三种触发模式。
/// 倒计时用 Stopwatch 单调时钟，避免系统时间被改导致错乱（R-07）。
/// 电源动作在后台线程异步执行，绝不阻塞调度/UI 线程。
/// </summary>
internal sealed class Scheduler
{
    private readonly System.Timers.Timer _timer = new(500);
    private readonly Stopwatch _sw = new();
    private double _countdownTotalMs;
    private double _idleTargetSec;
    private DateTime _targetUtc;
    private DateTime _idleSnoozeUntil = DateTime.MinValue; // 空闲模式贪睡截止时刻
    private bool _scheduledFiredToday;
    private DateTime _lastSnoozeTime = DateTime.MinValue;
    private int _executing; // 幂等闸门：0=可执行，1=已触发执行

    public AppConfig Config { get; private set; }
    public TaskState State { get; private set; } = TaskState.Idle;

    // 事件（在调用线程触发，UI 层负责封送）
    public event Action<string> StatusChanged;
    public event Action ReminderRequested;
    public event Action<string> ExecutionRequested;
    public event Action TaskEnded;

    public Scheduler(AppConfig config)
    {
        Config = config;
        _timer.Elapsed += OnTick;
        _timer.AutoReset = true;
    }

    public void UpdateConfig(AppConfig cfg) => Config = cfg;

    public bool IsRunning => State == TaskState.Running || State == TaskState.Reminding;

    public void Start()
    {
        Stop(true);
        _scheduledFiredToday = false;
        _lastSnoozeTime = DateTime.MinValue;
        _idleSnoozeUntil = DateTime.MinValue;
        Interlocked.Exchange(ref _executing, 0);
        State = TaskState.Running;
        Config.TaskActive = true;
        Config.ActivatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        Config.SnoozeCount = 0;

        switch (Config.Mode)
        {
            case TriggerMode.Countdown:
                _countdownTotalMs = (Config.CountdownMinutes * 60 + Config.CountdownSeconds) * 1000.0;
                _sw.Restart();
                _targetUtc = DateTime.UtcNow.AddMilliseconds(_countdownTotalMs);
                Config.TargetUtcTicks = _targetUtc.Ticks.ToString();
                break;

            case TriggerMode.Scheduled:
                _targetUtc = ParseTodayLocal(Config.ScheduledTime).ToUniversalTime();
                if (_targetUtc <= DateTime.UtcNow) _targetUtc = _targetUtc.AddDays(1);
                Config.TargetUtcTicks = _targetUtc.Ticks.ToString();
                break;

            case TriggerMode.Idle:
                _idleTargetSec = Config.IdleMinutes * 60.0;
                NativeMethods.KeepAwake(false);
                break;
        }

        ConfigService.Save(Config);
        _timer.Start();
        Logger.Info($"任务启动：模式={Config.Mode}, 动作={Config.Action}, 提前提醒={Config.ReminderSeconds}s");
        EmitStatus();
    }

    /// <summary>重启后恢复之前激活的任务（按 UTC 目标时间）。</summary>
    public bool TryRestore()
    {
        if (!Config.TaskActive) return false;
        if (string.IsNullOrEmpty(Config.TargetUtcTicks) ||
            !long.TryParse(Config.TargetUtcTicks, out long ticks))
        {
            ClearActive();
            return false;
        }

        _scheduledFiredToday = false;
        _lastSnoozeTime = DateTime.MinValue;
        _idleSnoozeUntil = DateTime.MinValue;
        Interlocked.Exchange(ref _executing, 0);
        State = TaskState.Running;

        if (Config.Mode == TriggerMode.Countdown || Config.Mode == TriggerMode.Scheduled)
        {
            _targetUtc = new DateTime(ticks, DateTimeKind.Utc);
            if (Config.Mode == TriggerMode.Countdown)
            {
                var remain = _targetUtc - DateTime.UtcNow;
                _countdownTotalMs = Math.Max(0, remain.TotalMilliseconds);
                _sw.Restart();
            }
        }
        else if (Config.Mode == TriggerMode.Idle)
        {
            _idleTargetSec = Config.IdleMinutes * 60.0;
        }

        _timer.Start();
        Logger.Info("已从持久化配置恢复激活任务");
        EmitStatus();
        return true;
    }

    public void Stop(bool silent = false)
    {
        _timer.Stop();
        NativeMethods.KeepAwake(false);
        Interlocked.Exchange(ref _executing, 0);
        if (State != TaskState.Idle)
        {
            State = TaskState.Idle;
            ClearActive();
            if (!silent) { Logger.Info("任务已取消"); TaskEnded?.Invoke(); }
        }
        EmitStatus();
    }

    public void MarkExecuted()
    {
        _timer.Stop();
        NativeMethods.KeepAwake(false);
        State = TaskState.Executed;
        ClearActive();
        EmitStatus();
    }

    public void MarkReminding() { State = TaskState.Reminding; EmitStatus(); }

    /// <summary>推迟（贪睡）N 分钟：重新设定目标时间并回到运行态。</summary>
    public void Snooze(int minutes)
    {
        if (minutes <= 0) return;
        Config.SnoozeCount++;
        _lastSnoozeTime = DateTime.Now;

        if (Config.Mode == TriggerMode.Countdown)
        {
            _countdownTotalMs = minutes * 60 * 1000.0;
            _sw.Restart();
            _targetUtc = DateTime.UtcNow.AddMinutes(minutes);
        }
        else if (Config.Mode == TriggerMode.Idle)
        {
            // 修复：空闲模式必须单独记录贪睡截止时刻，否则 CheckIdle 不认账，
            // 下一 tick 立即再次进入提醒，形成提醒轰炸。
            _idleSnoozeUntil = DateTime.Now.AddMinutes(minutes);
            _targetUtc = DateTime.UtcNow.AddMinutes(minutes);
        }
        else
        {
            _targetUtc = DateTime.UtcNow.AddMinutes(minutes);
            _scheduledFiredToday = false;
        }
        Config.TargetUtcTicks = _targetUtc.Ticks.ToString();
        ConfigService.Save(Config);
        State = TaskState.Running;
        Logger.Info($"贪睡推迟 {minutes} 分钟（第 {Config.SnoozeCount} 次）");
        EmitStatus();
    }

    private void ClearActive()
    {
        Config.TaskActive = false;
        Config.TargetUtcTicks = "";
        Config.SnoozeCount = 0;
        ConfigService.Save(Config);
    }

    private static DateTime ParseTodayLocal(string hhmm)
    {
        if (TimeSpan.TryParse(hhmm, out var ts))
            return DateTime.Today.Add(ts);
        return DateTime.Today.AddHours(23).AddMinutes(30);
    }

    private void OnTick(object sender, ElapsedEventArgs e)
    {
        // Reminding 状态需要继续检查（空闲模式用户可能一直不操作，需按累计空闲时间执行）
        // 但 Executed / Idle 状态不再处理
        if (State != TaskState.Running && State != TaskState.Reminding) return;
        try
        {
            switch (Config.Mode)
            {
                case TriggerMode.Countdown:
                    CheckCountdown();
                    break;
                case TriggerMode.Scheduled:
                    CheckScheduled();
                    break;
                case TriggerMode.Idle:
                    CheckIdle();
                    break;
            }
            EmitStatus();
        }
        catch (Exception ex) { Logger.Error("调度 tick 异常", ex); }
    }

    private void CheckCountdown()
    {
        double remainMs = _countdownTotalMs - _sw.Elapsed.TotalMilliseconds;
        double remindMs = Config.ReminderSeconds * 1000.0;

        if (remainMs <= remindMs && State == TaskState.Running)
        {
            MarkReminding();
            ReminderRequested?.Invoke();
        }
        if (remainMs <= 0)
        {
            RequestExecution();
        }
    }

    private void CheckScheduled()
    {
        var nowLocal = DateTime.Now;
        bool dayMatch = Config.Repeat switch
        {
            RepeatType.Once => true,
            RepeatType.Daily => true,
            RepeatType.Weekly => Config.Weekdays != null &&
                                 Config.Weekdays.Length == 7 &&
                                 Config.Weekdays[(int)nowLocal.DayOfWeek],
            _ => true
        };

        var targetLocal = ParseTodayLocal(Config.ScheduledTime);

        // 距离触发点的秒数
        double diffSec = (targetLocal - nowLocal).TotalSeconds;
        if (diffSec < 0) diffSec += 86400; // 跨天

        if (!dayMatch) return;

        if (diffSec <= Config.ReminderSeconds && State == TaskState.Running && !_scheduledFiredToday)
        {
            MarkReminding();
            ReminderRequested?.Invoke();
            _scheduledFiredToday = true;
        }
        if (diffSec <= 0 && _scheduledFiredToday)
        {
            RequestExecution();
        }
    }

    private void CheckIdle()
    {
        // 贪睡保护期内不触发任何提醒或执行
        if (DateTime.Now < _idleSnoozeUntil) return;

        double idle = NativeMethods.GetIdleSeconds();

        if (idle >= _idleTargetSec - Config.ReminderSeconds && State == TaskState.Running)
        {
            MarkReminding();
            ReminderRequested?.Invoke();
        }
        // 修复竞态：达到目标空闲时间就执行，与提醒状态无关；RequestExecution 自带幂等闸门
        if (idle >= _idleTargetSec)
        {
            RequestExecution();
        }
    }

    /// <summary>
    /// 统一的执行入口：幂等（同一任务只执行一次），先把状态切到 Executed 停表，
    /// 再把真正的电源动作丢到后台线程异步执行，绝不阻塞调度线程与 UI 线程。
    /// </summary>
    private void RequestExecution()
    {
        if (Interlocked.Exchange(ref _executing, 1) != 0) return; // 已执行，直接返回

        _timer.Stop();
        NativeMethods.KeepAwake(false);
        State = TaskState.Executed;
        ClearActive();
        EmitStatus();
        Logger.Info($"触发执行：模式={Config.Mode}, 动作={Config.Action}, 强制={Config.Force}");

        var action = Config.Action;
        var force = Config.Force;
        _ = NativeMethods.ExecuteAsync(action, force).ContinueWith(t =>
        {
            string msg = t.Exception != null ? "执行异常：" + t.Exception.InnerException?.Message : t.Result;
            Logger.Info($"电源动作结果：{action} -> {msg}");
            ExecutionRequested?.Invoke(msg);
        });
    }

    /// <summary>供提醒框「立即执行」按钮调用，走同一幂等入口。</summary>
    public void ExecuteNow() => RequestExecution();

    private void EmitStatus()
    {
        string text = State switch
        {
            TaskState.Idle => "空闲 · 当前无任务",
            TaskState.Executed => "已执行 · 即将生效",
            TaskState.Reminding => $"提醒中 · 即将{GetActionName()}",
            TaskState.Running => Config.Mode switch
            {
                TriggerMode.Countdown =>
                    $"倒计时 · 剩余 {FormatRemain(_countdownTotalMs - _sw.Elapsed.TotalMilliseconds)} → {GetActionName()}",
                TriggerMode.Scheduled =>
                    $"定时 · {Config.ScheduledTime} {GetRepeatName()} → {GetActionName()}",
                TriggerMode.Idle =>
                    $"空闲监测 · {Config.IdleMinutes} 分钟 → {GetActionName()}（当前空闲 {(int)NativeMethods.GetIdleSeconds()}s）",
                _ => "运行中"
            },
            _ => ""
        };
        StatusChanged?.Invoke(text);
    }

    private static string FormatRemain(double ms)
    {
        if (ms < 0) ms = 0;
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes:00}:{ts.Seconds:00}";
    }

    public string GetActionName() => Config.Action switch
    {
        PowerAction.Shutdown => "关机",
        PowerAction.Restart => "重启",
        PowerAction.Sleep => "睡眠",
        PowerAction.Hibernate => "休眠",
        PowerAction.Logoff => "注销",
        PowerAction.Lock => "锁屏",
        PowerAction.MonitorOff => "关闭显示器",
        _ => "执行"
    };

    private string GetRepeatName() => Config.Repeat switch
    {
        RepeatType.Once => "一次性",
        RepeatType.Daily => "每天",
        RepeatType.Weekly => "每周",
        _ => ""
    };
}
