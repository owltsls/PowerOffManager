using System;
using System.Drawing;
using System.Windows.Forms;

namespace PowerOffManager;

internal sealed class MainForm : Form
{
    private readonly AppConfig _cfg;
    private readonly Scheduler _scheduler;
    private bool _restoredTask;   // 启动时是否恢复了上次的定时任务
    private bool _forceExit;      // 真正退出时跳过「最小化到托盘」拦截

    // 控件（字段供配置读写与事件使用）
    private readonly TabControl _tabs = new();
    private readonly NumericUpDown _numMin = new(), _numSec = new();
    private readonly DateTimePicker _dtTime = new();
    private readonly ComboBox _cboRepeat = new();
    private readonly CheckBox[] _dayChecks = new CheckBox[7];
    private readonly NumericUpDown _numIdle = new();
    private readonly ComboBox _cboAction = new();
    private readonly CheckBox _chkForce = new();
    private readonly NumericUpDown _numRemind = new();
    private readonly Button _btnStart = new(), _btnCancel = new();
    private readonly Label _lblStatus = new();
    private readonly Panel _statusDot = new();
    private readonly NotifyIcon _tray = new();

    // 配色：统一冷蓝调（深蓝 → 亮蓝 → 淡蓝 → 白），联系方式与标题栏同色系
    private static readonly Color CPrimary = Color.FromArgb(30, 58, 138);      // 标题栏 深蓝
    private static readonly Color CAccent = Color.FromArgb(37, 99, 235);      // 主强调 亮蓝
    private static readonly Color CBg = Color.FromArgb(241, 245, 249);        // 页面底色
    private static readonly Color CHead = Color.FromArgb(239, 246, 255);      // 卡片头
    private static readonly Color CText = Color.FromArgb(30, 41, 59);         // 正文
    private static readonly Color CMuted = Color.FromArgb(100, 116, 139);     // 次要文字
    private static readonly Color CBorder = Color.FromArgb(226, 232, 240);
    private static readonly Color CCard = Color.White;
    private static readonly Color CContact = Color.FromArgb(191, 219, 254);   // 联系方式 淡蓝白

    private static readonly string[] ActionNames =
        { "关机 (Shutdown)", "重启 (Restart)", "睡眠 (Sleep)", "休眠 (Hibernate)",
          "注销 (Logoff)", "锁屏 (Lock)", "关闭显示器 (Monitor Off)" };
    private static readonly string[] DayNames = { "日", "一", "二", "三", "四", "五", "六" };
    private static readonly string[] RepeatNames = { "一次性", "每天", "每周" };

    public MainForm(bool startToTray = false)
    {
        if (startToTray) Logger.Info("后台启动模式：主界面初始不显示，仅托盘运行");
        _cfg = ConfigService.Load();
        ConfigService.EnsureAutoStartArgument();  // 修复旧版自启快捷方式（补 --autostart）
        _scheduler = new Scheduler(_cfg);

        Text = "准点 · 定时关机管家";
        Size = new Size(920, 660);
        MinimumSize = new Size(820, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Program.Icons.App;
        BackColor = CBg;

        BuildUi();
        LoadConfigToUi();

        _scheduler.StatusChanged += s => SafeInvoke(() =>
        {
            _lblStatus.Text = s;
            _statusDot.BackColor = s.Contains("空闲") || s.Contains("无任务")
                ? Color.FromArgb(156, 163, 175) : CAccent;
            _tray.Text = "准点定时关机\n" + s;
        });
        _scheduler.ReminderRequested += () => SafeInvoke(ShowReminder);
        _scheduler.ExecutionRequested += msg => SafeInvoke(() =>
            _tray.ShowBalloonTip(3000, "已执行", msg, ToolTipIcon.Info));
        _scheduler.TaskEnded += () => SafeInvoke(RefreshButtons);

        _tray.Icon = Program.Icons.App;
        _tray.Visible = true;

        _restoredTask = _cfg.TaskActive && _scheduler.TryRestore();

        RefreshButtons();
        _scheduler.UpdateConfig(_cfg);
    }

    // ============ 界面构建 ============
    private void BuildUi()
    {
        // 主网格：标题 / 内容 / 底部，三段互不重叠
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = CBg, Margin = Padding.Empty, Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        Controls.Add(root);

        // ---- 顶部标题 ----
        var header = new Panel { Dock = DockStyle.Fill, BackColor = CPrimary, Margin = Padding.Empty };
        var headGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, BackColor = Color.Transparent
        };
        headGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        headGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        headGrid.Controls.Add(new Label
        {
            Text = "准点 · 定时关机管家", Font = new Font("Microsoft YaHei", 16F, FontStyle.Bold),
            ForeColor = Color.White, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(22, 0, 0, 0)
        }, 0, 0);
        // 右上角：联系方式（微信号），单行垂直居中，避免被下方卡片遮挡
        headGrid.Controls.Add(new Label
        {
            Text = "联系方式 · 微信：TheSun_Also_Rises",
            Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold),
            ForeColor = CContact,
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(0, 0, 20, 0)
        }, 1, 0);
        header.Controls.Add(headGrid);
        root.Controls.Add(header, 0, 0);

