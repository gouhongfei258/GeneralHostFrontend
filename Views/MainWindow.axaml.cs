using Avalonia.Controls;
using Avalonia.Threading;
using System.Runtime.InteropServices;

namespace GeneralHostFrontend.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Opened += OnOpened;
        }

        private void OnOpened(object? sender, EventArgs e)
        {
            WindowState = WindowState.Normal;
            ShowInTaskbar = true;
            Position = new Avalonia.PixelPoint(80, 80);
            Topmost = true;
            Activate();

            var openedHandle = TryGetPlatformHandle();
            ForceWin32Visible(openedHandle?.Handle ?? IntPtr.Zero);
            StartupTrace.Write($"MainWindow forced visible. State={WindowState}; Position={Position}; Bounds={Bounds}; ShowInTaskbar={ShowInTaskbar}; PlatformHandle={openedHandle?.Handle.ToInt64() ?? 0}; Descriptor={openedHandle?.HandleDescriptor ?? "null"}.");

            Dispatcher.UIThread.Post(() =>
            {
                Topmost = false;
                Activate();
                var delayedHandle = TryGetPlatformHandle();
                ForceWin32Visible(delayedHandle?.Handle ?? IntPtr.Zero);
                StartupTrace.Write($"MainWindow topmost released. State={WindowState}; Position={Position}; Bounds={Bounds}; IsVisible={IsVisible}; PlatformHandle={delayedHandle?.Handle.ToInt64() ?? 0}; Descriptor={delayedHandle?.HandleDescriptor ?? "null"}.");
            }, DispatcherPriority.Background);
        }

        private static void ForceWin32Visible(IntPtr handle)
        {
            if (handle == IntPtr.Zero || !OperatingSystem.IsWindows())
            {
                return;
            }

            ShowWindow(handle, ShowWindowCommand.Restore);
            SetWindowPos(handle, IntPtr.Zero, 80, 80, 1180, 760, SetWindowPosFlags.ShowWindow);
            SetForegroundWindow(handle);
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, ShowWindowCommand nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, SetWindowPosFlags uFlags);

        private enum ShowWindowCommand
        {
            Restore = 9
        }

        [Flags]
        private enum SetWindowPosFlags
        {
            ShowWindow = 0x0040
        }
    }
}
