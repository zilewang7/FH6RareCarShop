using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FH6ItalianRarities.Core;
using FH6ItalianRarities.Infrastructure;
using FH6ItalianRarities.Models;

namespace FH6ItalianRarities;

public partial class MainWindow
{
    private readonly AutoPurchaseSettingsStore _autoSettings;
    private CancellationTokenSource? _autoCancellation;
    private AutoPurchaseOverlayWindow? _autoOverlay;
    private bool _autoCloseRequested;

    private void InitializeAutoPurchase()
    {
        var options = _autoSettings.Load();
        AutoEnterDurationBox.Text = Format(options.EnterDuration.TotalSeconds);
        AutoEnterIntervalBox.Text = Format(options.EnterInterval.TotalMilliseconds);
        AutoStartDelayBox.Text = Format(options.StartDelay.TotalSeconds);
        AutoPurchaseDelayBox.Text = Format(options.PurchaseDelay.TotalSeconds);
        AutoRestoreDelayBox.Text = Format(options.RestoreDelay.TotalSeconds);
        AutoMaxRoundsBox.Text = options.MaxRounds.ToString(CultureInfo.InvariantCulture);
        AutoRepeatCheckBox.IsChecked = options.RepeatPurchase;
        AutoOverlayCheckBox.IsChecked = options.ShowOverlay;
        UpdateAutoSlots();
        AutoRepeat_Changed(this, new RoutedEventArgs());

        static string Format(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);
    }

    private void UpdateAutoSlots()
    {
        if (AutoSlotCombo is null) return;
        var previous = AutoSlotCombo.SelectedItem is int slot ? slot : 1;
        AutoSlotCombo.ItemsSource = Enumerable.Range(1, SelectedActivity.SlotCount).ToArray();
        AutoSlotCombo.SelectedItem = Math.Min(previous, SelectedActivity.SlotCount);
        UpdateAutoTarget();
    }

    private void UpdateAutoTarget()
    {
        if (AutoTargetText is null || AutoSlotCombo is null) return;
        var slot = AutoSlotCombo.SelectedItem is int selected ? selected : 1;
        var car = GetSelectedCar(slot);
        AutoTargetText.Text = car is null ? "请在下方为此展位选择车辆" : $"{car.Year} {car.ChineseName}";
        AutoTargetText.ToolTip = car?.OriginalName;
    }

    private void AutoSlot_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAutoTarget();

    private void AutoRepeat_Changed(object sender, RoutedEventArgs e)
    {
        if (AutoMaxRoundsBox is not null)
        {
            AutoMaxRoundsBox.IsEnabled = AutoRepeatCheckBox.IsChecked == true;
        }
    }