        // ---- 内容区 ----
        var content = new Panel
        {
            Dock = DockStyle.Fill, BackColor = CBg,
            Padding = new Padding(18, 14, 18, 8), Margin = Padding.Empty
        };

        // 动作卡片（Dock=Top）——用 out body 取得内容面板
        var actionCard = MakeModeCard("✓", "执行动作", "选择任务到点后要执行的操作", out var actBody);
        actionCard.Dock = DockStyle.Top;
        actionCard.Height = 118;
        actBody.Padding = new Padding(12, 2, 12, 4);
        var actGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2,
            Padding = new Padding(2, 0, 2, 2), Margin = Padding.Empty, BackColor = Color.Transparent
        };
        actGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        actGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        actGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        actGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        actGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _cboAction.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboAction.Items.AddRange(ActionNames);
        _cboAction.SelectedIndex = 0;
        _cboAction.Dock = DockStyle.Top;
        _cboAction.Font = new Font("Microsoft YaHei", 10F);
        actGrid.Controls.Add(MakeFieldLabel("电源动作", "任务到点后执行"), 0, 0);
        actGrid.Controls.Add(MakeFieldLabel("高级选项", "按需勾选"), 1, 0);
        actGrid.Controls.Add(MakeFieldLabel("提前提醒", "30–300 秒"), 2, 0);
        actGrid.Controls.Add(_cboAction, 0, 1);

        var forcePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0) };
        _chkForce.Text = "强制关闭未响应程序";
        _chkForce.Font = new Font("Microsoft YaHei", 10F);
        _chkForce.ForeColor = CText;
        _chkForce.AutoSize = true;
        _chkForce.Location = new Point(2, 4);
        forcePanel.Controls.Add(_chkForce);
        actGrid.Controls.Add(forcePanel, 1, 1);

        var remindPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 2, 0, 0) };
        _numRemind.Minimum = 30; _numRemind.Maximum = 300; _numRemind.Value = 60;
        _numRemind.Width = 74; _numRemind.Font = new Font("Microsoft YaHei", 10F);
        remindPanel.Controls.Add(_numRemind);
        remindPanel.Controls.Add(new Label { Text = "秒", AutoSize = true, Font = new Font("Microsoft YaHei", 10F), ForeColor = CMuted, Padding = new Padding(0, 6, 0, 0) });
        actGrid.Controls.Add(remindPanel, 2, 1);
        actBody.Controls.Add(actGrid);

        // 模式页签（Dock=Fill，先添加，让动作卡片先停靠）
        _tabs.Dock = DockStyle.Fill;
        _tabs.Font = new Font("Microsoft YaHei", 10F);
        _tabs.Padding = new Point(14, 6);
        _tabs.Controls.Add(BuildCountdownTab());
        _tabs.Controls.Add(BuildScheduledTab());
        _tabs.Controls.Add(BuildIdleTab());
        content.Controls.Add(_tabs);
        content.Controls.Add(actionCard);
        root.Controls.Add(content, 0, 1);

        // ---- 底部 ----
        var footer = new Panel
        {
            Dock = DockStyle.Fill, BackColor = CCard,
            Padding = new Padding(18, 10, 18, 14), Margin = Padding.Empty
        };
        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = Padding.Empty
        };
        _btnStart.Text = "启动任务"; _btnStart.Size = new Size(132, 42);
        _btnStart.BackColor = CAccent; _btnStart.ForeColor = Color.White;
        _btnStart.FlatStyle = FlatStyle.Flat; _btnStart.Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold);
        _btnStart.FlatAppearance.BorderSize = 0; _btnStart.Cursor = Cursors.Hand;
        _btnStart.Click += BtnStart_Click;
        btnRow.Controls.Add(_btnStart);

        _btnCancel.Text = "取消任务"; _btnCancel.Size = new Size(116, 42);
        _btnCancel.BackColor = Color.White; _btnCancel.ForeColor = CText;
        _btnCancel.FlatStyle = FlatStyle.Flat; _btnCancel.Font = new Font("Microsoft YaHei", 11F);
        _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(156, 163, 175);
        _btnCancel.Cursor = Cursors.Hand;
        _btnCancel.Click += (s, e) => { _scheduler.Stop(); SaveUiToConfig(); };
        btnRow.Controls.Add(_btnCancel);

        var btnSettings = new Button
        {
            Text = "设置", Size = new Size(86, 42), BackColor = Color.White,
            FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei", 11F), Cursor = Cursors.Hand
        };
        btnSettings.FlatAppearance.BorderColor = Color.FromArgb(156, 163, 175);
        btnSettings.Click += (s, e) => ShowSettings();
        btnRow.Controls.Add(btnSettings);
        footer.Controls.Add(btnRow);

        var statusBar = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Color.FromArgb(243, 244, 246) };
        _statusDot.Size = new Size(12, 12);
        _statusDot.BackColor = Color.FromArgb(156, 163, 175);
        _statusDot.Location = new Point(14, 15);
        statusBar.Controls.Add(_statusDot);
        _lblStatus.Text = "空闲 · 当前无任务";
        _lblStatus.Font = new Font("Microsoft YaHei", 10F);
        _lblStatus.ForeColor = CText;
        _lblStatus.Location = new Point(36, 10);
        _lblStatus.Size = new Size(820, 24);
        statusBar.Controls.Add(_lblStatus);
        footer.Controls.Add(statusBar);
        root.Controls.Add(footer, 0, 2);

        BuildTray();
        FormClosing += MainForm_FormClosing;
    }

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开主界面", null, (s, e) => RestoreFromTray());
        menu.Items.Add("查看状态", null, (s, e) => _tray.ShowBalloonTip(3000, "当前状态", _lblStatus.Text, ToolTipIcon.Info));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("取消当前任务", null, (s, e) => _scheduler.Stop());
        menu.Items.Add("暂停提醒 1 小时", null, (s, e) => PauseOneHour());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("设置…", null, (s, e) => ShowSettings());
        menu.Items.Add("退出", null, (s, e) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (s, e) => RestoreFromTray();
    }

    /// <summary>从托盘唤出主界面（开机自启后主界面初始不显示）。</summary>
    private void RestoreFromTray()
    {
        if (IsDisposed) return;
        try
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }
        catch (Exception ex) { Logger.Warn("唤出主界面失败：" + ex.Message); }
    }

    /// <summary>开机自启时由 Program 在消息循环启动后调用，给出托盘提示。</summary>
    public void NotifyAutoStarted()
    {
        try
        {
            string tip = _restoredTask
                ? "上次设置的定时任务已恢复，双击托盘图标可查看"
                : "双击托盘图标可打开主界面";
            _tray.ShowBalloonTip(3000, "已在后台运行", tip, ToolTipIcon.Info);
        }
        catch (Exception ex) { Logger.Warn("托盘提示失败：" + ex.Message); }
    }

    // ---- 卡片 / 表单辅助 ----
    private static Panel MakeCard(string number, string title, string subtitle)
    {
        var card = new Panel { BackColor = CCard, BorderStyle = BorderStyle.FixedSingle, Padding = Padding.Empty };
        var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(14, 4, 14, 6) };
        card.Controls.Add(body);
        var head = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = CHead };
        var badge = new Label
        {
            Text = number, Font = new Font("Microsoft YaHei", 12F, FontStyle.Bold),
            ForeColor = Color.White, BackColor = CAccent, TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(28, 28), Location = new Point(14, 10)
        };
        head.Controls.Add(badge);
        head.Controls.Add(new Label
        {
            Text = title, Font = new Font("Microsoft YaHei", 12F, FontStyle.Bold),
            ForeColor = CText, Location = new Point(52, 6), Size = new Size(240, 22),
            TextAlign = ContentAlignment.MiddleLeft
        });
        head.Controls.Add(new Label
        {
            Text = subtitle, Font = new Font("Microsoft YaHei", 9F),
            ForeColor = CMuted, Location = new Point(52, 26), Size = new Size(500, 18),
            TextAlign = ContentAlignment.MiddleLeft
        });
        card.Controls.Add(head);
        return card;
    }

    /// <summary>返回一个卡片，并把内容面板通过 out 传出供调用方填充。</summary>
    private static Panel MakeModeCard(string number, string title, string subtitle, out Panel body)
    {
        var card = MakeCard(number, title, subtitle);
        body = card.Controls[0] as Panel ?? new Panel();
        body.Padding = new Padding(22, 16, 22, 12);
        return card;
    }

    private static Label MakeFieldLabel(string main, string hint)
    {
        // 单行主标签，副文字放在卡片副标题区，避免行高不足导致文字重叠
        return new Label
        {
            Text = main, Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold),
            ForeColor = CText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static TableLayoutPanel NewForm(int rows)
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 2, RowCount = rows,
            AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 4)
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < rows; i++)
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        return t;
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text, Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold),
        ForeColor = CText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
    };

    private static Label HintLabel(string text) => new()
    {
        Text = text, Font = new Font("Microsoft YaHei", 9F),
        ForeColor = CMuted, AutoSize = true, MaximumSize = new Size(640, 0)
    };

    private static void SetNumericStyle(params NumericUpDown[] nums)
    {
        foreach (var n in nums)
        {
            n.Font = new Font("Microsoft YaHei", 12F);
            n.Height = 34;
            n.Width = 120;
        }
    }

    private TabPage NewModePage(string tabText, string number, string title, string subtitle)
    {
        var page = new TabPage { Text = tabText, BackColor = CBg, Padding = new Padding(12) };
        var card = MakeModeCard(number, title, subtitle, out var body);
        card.Dock = DockStyle.Fill;
        page.Controls.Add(card);
        _modeBodies[page] = body;
        return page;
    }

    private readonly System.Collections.Generic.Dictionary<TabPage, Panel> _modeBodies = new();

    private TabPage BuildCountdownTab()
    {
        var page = NewModePage("⏱ 倒计时", "①", "倒计时模式", "设定时长，到点后自动执行所选动作");
        var body = _modeBodies[page];
        SetNumericStyle(_numMin, _numSec);
        _numMin.Minimum = 0; _numMin.Maximum = 1440; _numMin.Value = 10;
        _numSec.Minimum = 0; _numSec.Maximum = 59; _numSec.Value = 0;

        var form = NewForm(3);
        form.Controls.Add(FieldLabel("倒计时时长"), 0, 0);
        var numPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 4, 0, 0) };
        numPanel.Controls.Add(_numMin);
        numPanel.Controls.Add(new Label { Text = "分", AutoSize = true, Font = new Font("Microsoft YaHei", 11F), ForeColor = CMuted, Padding = new Padding(2, 8, 10, 0) });
        numPanel.Controls.Add(_numSec);
        numPanel.Controls.Add(new Label { Text = "秒", AutoSize = true, Font = new Font("Microsoft YaHei", 11F), ForeColor = CMuted, Padding = new Padding(2, 8, 0, 0) });
        form.Controls.Add(numPanel, 1, 0);

        form.Controls.Add(FieldLabel("快捷设置"), 0, 1);
        var quick = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 4, 0, 0) };
        quick.Controls.Add(QuickBtn("15 分钟", 15, 0));
        quick.Controls.Add(QuickBtn("30 分钟", 30, 0));
        quick.Controls.Add(QuickBtn("1 小时", 60, 0));
        quick.Controls.Add(QuickBtn("2 小时", 120, 0));
        form.Controls.Add(quick, 1, 1);

        form.Controls.Add(new Label(), 0, 2);
        form.Controls.Add(HintLabel("范围 0–1440 分钟（最长 24 小时）；倒计时期间任何操作都不会中断计时。"), 1, 2);
        body.Controls.Add(form);
        return page;
    }

    private Button QuickBtn(string text, int min, int sec)
    {
        var b = new Button
        {
            Text = text, Size = new Size(84, 32), FlatStyle = FlatStyle.Flat,
            BackColor = Color.White, ForeColor = CAccent, Font = new Font("Microsoft YaHei", 10F),
            Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0)
        };
        b.FlatAppearance.BorderColor = CAccent;
        b.Click += (s, e) => { _numMin.Value = min; _numSec.Value = sec; };
        return b;
    }

    private TabPage BuildScheduledTab()
    {
        var page = NewModePage("🕒 时间点/周期", "②", "时间点 / 周期模式", "在指定时刻执行，可按每天或每周重复");
        var body = _modeBodies[page];

        _dtTime.Format = DateTimePickerFormat.Time;
        _dtTime.ShowUpDown = true;
        _dtTime.Font = new Font("Microsoft YaHei", 12F);
        _dtTime.Height = 34; _dtTime.Width = 140;
        _cboRepeat.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboRepeat.Items.AddRange(RepeatNames);
        _cboRepeat.SelectedIndex = 0;
        _cboRepeat.Font = new Font("Microsoft YaHei", 11F);
        _cboRepeat.Height = 34; _cboRepeat.Width = 120;

        var form = NewForm(4);
        form.Controls.Add(FieldLabel("执行时刻"), 0, 0);
        var t1 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 4, 0, 0) };
        t1.Controls.Add(_dtTime);
        t1.Controls.Add(new Label { Text = "（本地时间 24 小时制）", AutoSize = true, ForeColor = CMuted, Font = new Font("Microsoft YaHei", 9F), Padding = new Padding(0, 10, 0, 0) });
        form.Controls.Add(t1, 1, 0);

        form.Controls.Add(FieldLabel("重复方式"), 0, 1);
        form.Controls.Add(_cboRepeat, 1, 1);

        form.Controls.Add(FieldLabel("选择星期"), 0, 2);
        var days = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 6, 0, 0) };
        for (int i = 0; i < 7; i++)
        {
            _dayChecks[i] = new CheckBox
            {
                Text = "周" + DayNames[i], AutoSize = true, Font = new Font("Microsoft YaHei", 10F),
                ForeColor = CText, Margin = new Padding(0, 0, 12, 0)
            };
            if (i == 0 || i == 6) _dayChecks[i].Checked = false;
            else _dayChecks[i].Checked = true;
            days.Controls.Add(_dayChecks[i]);
        }
        form.Controls.Add(days, 1, 2);
        form.Controls.Add(new Label(), 0, 3);
        form.Controls.Add(HintLabel("提示：选择「每周」时，上面的星期选择生效；一次性任务若当天时刻已过则顺延到次日。"), 1, 3);
        body.Controls.Add(form);
        return page;
    }

    private TabPage BuildIdleTab()
    {
        var page = NewModePage("💤 空闲检测", "③", "空闲检测模式", "无键鼠操作达到设定时长后自动执行");
        var body = _modeBodies[page];
        SetNumericStyle(_numIdle);
        _numIdle.Minimum = 1; _numIdle.Maximum = 240; _numIdle.Value = 15;

        var form = NewForm(2);
        form.Controls.Add(FieldLabel("空闲时长"), 0, 0);
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 4, 0, 0) };
        p.Controls.Add(_numIdle);
        p.Controls.Add(new Label { Text = "分钟", AutoSize = true, Font = new Font("Microsoft YaHei", 11F), ForeColor = CMuted, Padding = new Padding(2, 8, 0, 0) });
        form.Controls.Add(p, 1, 0);
        form.Controls.Add(new Label(), 0, 1);
        form.Controls.Add(HintLabel("范围 1–240 分钟。任何键盘或鼠标操作都会重置空闲计时；状态栏会实时显示当前空闲秒数。"), 1, 1);
        body.Controls.Add(form);
        return page;
    }

    // ============ 业务逻辑（保持原样） ============
    private void BtnStart_Click(object sender, EventArgs e)
    {
        SaveUiToConfig();
        if (_cfg.Mode == TriggerMode.Countdown && _cfg.CountdownMinutes == 0 && _cfg.CountdownSeconds == 0)
        {
            MessageBox.Show("倒计时时长不能为 0。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _scheduler.Start();
        RefreshButtons();
        if (_cfg.MinimizeToTray)
        {
            WindowState = FormWindowState.Minimized;
            Hide();
            _tray.ShowBalloonTip(2500, "任务已启动", "程序已最小化到托盘运行", ToolTipIcon.Info);
        }
    }

    private void ShowReminder()
    {
        if (IsDisposed) return;
        var form = new ReminderForm(_scheduler, Math.Max(5, _cfg.ReminderSeconds));
        form.Decision += decision =>
        {
            if (decision == "execute")
            {
                // 走调度器统一幂等入口：状态切换 + 后台线程异步执行，绝不阻塞 UI 线程
                _scheduler.ExecuteNow();
                Logger.Info($"提醒中用户选择立即执行：{_cfg.Action}");
            }
            else if (decision == "cancel")
            {
                _scheduler.Stop();
                _tray.ShowBalloonTip(2500, "已取消", "定时任务已取消", ToolTipIcon.Info);
            }
            else if (decision.StartsWith("snooze:"))
            {
                int mins = int.Parse(decision.Split(':')[1]);
                _scheduler.Snooze(mins);
                _tray.ShowBalloonTip(2500, "已推迟", $"将在 {mins} 分钟后再次提醒", ToolTipIcon.Info);
            }
        };
        form.Show(this);
    }

    private void PauseOneHour()
    {
        if (!_scheduler.IsRunning) return;
        _scheduler.Snooze(60);
        _tray.ShowBalloonTip(2500, "已暂停", "已推迟 60 分钟", ToolTipIcon.Info);
    }

    private void ShowSettings()
    {
        using var dlg = new SettingsDialog(_cfg);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            ConfigService.SetStartWithWindows(_cfg.StartWithWindows);
            ConfigService.Save(_cfg);
        }
    }

    private void RefreshButtons()
    {
        bool running = _scheduler.IsRunning;
        _btnStart.Enabled = !running;
        _btnCancel.Enabled = running;
        _btnStart.Text = running ? "任务运行中" : "启动任务";
    }

    private void SaveUiToConfig()
    {
        _cfg.Mode = (TriggerMode)_tabs.SelectedIndex;
        _cfg.CountdownMinutes = (int)_numMin.Value;
        _cfg.CountdownSeconds = (int)_numSec.Value;
        _cfg.ScheduledTime = _dtTime.Value.ToString("HH:mm");
        _cfg.Repeat = (RepeatType)_cboRepeat.SelectedIndex;
        for (int i = 0; i < 7; i++) _cfg.Weekdays[i] = _dayChecks[i].Checked;
        _cfg.IdleMinutes = (int)_numIdle.Value;
        _cfg.Action = (PowerAction)_cboAction.SelectedIndex;
        _cfg.Force = _chkForce.Checked;
        _cfg.ReminderSeconds = (int)_numRemind.Value;
        ConfigService.Save(_cfg);
        _scheduler.UpdateConfig(_cfg);
    }

    private void LoadConfigToUi()
    {
        _tabs.SelectedIndex = Math.Min(_tabs.TabCount - 1, Math.Max(0, (int)_cfg.Mode));
        _numMin.Value = Math.Min(_numMin.Maximum, Math.Max(_numMin.Minimum, _cfg.CountdownMinutes));
        _numSec.Value = Math.Min(_numSec.Maximum, Math.Max(_numSec.Minimum, _cfg.CountdownSeconds));
        if (TimeSpan.TryParse(_cfg.ScheduledTime, out var ts)) _dtTime.Value = DateTime.Today.Add(ts);
        _cboRepeat.SelectedIndex = Math.Min(2, Math.Max(0, (int)_cfg.Repeat));
        if (_cfg.Weekdays != null && _cfg.Weekdays.Length == 7)
            for (int i = 0; i < 7; i++) _dayChecks[i].Checked = _cfg.Weekdays[i];
        _numIdle.Value = Math.Min(_numIdle.Maximum, Math.Max(_numIdle.Minimum, _cfg.IdleMinutes));
        _cboAction.SelectedIndex = Math.Min(ActionNames.Length - 1, Math.Max(0, (int)_cfg.Action));
        _chkForce.Checked = _cfg.Force;
        _numRemind.Value = Math.Min(_numRemind.Maximum, Math.Max(_numRemind.Minimum, _cfg.ReminderSeconds));
    }

    private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (_forceExit) return;  // 托盘菜单「退出」：不要被最小化逻辑拦截
        if (e.CloseReason == CloseReason.UserClosing && _cfg.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            _tray.ShowBalloonTip(2000, "已最小化到托盘", "双击托盘图标可重新打开", ToolTipIcon.Info);
        }
    }

    private void ExitApp()
    {
        _forceExit = true;
        try { SaveUiToConfig(); } catch (Exception ex) { Logger.Warn("退出前保存配置失败：" + ex.Message); }
        _tray.Visible = false;
        Application.Exit();
    }

    /// <summary>
    /// 线程封送：事件来自 System.Timers.Timer 的线程池线程，
    /// 必须通过 BeginInvoke 切回 UI 线程操作控件，否则会假死/抛跨线程异常。
    /// </summary>
    private void SafeInvoke(Action a)
    {
        try
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(a);
            else a();
        }
        catch (Exception ex) { Logger.Warn("UI 封送失败：" + ex.Message); }
    }
}

