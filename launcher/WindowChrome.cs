using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed partial class BuilderWindow
{
    private const int HitTest = 0x0084;
    private const int CalculateNonclient = 0x0083;
    private const int PaintNonclient = 0x0085;
    private const int ActivateNonclient = 0x0086;
    private const int GetMinMaxInfo = 0x0024;
    private const int NonclientLeftDown = 0x00A1;
    private const int Caption = 2;
    private const int Client = 1;

    private Control CreateWindowControls()
    {
        var row = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
            Padding = new Padding(0, 9, 0, 0), Margin = new Padding(0)
        };
        var close = new WindowButton("Close", WindowIcon.Close);
        var maximize = new WindowButton("Maximize", WindowIcon.Maximize);
        var minimize = new WindowButton("Minimize", WindowIcon.Minimize);
        row.Controls.AddRange(new Control[] { close, maximize, minimize });
        close.Click += (sender, e) => Close();
        maximize.Click += (sender, e) => ToggleMaximize();
        minimize.Click += (sender, e) => WindowState = FormWindowState.Minimized;
        Resize += (sender, e) => {
            bool maximized = WindowState == FormWindowState.Maximized;
            maximize.Icon = maximized ? WindowIcon.Restore : WindowIcon.Maximize;
            maximize.AccessibleName = maximized ? "Restore window" : "Maximize";
            maximize.Invalidate();
        };
        row.MouseDown += DragWindow;
        return row;
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
    }

    private void DragWindow(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (e.Clicks > 1) { ToggleMaximize(); return; }
        ReleaseCapture();
        SendMessage(Handle, NonclientLeftDown, new IntPtr(Caption), IntPtr.Zero);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            // Preserve normal resize, maximize, and taskbar behavior.
            parameters.Style &= ~0x00C00000; // WS_CAPTION
            parameters.Style |= 0x00040000 | 0x00020000 | 0x00010000 | 0x00080000;
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == CalculateNonclient || message.Msg == PaintNonclient)
        {
            message.Result = IntPtr.Zero;
            return;
        }
        if (message.Msg == ActivateNonclient)
        {
            // Keep Windows activation bookkeeping, suppressing its frame repaint.
            message.Result = DefWindowProc(message.HWnd, message.Msg, message.WParam, new IntPtr(-1));
            Invalidate(true);
            return;
        }
        base.WndProc(ref message);
        if (message.Msg == GetMinMaxInfo)
        {
            var monitor = Screen.FromHandle(message.HWnd);
            Rectangle work = monitor.WorkingArea;
            var limits = (WindowLimits)Marshal.PtrToStructure(message.LParam, typeof(WindowLimits));
            limits.MaxPosition = new Point(work.Left - monitor.Bounds.Left, work.Top - monitor.Bounds.Top);
            limits.MaxSize = work.Size;
            Marshal.StructureToPtr(limits, message.LParam, false);
        }
        if (message.Msg != HitTest || WindowState != FormWindowState.Normal ||
            message.Result.ToInt32() != Client) return;

        long packed = message.LParam.ToInt64();
        Point pointer = PointToClient(new Point((short)packed, (short)(packed >> 16)));
        const int grip = 7;
        bool left = pointer.X < grip, right = pointer.X >= ClientSize.Width - grip;
        bool top = pointer.Y < grip, bottom = pointer.Y >= ClientSize.Height - grip;
        int edge = top ? (left ? 13 : right ? 14 : 12) :
            bottom ? (left ? 16 : right ? 17 : 15) : left ? 10 : right ? 11 : Client;
        message.Result = new IntPtr(edge);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowLimits
    {
        internal Point Reserved;
        internal Size MaxSize;
        internal Point MaxPosition;
        internal Size MinTrackSize;
        internal Size MaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private enum WindowIcon { Minimize, Maximize, Restore, Close }

    private sealed class WindowButton : Button
    {
        internal WindowIcon Icon;
        private bool hovered;

        internal WindowButton(string name, WindowIcon icon)
        {
            Icon = icon;
            AccessibleName = name;
            Size = new Size(42, 35);
            Margin = new Padding(1, 0, 1, 0);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Background;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color background = hovered ? (Icon == WindowIcon.Close ? Color.FromArgb(132, 39, 32) : Surface) : Background;
            e.Graphics.Clear(background);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int x = Width / 2, y = Height / 2;
            using (var pen = new Pen(hovered ? Gold : Light, 1.5f))
            {
                switch (Icon)
                {
                    case WindowIcon.Minimize:
                        e.Graphics.DrawLine(pen, x - 6, y + 3, x + 6, y + 3);
                        break;
                    case WindowIcon.Maximize:
                        e.Graphics.DrawRectangle(pen, x - 5, y - 5, 10, 10);
                        break;
                    case WindowIcon.Restore:
                        e.Graphics.DrawLines(pen, new[] { new Point(x - 2, y - 4), new Point(x - 2, y - 7),
                            new Point(x + 7, y - 7), new Point(x + 7, y + 2), new Point(x + 4, y + 2) });
                        e.Graphics.DrawRectangle(pen, x - 6, y - 3, 9, 9);
                        break;
                    case WindowIcon.Close:
                        e.Graphics.DrawLine(pen, x - 5, y - 5, x + 5, y + 5);
                        e.Graphics.DrawLine(pen, x + 5, y - 5, x - 5, y + 5);
                        break;
                }
            }
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4));
        }
    }
}
