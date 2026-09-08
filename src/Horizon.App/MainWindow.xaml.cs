using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Horizon.App.ViewModels;
using Horizon.Core.Interfaces;
using Horizon.Core.Models;
using Forms = System.Windows.Forms;

namespace Horizon.App;

public partial class MainWindow : Window
{
    private readonly RootViewModel _root;
    private readonly IToastService _toasts;
    private readonly IDialogService _dialogs;
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ContextMenuStrip _trayMenu;
    private readonly DispatcherTimer _toastTimer = new();
    private bool _allowClose;
    private HwndSource? _windowSource;

    private Guid? _activeDialog;

    public MainWindow(RootViewModel root, IToastService toasts, IDialogService dialogs)
    {
        InitializeComponent();
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);
        _root = root;
        _toasts = toasts;
        _dialogs = dialogs;
        DataContext = root;
        toasts.ToastRequested += OnToastRequested;
        dialogs.DialogRequested += OnDialogRequested;
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastBorder.Visibility = Visibility.Collapsed; };
        StateChanged += Window_StateChanged;

        _trayMenu = new Forms.ContextMenuStrip();
        _trayMenu.Items.Add("Open Horizon", null, (_, _) => ShowFromTray());
        _trayMenu.Items.Add("Exit", null, (_, _) => ExitApplication());
        _tray = new Forms.NotifyIcon { Text = "Horizon", Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!), ContextMenuStrip = _trayMenu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowFromTray();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleMaximize(); return; }
        if (WindowState == WindowState.Maximized)
        {
            var cursor = e.GetPosition(this);
            var fraction = cursor.X / Math.Max(1, ActualWidth);
            WindowState = WindowState.Normal;
            Left = SystemParameters.WorkArea.Left + cursor.X - (ActualWidth * fraction);
            Top = Math.Max(SystemParameters.WorkArea.Top, cursor.Y - 20);
        }
        try { DragMove(); } catch (InvalidOperationException) { }
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => ExitApplication();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Window_StateChanged(object? sender, EventArgs e) => UpdateMaximizeButton();
    private void UpdateMaximizeButton()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "❐" : "□";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
        MaximizeButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, maximized ? "Restore Horizon" : "Maximize Horizon");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowSource = (HwndSource)PresentationSource.FromVisual(this);
        _windowSource.AddHook(WindowMessageHook);
    }

    private static IntPtr WindowMessageHook(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int getMinMaxInfo = 0x0024;
        if (message != getMinMaxInfo) return IntPtr.Zero;
        var monitor = MonitorFromWindow(windowHandle, 2);
        if (monitor == IntPtr.Zero) return IntPtr.Zero;
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo)) return IntPtr.Zero;
        var sizing = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        sizing.MaxPosition.X = monitorInfo.WorkArea.Left - monitorInfo.MonitorArea.Left;
        sizing.MaxPosition.Y = monitorInfo.WorkArea.Top - monitorInfo.MonitorArea.Top;
        sizing.MaxSize.X = monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left;
        sizing.MaxSize.Y = monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top;
        Marshal.StructureToPtr(sizing, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose && _root.Settings.MinimizeToTray) { e.Cancel = true; Hide(); return; }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _toasts.ToastRequested -= OnToastRequested;
        _dialogs.DialogRequested -= OnDialogRequested;
        StateChanged -= Window_StateChanged;
        _toastTimer.Stop();
        if (_windowSource is not null) _windowSource.RemoveHook(WindowMessageHook);
        _tray.Visible = false;
        _tray.Dispose();
        _trayMenu.Dispose();
        base.OnClosed(e);
    }

    private void ShowFromTray() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void ExitApplication() { _allowClose = true; Close(); Application.Current.Shutdown(); }

    private void OnToastRequested(object? sender, ToastMessage message)
    {
        RunOnUiThread(() =>
        {
            ToastText.Text = message.Message;
            ToastDot.Fill = message.Tone switch { StatusTone.Success => (Brush)FindResource("Brush.Success"), StatusTone.Warning => (Brush)FindResource("Brush.Warning"), StatusTone.Danger => (Brush)FindResource("Brush.Danger"), _ => (Brush)FindResource("Brush.TextSecondary") };
            ToastBorder.Visibility = Visibility.Visible;
            _toastTimer.Interval = message.Duration;
            _toastTimer.Stop(); _toastTimer.Start();
        });
    }

    private void OnDialogRequested(object? sender, DialogRequest request)
    {
        RunOnUiThread(() =>
        {
            _activeDialog = request.Id;
            DialogTitle.Text = request.Title;
            DialogMessage.Text = request.Message;
            DialogPrimary.Content = request.PrimaryAction;
            DialogSecondary.Content = request.SecondaryAction;
            DialogPrimary.Background = request.Tone == StatusTone.Danger ? (Brush)FindResource("Brush.Danger") : (Brush)FindResource("Brush.PrimaryAction");
            DialogPrimary.Foreground = request.Tone == StatusTone.Danger ? Brushes.White : (Brush)FindResource("Brush.PrimaryActionText");
            DialogOverlay.Visibility = Visibility.Visible;
        });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    private void RunOnUiThread(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else _ = Dispatcher.InvokeAsync(action, DispatcherPriority.DataBind);
    }

    private void DialogPrimary_Click(object sender, RoutedEventArgs e) => CompleteDialog(true);
    private void DialogSecondary_Click(object sender, RoutedEventArgs e) => CompleteDialog(false);
    private void CompleteDialog(bool accepted)
    {
        if (_activeDialog is not Guid id) return;
        _activeDialog = null;
        DialogOverlay.Visibility = Visibility.Collapsed;
        _dialogs.Complete(id, accepted);
    }
}