/// <summary>设置对话框（网格布局）。</summary>
internal sealed class SettingsDialog : Form
{
    private readonly AppConfig _cfg;
    private readonly CheckBox _chkAutostart = new(), _chkLog = new(), _chkTray = new();

    public SettingsDialog(AppConfig cfg)
    {
        _cfg = cfg;
        Text = "设置"; Size = new Size(480, 360); FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent; Icon = Program.Icons.App;
        MinimizeBox = MaximizeBox = false; BackColor = Color.White; Padding = new Padding(20);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        var g = new GroupBox { Text = "常规设置", Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei", 10F), Padding = new Padding(16, 26, 16, 8) };
        _chkAutostart.Text = "开机自动启动（写入启动文件夹快捷方式）";
        _chkAutostart.Checked = cfg.StartWithWindows; _chkAutostart.AutoSize = true; _chkAutostart.Location = new Point(16, 32);
        _chkTray.Text = "关闭窗口时最小化到托盘";
        _chkTray.Checked = cfg.MinimizeToTray; _chkTray.AutoSize = true; _chkTray.Location = new Point(16, 68);
        _chkLog.Text = "启用操作日志";
        _chkLog.Checked = cfg.EnableLog; _chkLog.AutoSize = true; _chkLog.Location = new Point(16, 104);
        g.Controls.Add(_chkAutostart); g.Controls.Add(_chkTray); g.Controls.Add(_chkLog);
        grid.Controls.Add(g, 0, 0);

        var info = new Label
        {
            Text = $"配置文件：{ConfigService.ConfigPath}\n日志目录：{Logger.ExportPath}\n版本：1.0.0 · 绿色单文件",
            Dock = DockStyle.Fill, ForeColor = Color.FromArgb(100, 116, 139), Font = new Font("Microsoft YaHei", 9F),
            Padding = new Padding(4, 8, 0, 0)
        };
        grid.Controls.Add(info, 0, 1);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "保存", DialogResult = DialogResult.OK, Size = new Size(92, 36), Font = new Font("Microsoft YaHei", 10F) };
        ok.Click += (s, e) =>
        {
            cfg.StartWithWindows = _chkAutostart.Checked;
            cfg.MinimizeToTray = _chkTray.Checked;
            cfg.EnableLog = _chkLog.Checked;
        };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Size = new Size(92, 36), Font = new Font("Microsoft YaHei", 10F) };
        btnPanel.Controls.Add(ok); btnPanel.Controls.Add(cancel);
        grid.Controls.Add(btnPanel, 0, 2);

        Controls.Add(grid);
        AcceptButton = ok; CancelButton = cancel;
    }
}
