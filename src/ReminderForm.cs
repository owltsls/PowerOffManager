using System;
using System.Drawing;
using System.Windows.Forms;

namespace PowerOffManager;

/// <summary>倒计时提醒弹窗：立即执行 / 取消 / 推迟。可置顶、可拖动。</summary>
internal sealed class ReminderForm : Form
{
    private readonly Scheduler _scheduler;
    private readonly int _seconds;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly Label _lblCountdown;
    private readonly Label _lblInfo;
    private int _remaining;
    private bool _decided;
    private Point _dragOffset;

    public event Action<string> Decision; // "execute" / "cancel" / "snooze:N"

    public ReminderForm(Scheduler scheduler, int seconds)
    {
        _scheduler = scheduler;
        _seconds = seconds;
        _remaining = seconds;

        Text = "即将执行：" + scheduler.GetActionName();
        Size = new Size(460, 250);
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.White;
        Padding = Padding.Empty;

        var area = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(area.Right - Width - 24, area.Bottom - Height - 24);

        // 顶部标题条（可拖动）
        var bar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.FromArgb(30, 58, 138) };
        var barTitle = new Label
        {
            Text = "   ⏰  定时关机管家 · 执行提醒",
            ForeColor = Color.White, Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
        };
        bar.Controls.Add(barTitle);
        bar.MouseDown += Bar_MouseDown;
        bar.MouseMove += Bar_MouseMove;
        Controls.Add(bar);

        _lblInfo = new Label
        {
            Text = $"系统将在倒计时结束后执行【{scheduler.GetActionName()}】",
            Dock = DockStyle.Top, Height = 40, TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei", 10F), ForeColor = Color.FromArgb(55, 65, 81)
        };
        Controls.Add(_lblInfo);

        _lblCountdown = new Label
        {
            Text = _remaining.ToString(),
            Dock = DockStyle.Top, Height = 66, TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei", 30F, FontStyle.Bold),
            ForeColor = Color.FromArgb(37, 99, 235)
        };
        Controls.Add(_lblCountdown);

        if (NativeMethods.IsRemoteSession())
        {
            var warn = new Label
            {
                Text = "⚠ 检测到远程会话，执行会影响其他登录用户",
                Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei", 8F), ForeColor = Color.FromArgb(220, 38, 38)
            };
            Controls.Add(warn);
        }

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(12, 10, 12, 10), WrapContents = false
        };
        btnPanel.Controls.Add(MakeBtn("立即执行", Color.FromArgb(220, 38, 38), Color.White, 92, (s, e) => Decide("execute")));
        btnPanel.Controls.Add(MakeBtn("取消", Color.White, Color.FromArgb(55, 65, 81), 66, (s, e) => Decide("cancel"), true));
        btnPanel.Controls.Add(MakeBtn("推迟5分", Color.White, Color.FromArgb(55, 65, 81), 78, (s, e) => Decide("snooze:5"), true));
        btnPanel.Controls.Add(MakeBtn("推迟10分", Color.White, Color.FromArgb(55, 65, 81), 84, (s, e) => Decide("snooze:10"), true));
        btnPanel.Controls.Add(MakeBtn("推迟30分", Color.White, Color.FromArgb(55, 65, 81), 84, (s, e) => Decide("snooze:30"), true));
        Controls.Add(btnPanel);

        _timer.Tick += (s, e) =>
        {
            _remaining--;
            if (_remaining <= 0) { Decide("execute"); return; }
            _lblCountdown.Text = _remaining.ToString();
        };
    }

    private Button MakeBtn(string text, Color bg, Color fg, int width, EventHandler click, bool border = false)
    {
        var b = new Button
        {
            Text = text, Width = width, Height = 32, BackColor = bg, ForeColor = fg,
            FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei", 9F),
            Margin = new Padding(0, 0, 6, 0), Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = border ? Color.FromArgb(156, 163, 175) : bg;
        b.FlatAppearance.BorderSize = border ? 1 : 0;
        b.Click += click;
        return b;
    }

    private void Bar_MouseDown(object sender, MouseEventArgs e) =>
        _dragOffset = new Point(e.X, e.Y);

    private void Bar_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            Location = new Point(Cursor.Position.X - _dragOffset.X, Cursor.Position.Y - _dragOffset.Y);
    }

    private void Decide(string decision)
    {
        if (_decided) return;
        _decided = true;
        _timer.Stop();
        Decision?.Invoke(decision);
        Close();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _timer.Start();
        BringToFront();
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Stop();
        base.OnFormClosing(e);
    }
}