    private AutoPurchaseOptions ReadAutoOptions()
    {
        var repeat = AutoRepeatCheckBox.IsChecked == true;
        var rounds = 0;
        if (repeat && (!int.TryParse(AutoMaxRoundsBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out rounds) || rounds < 0))
        {
            throw new ArgumentException("购买数量须为非负整数；0 表示无限循环。");
        }
        var options = new AutoPurchaseOptions
        {
            EnterDuration = TimeSpan.FromSeconds(Number(AutoEnterDurationBox, "连按时长", 0.1, 120)),
            EnterInterval = TimeSpan.FromMilliseconds(Number(AutoEnterIntervalBox, "按键间隔", 60, 2000)),
            StartDelay = TimeSpan.FromSeconds(Number(AutoStartDelayBox, "启动倒计时", 0, 60)),
            PurchaseDelay = TimeSpan.FromSeconds(Number(AutoPurchaseDelayBox, "购买后等待", 0, 60)),
            RestoreDelay = TimeSpan.FromSeconds(Number(AutoRestoreDelayBox, "恢复后等待", 0, 60)),
            RepeatPurchase = repeat,
            MaxRounds = rounds,
            ShowOverlay = AutoOverlayCheckBox.IsChecked == true
        };
        options.Validate();
        return options;

        static double Number(TextBox box, string label, double minimum, double maximum)
        {
            if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) ||
                !double.IsFinite(number) || number < minimum || number > maximum)
            {
                throw new ArgumentException($"{label}须为 {minimum}–{maximum} 范围内的数字。");
            }
            return number;
        }
    }

    private async void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        ForzaGameInput? input = null;
        AutomationStopHotKey? hotKey = null;
        CancellationTokenSource? cancellation = null;
        var overlayError = false;
        try
        {
            var options = ReadAutoOptions();
            var slot = AutoSlotCombo.SelectedItem is int selected ? selected : 1;
            var target = GetSelectedCar(slot) ?? throw new ArgumentException("请先为目标展位选择车辆。");
            var process = _gameService.GetProcessStatus();
            if (!process.IsRunning || process.ProcessId is not int processId)
            {
                throw new GameToolException("请先启动游戏，并进入对应的奇珍商店购买位置。");
            }
            input = new ForzaGameInput(processId);
            hotKey = new AutomationStopHotKey(this, StopAutoPurchase);
            cancellation = new CancellationTokenSource();
            _autoCancellation = cancellation;
            SetAutoRunning(true);
            CloseAutoOverlay();
            if (options.ShowOverlay)
            {
                var overlay = new AutoPurchaseOverlayWindow(target, options);
                _autoOverlay = overlay;
                overlay.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_autoOverlay, overlay)) _autoOverlay = null;
                };
                overlay.StartTracking(processId);
            }
            AutoRoundsText.Text = FormatAutoRounds(0, options);
            AutoStatusText.Foreground = (System.Windows.Media.Brush)FindResource("ActionBrush");
            _lastProgressMessage = string.Empty;
            try { _autoSettings.Save(options); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLogger.Warn("Automatic purchase parameters could not be saved. " + exception.Message);
                AppendDiagnostic("参数保存失败，本次仍使用当前参数运行。");
            }
            AppendDiagnostic(
                $"自动购买启动：{target.OriginalName}，展位 {slot}，连按 {options.EnterDuration.TotalSeconds:0.##} 秒，" +
                $"间隔 {options.EnterInterval.TotalMilliseconds:0.##} 毫秒，" +
                (options.RepeatPurchase ? options.MaxRounds == 0 ? "无限循环。" : $"{options.MaxRounds} 轮。" : "单次购买。"));

            var session = cancellation;
            var gameInput = input;
            var gameProgress = new Progress<GameProgress>(value =>
            {
                if (!ReferenceEquals(_autoCancellation, session) || session.IsCancellationRequested) return;
                AutoStatusText.Text = value.Message;
                _autoOverlay?.UpdateStatus(value.Message);
                UpdateProgress(value);
            });
            var loopProgress = new Progress<AutoPurchaseProgress>(value =>
            {
                if (!ReferenceEquals(_autoCancellation, session)) return;
                AutoRoundsText.Text = FormatAutoRounds(value.CompletedRounds, options);
                _autoOverlay?.UpdateCount(value.CompletedRounds);
                if (session.IsCancellationRequested) return;
                AutoStatusText.Text = value.Message;
                FooterStatusText.Text = value.Message;
                _autoOverlay?.UpdateProgress(value);
            });
            var runner = new AutoPurchaseRunner(new AutoPurchaseActions(
                token => PrepareAutoPurchaseAsync(target, options, processId, gameInput, gameProgress, token),
                gameInput.EnsureGameForeground,
                gameInput.EnterDown,
                gameInput.EnterUp,
                token => RestoreAutoPurchaseAsync(target, gameProgress, token)));
            var completed = await runner.RunAsync(options, loopProgress, session.Token);
            AutoRoundsText.Text = FormatAutoRounds(completed, options);
            _autoOverlay?.UpdateCount(completed);
            AutoStatusText.Text = $"自动购买已完成，共执行 {completed} 轮。";
            AutoStatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            FooterStatusText.Text = AutoStatusText.Text;
            AppendDiagnostic(AutoStatusText.Text);
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        {
            AutoStatusText.Text = "自动购买已停止，当前操作已结束。";
            AutoStatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush");
            FooterStatusText.Text = AutoStatusText.Text;
            AppendDiagnostic(AutoStatusText.Text);
        }
        catch (Exception exception)
        {
            overlayError = true;
            var message = exception is GameToolException gameError ? gameError.UserMessage : exception.Message;
            AutoStatusText.Text = "自动购买已停止：" + message;
            AutoStatusText.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
            FooterStatusText.Text = "自动购买未完成";
            AppendDiagnostic(AutoStatusText.Text);
            AppLogger.Error("Automatic purchase stopped", exception);
            if (cancellation is null) ShowError(message);
        }
        finally
        {
            hotKey?.Dispose();
            try { input?.Dispose(); }
            catch (Exception exception)
            {
                overlayError = true;
                AppLogger.Error("Automatic purchase input cleanup failed", exception);
                AutoStatusText.Text = "停止时未能释放 Enter，请手动按下并松开 Enter。";
                AutoStatusText.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
                FooterStatusText.Text = AutoStatusText.Text;
            }
            if (cancellation is not null)
            {
                _autoOverlay?.Finish(AutoStatusText.Text, overlayError);
                _autoCancellation = null;
                cancellation.Dispose();
                SetAutoRunning(false);
                RefreshProcessStatus();
            }
            if (_autoCloseRequested)
            {
                _autoCloseRequested = false;
                CloseAutoOverlay();
                Close();
            }
        }
    }

    private async Task PrepareAutoPurchaseAsync(
        CarOption target,
        AutoPurchaseOptions options,
        int processId,
        ForzaGameInput input,
        IProgress<GameProgress> progress,
        CancellationToken cancellationToken)
    {
        var snapshot = await _gameService.InspectAsync(target.ActivityId, progress, cancellationToken);
        input.EnsureGameForeground();
        if (snapshot.ProcessId != processId) throw new GameToolException("连接的游戏进程发生变化，请重新启动脚本。");
        if (snapshot.SetterAddress == 0 || (options.RepeatPurchase && snapshot.RefreshAddress == 0))
        {
            throw new GameToolException("当前活动或版本未通过自动购买所需的切换/重购校验。");
        }
        ApplySnapshot(snapshot);
        var current = snapshot.Slots.Single(state => state.Slot == target.Slot);
        if (current.CurrentAftermarketId != target.AftermarketId || current.CurrentCarModelId != target.CarModelId)
        {
            var applied = await _gameService.ApplyAsync([target], progress, cancellationToken);
            ApplySnapshot(applied.Snapshot);
        }
        cancellationToken.ThrowIfCancellationRequested();
        input.EnsureGameForeground();
        PurchaseDiagnostic? diagnostic = null;
        try
        {
            diagnostic = await _gameService.DiagnosePurchaseAsync(target.ActivityId, target.Slot, progress, cancellationToken);
        }
        catch (GameToolException exception)
        {
            // A purchased booth may have unloaded its eligibility component.
            // The existing restore transaction validates and rebuilds it itself.
            AppLogger.Info("Initial purchase interaction unavailable; preparing restore. " + exception.Message);
        }
        cancellationToken.ThrowIfCancellationRequested();
        input.EnsureGameForeground();
        if (diagnostic is null || !diagnostic.IsEligible)
        {
            await RestoreAutoPurchaseAsync(target, progress, CancellationToken.None);
        }
        else
        {
            PurchaseDiagnosticText.Text = FormatPurchaseDiagnostic(diagnostic);
        }
        cancellationToken.ThrowIfCancellationRequested();
        // Also allow a newly selected/restored booth to finish loading before input.
        await Task.Delay(options.RestoreDelay, cancellationToken);
        input.EnsureGameForeground();
    }

    private async Task RestoreAutoPurchaseAsync(
        CarOption target,
        IProgress<GameProgress> progress,
        CancellationToken cancellationToken)
    {
        var result = await _gameService.RestorePurchaseAsync(target, progress, cancellationToken);
        PurchaseDiagnosticText.Text = FormatPurchaseDiagnostic(result.FinalDiagnostic);
        AppendDiagnostic($"自动恢复重购完成：展位 {target.Slot}，{target.OriginalName}，耗时 {result.Elapsed.TotalSeconds:0.0} 秒。");
    }

    private void SetAutoRunning(bool running)
    {
        _isBusy = running;
        ActivityCombo.IsEnabled = !running;
        WorkspaceTabs.IsEnabled = !running;
        InspectButton.IsEnabled = !running;
        AutoConfigurationPanel.IsEnabled = !running;
        AutoStartButton.IsEnabled = !running;
        AutoStopButton.IsEnabled = running;
    }

    private static string FormatAutoRounds(int completed, AutoPurchaseOptions options)
    {
        var limit = options.RepeatPurchase ? options.MaxRounds : 1;
        return limit == 0 ? $"已执行 {completed} 轮 · 无限循环" : $"已执行 {completed} / {limit} 轮";
    }

    private void AutoStop_Click(object sender, RoutedEventArgs e) => StopAutoPurchase();

    private void CloseAutoOverlay()
    {
        var overlay = _autoOverlay;
        _autoOverlay = null;
        overlay?.Close();
    }

    private void StopAutoPurchase()
    {
        if (_autoCancellation is null || _autoCancellation.IsCancellationRequested) return;
        _autoCancellation.Cancel();
        AutoStopButton.IsEnabled = false;
        AutoStatusText.Text = "正在停止：按键已中止，等待当前操作安全结束…";
        FooterStatusText.Text = AutoStatusText.Text;
        _autoOverlay?.UpdateStatus(AutoStatusText.Text);
    }
}
