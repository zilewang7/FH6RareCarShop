using System.IO;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using FH6ItalianRarities;
using FH6ItalianRarities.Core;
using FH6ItalianRarities.Infrastructure;
using FH6ItalianRarities.Models;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try { return Run(); }
        catch (Exception exception)
        {
            Console.WriteLine("FAIL " + exception);
            return 1;
        }
    }

    private static int Run()
    {
        // Construct and render the window off screen. Do not start the application,
        // register a hotkey, open a game process, or send any real keyboard input.
        var app = new App();
        app.InitializeComponent();
        // Load from an absent test path rather than the user's saved preferences.
        var settings = new AutoPurchaseSettingsStore(Path.Combine(Path.GetTempPath(),
            "FH6RareCarShop.UiTests", Guid.NewGuid() + ".json"));
        var window = (MainWindow)Activator.CreateInstance(typeof(MainWindow),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [settings], null)!;
        try
        {
            T Control<T>(string name) where T : FrameworkElement =>
                window.FindName(name) as T ?? throw new Exception($"Missing automatic purchase control: {name}");
            void Check(bool condition, string message)
            {
                if (!condition) throw new Exception(message);
                Console.WriteLine("PASS " + message);
            }

            Check(Control<Button>("AutoStartButton").IsEnabled, "automatic purchase can be started from the main window");
            Check(!Control<Button>("AutoStopButton").IsEnabled, "stop is disabled when no script is running");
            Check(Control<CheckBox>("AutoOverlayCheckBox").IsChecked == true, "game overlay is enabled by default");
            Check(Control<TextBox>("AutoEnterDurationBox").Text == "9" &&
                Control<TextBox>("AutoPurchaseDelayBox").Text == "0" && Control<TextBox>("AutoRestoreDelayBox").Text == "0",
                "default timing uses nine seconds and zero waits");
            var read = typeof(MainWindow).GetMethod("ReadAutoOptions", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Control<TextBox>("AutoEnterDurationBox").Text = "9.5";
            var options = (AutoPurchaseOptions)read.Invoke(window, null)!;
            Check(options.EnterDuration == TimeSpan.FromSeconds(9.5), "fractional-second parameters parse correctly");
            Control<TextBox>("AutoEnterDurationBox").Text = "9";
            Control<TextBox>("AutoEnterIntervalBox").Text = "120";
            Control<TextBox>("AutoStartDelayBox").Text = "5";
            Control<TextBox>("AutoPurchaseDelayBox").Text = "0";
            Control<TextBox>("AutoRestoreDelayBox").Text = "0";
            Control<TextBox>("AutoMaxRoundsBox").Text = "10";
            var quantityOptions = (AutoPurchaseOptions)read.Invoke(window, null)!;
            Check(quantityOptions.MaxRounds == 10, "requested purchase quantity is passed to the loop");
            Control<CheckBox>("AutoOverlayCheckBox").IsChecked = false;
            var overlayDisabledOptions = (AutoPurchaseOptions)read.Invoke(window, null)!;
            Check(!(bool)typeof(AutoPurchaseOptions).GetProperty("ShowOverlay")!.GetValue(overlayDisabledOptions)!,
                "overlay can be disabled without changing purchase timing");
            Control<CheckBox>("AutoOverlayCheckBox").IsChecked = true;
            var formatRounds = typeof(MainWindow).GetMethod("FormatAutoRounds", BindingFlags.NonPublic | BindingFlags.Static)!;
            Check(((string)formatRounds.Invoke(null, [3, quantityOptions])!).Contains("3 / 10"), "progress shows the requested purchase quantity");
            Control<CheckBox>("AutoRepeatCheckBox").IsChecked = true;
            Control<TextBox>("AutoMaxRoundsBox").Text = "-1";
            try { read.Invoke(window, null); throw new Exception("Negative loop count was accepted."); }
            catch (TargetInvocationException exception) when (exception.InnerException is ArgumentException)
            {
                Console.WriteLine("PASS invalid parameters are rejected before startup");
            }
            Control<TextBox>("AutoMaxRoundsBox").Text = "0";
            foreach (var activity in CarCatalog.Activities)
            {
                window.SelectedActivity = activity;
                var slots = Control<ComboBox>("AutoSlotCombo");
                Check(slots.Items.Count == activity.SlotCount, $"{activity.DisplayName} offers only its available slots");
            }
            window.SelectedActivity = CarCatalog.DefaultActivity;
            Check(Control<TextBlock>("AutoTargetText").Text.Contains(window.SelectedSlot1!.ChineseName), "the selected vehicle is shown beside the script");
            var running = typeof(MainWindow).GetMethod("SetAutoRunning", BindingFlags.NonPublic | BindingFlags.Instance)!;
            running.Invoke(window, [true]);
            Check(Control<Button>("AutoStopButton").IsEnabled && !Control<Button>("AutoStartButton").IsEnabled, "running state leaves stop accessible and prevents a second start");
            Check(!Control<ComboBox>("ActivityCombo").IsEnabled && !Control<TabControl>("WorkspaceTabs").IsEnabled, "activity and vehicle selection are locked during automation");
            using (var cancellation = new CancellationTokenSource())
            {
                var cancellationField = typeof(MainWindow).GetField("_autoCancellation", BindingFlags.NonPublic | BindingFlags.Instance)!;
                cancellationField.SetValue(window, cancellation);
                var closing = new CancelEventArgs();
                typeof(MainWindow).GetMethod("OnClosing", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.Invoke(window, [window, closing]);
                Check(closing.Cancel && cancellation.IsCancellationRequested, "closing requests stop and waits for the active operation");
                cancellationField.SetValue(window, null);
                typeof(MainWindow).GetField("_autoCloseRequested", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, false);
            }
            running.Invoke(window, [false]);
            Control<TextBox>("AutoMaxRoundsBox").Text = "0";
            Control<TextBlock>("AutoRoundsText").Text = (string)formatRounds.Invoke(null, [0, new AutoPurchaseOptions()])!;
            Control<TextBlock>("AutoStatusText").Text = "选择下方目标车辆，站在游戏内购买位置后启动。F8 可随时停止；切出游戏会停止脚本。";
            Control<TextBlock>("FooterStatusText").Text = "就绪";

            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(1360, 907));
            root.Arrange(new Rect(0, 0, 1360, 907));
            root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1360, 907, 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var drawing = background.RenderOpen())
            {
                drawing.DrawRectangle(window.Background, null, new Rect(0, 0, 1360, 907));
            }
            bitmap.Render(background);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory("artifacts");
            using var output = File.Create("artifacts/auto-purchase-preview.png");
            encoder.Save(output);
            Console.WriteLine("PASS WPF main window rendered off screen");
            CheckOverlay(Check, window.SelectedSlot1!);
            var lingeringOverlay = new AutoPurchaseOverlayWindow(window.SelectedSlot1!, new AutoPurchaseOptions());
            var overlayHandle = new WindowInteropHelper(lingeringOverlay).EnsureHandle();
            typeof(MainWindow).GetField("_autoOverlay", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(window, lingeringOverlay);
            using (var cancellation = new CancellationTokenSource())
            {
                typeof(MainWindow).GetField("_autoCancellation", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(window, cancellation);
                typeof(MainWindow).GetMethod("StopAutoPurchase", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, null);
                Check(cancellation.IsCancellationRequested &&
                    ((TextBlock)lingeringOverlay.FindName("OverlayStatusText")).Text.Contains("正在停止"),
                    "main-window stop immediately updates the overlay while cleanup finishes");
                typeof(MainWindow).GetField("_autoCancellation", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(window, null);
            }
            window.Close();
            Check(!IsWindow(overlayHandle), "closing the main window also destroys the independent overlay");
            return 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine("FAIL " + exception.Message);
            return 1;
        }
        finally
        {
            typeof(MainWindow).GetField("_autoCancellation", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(window, null);
            typeof(MainWindow).GetField("_autoCloseRequested", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(window, false);
            typeof(MainWindow).GetMethod("SetAutoRunning", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(window, [false]);
            window.Close();
            app.Shutdown();
        }
    }

    private static void CheckOverlay(Action<bool, string> check, CarOption target)
    {
        var type = typeof(MainWindow).Assembly.GetType("FH6ItalianRarities.AutoPurchaseOverlayWindow")
            ?? throw new Exception("Missing game overlay window.");
        var options = new AutoPurchaseOptions { MaxRounds = 25 };
        var overlay = (Window)Activator.CreateInstance(type, [target, options])!;
        try
        {
            TextBlock Text(string name) => (TextBlock)(overlay.FindName(name)
                ?? throw new Exception("Missing overlay value: " + name));
            check(!overlay.ShowActivated && !overlay.ShowInTaskbar && !overlay.Focusable && overlay.Topmost,
                "overlay is topmost without activation, keyboard focus or a taskbar entry");
            check(overlay.AllowsTransparency && overlay.WindowStyle == WindowStyle.None && !overlay.IsHitTestVisible,
                "overlay is translucent and has no interactive controls");
            check(Text("OverlayVehicleText").Text.Contains(target.ChineseName), "overlay displays the locked target vehicle");
            check(Text("OverlayTargetText").Text == "25 辆", "overlay displays the requested quantity");
            check(Text("OverlayHotKeyText").Text.Contains("F8"), "overlay displays the global stop key");
            type.GetMethod("UpdateProgress")!.Invoke(overlay, [new AutoPurchaseProgress(AutoPurchaseStage.Restoring, 19, "正在恢复重购")]);
            check(Text("OverlayCompletedText").Text == "19 轮" && Text("OverlayStatusText").Text.Contains("恢复"),
                "overlay updates completed rounds and the active stage");
            var content = (FrameworkElement)overlay.Content;
            content.Measure(new Size(370, 280));
            content.Arrange(new Rect(0, 0, 370, 280));
            content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(370, 280, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var image = File.Create("artifacts/auto-purchase-overlay-preview.png")) encoder.Save(image);
            check(true, "game overlay rendered off screen");

            type.GetMethod("UpdateStatus")!.Invoke(overlay, ["正在停止，等待事务完成"]);
            check(Text("OverlayStatusText").Text.Contains("正在停止"), "overlay displays stop-in-progress immediately");
            type.GetMethod("Finish")!.Invoke(overlay, ["自动购买已停止", false]);
            type.GetMethod("UpdateProgress")!.Invoke(overlay, [new AutoPurchaseProgress(AutoPurchaseStage.Purchasing, 20, "stale progress")]);
            check(Text("OverlayCompletedText").Text == "19 轮" && Text("OverlayStatusText").Text == "自动购买已停止",
                "finished overlay cannot be overwritten by late progress");

            // Create only a hidden HWND to inspect native styles. Never show it or activate it.
            var handle = new WindowInteropHelper(overlay).EnsureHandle();
            var styles = GetWindowLongPtr(handle, -20).ToInt64();
            const long required = 0x08000000 | 0x00000020 | 0x00000080 | 0x00080000;
            check((styles & required) == required && (styles & 0x00040000) == 0,
                "native overlay styles prevent activation and pass mouse clicks through");
            check(Text("OverlayHotKeyText").Text == "已结束", "finished overlay no longer advertises an inactive stop key");

            var placement = type.GetMethod("CalculatePosition", BindingFlags.NonPublic | BindingFlags.Static)!;
            var coordinates = (ValueTuple<int, int>)placement.Invoke(null, [-1920, 100, 1920, 1080, 370, 280, 144u])!;
            check(coordinates.Item1 == -1884 && coordinates.Item2 == 136,
                "overlay placement handles negative monitor coordinates and DPI-scaled margins");

        }
        finally { overlay.Close(); }

        var infinite = (Window)Activator.CreateInstance(type, [target, new AutoPurchaseOptions()])!;
        try
        {
            check(((TextBlock)infinite.FindName("OverlayTargetText")).Text == "无限", "overlay labels unlimited mode correctly");
            var foreground = GetForegroundWindow();
            type.GetMethod("StartTracking")!.Invoke(infinite, [int.MaxValue]);
            check(!infinite.IsVisible && GetForegroundWindow() == foreground,
                "tracking keeps the overlay hidden when the game is not foreground and preserves focus");
        }
        finally { infinite.Close(); }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
}
