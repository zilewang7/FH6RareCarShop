using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using FH6ItalianRarities.Core;
using FH6ItalianRarities.Infrastructure;
using FH6ItalianRarities.Models;

namespace FH6ItalianRarities;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ForzaGameService _gameService = new();
    private readonly DispatcherTimer _processTimer;
    private readonly StringBuilder _diagnosticLog = new();
    private RareCarActivity _selectedActivity = CarCatalog.DefaultActivity;
    private CarOption? _selectedSlot1;
    private CarOption? _selectedSlot2;
    private CarOption? _selectedSlot3;
    private GameSnapshot? _lastSnapshot;
    private bool _isBusy;
    private string _lastProgressMessage = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        var version = GetType().Assembly.GetName().Version;
        AppVersionText.Text = version is null
            ? "x64"
            : $"v{version.Major}.{version.Minor}.{version.Build}  ·  x64";

        Slot1View = CreateView(1);
        Slot2View = CreateView(2);
        Slot3View = CreateView(3);
        SetDefaultSelections();
        DataContext = this;
        UpdateActivityPresentation();

        _processTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _processTimer.Tick += (_, _) => RefreshProcessStatus();

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _processTimer.Stop();
            _gameService.Dispose();
        };
        RefreshFilters();
        AppendDiagnostic(
            $"车辆目录自检通过：{CarCatalog.Activities.Count} 个活动、" +
            $"{CarCatalog.All.Count} 辆已验证车辆；默认选择 {SelectedActivity.DisplayName}。");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<RareCarActivity> Activities { get; } =
        CarCatalog.Activities.OrderByDescending(activity => activity.FirstAvailableFrom).ToArray();

    public RareCarActivity SelectedActivity
    {
        get => _selectedActivity;
        set
        {
            if (value is null || EqualityComparer<RareCarActivity>.Default.Equals(_selectedActivity, value))
            {
                return;
            }
            _selectedActivity = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedActivity)));
            ActivateActivity();
        }
    }

    public ICollectionView Slot1View { get; private set; }
    public ICollectionView Slot2View { get; private set; }
    public ICollectionView Slot3View { get; private set; }

    public CarOption? SelectedSlot1
    {
        get => _selectedSlot1;
        set => SetField(ref _selectedSlot1, value);
    }

    public CarOption? SelectedSlot2
    {
        get => _selectedSlot2;
        set => SetField(ref _selectedSlot2, value);
    }

    public CarOption? SelectedSlot3
    {
        get => _selectedSlot3;
        set => SetField(ref _selectedSlot3, value);
    }

    private ListCollectionView CreateView(int slot)
    {
        var cars = slot <= SelectedActivity.SlotCount
            ? CarCatalog.ForSlot(SelectedActivity.Id, slot)
            : [];
        var view = new ListCollectionView(cars.ToList());
        view.Filter = FilterCar;
        return view;
    }

    private void ActivateActivity()
    {
        Slot1View = CreateView(1);
        Slot2View = CreateView(2);
        Slot3View = CreateView(3);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Slot1View)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Slot2View)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Slot3View)));
        SetDefaultSelections();
        _lastSnapshot = null;
        ResetSnapshotPresentation();
        UpdateActivityPresentation();
        RefreshFilters();
        AppendDiagnostic(
            $"已选择 {SelectedActivity.DisplayName}；工具只会接受与该活动完整车池匹配的游戏内展位。");
    }

    private void SetDefaultSelections()
    {
        SelectedSlot1 = FindDefaultCar(1);
        SelectedSlot2 = FindDefaultCar(2);
        SelectedSlot3 = SelectedActivity.SlotCount >= 3 ? FindDefaultCar(3) : null;
    }

    private CarOption? FindDefaultCar(int slot)
    {
        var preferredId = (SelectedActivity.Id, slot) switch
        {
            (CarCatalog.ItalianActivityId, 1) => 157,
            (CarCatalog.ItalianActivityId, 2) => 160,
            (CarCatalog.ItalianActivityId, 3) => 156,
            (CarCatalog.BritishActivityId, 1) => 200,
            (CarCatalog.BritishActivityId, 2) => 199,
            _ => -1
        };
        var cars = CarCatalog.ForSlot(SelectedActivity.Id, slot);
        return cars.FirstOrDefault(car => car.AftermarketId == preferredId) ?? cars.FirstOrDefault();
    }

    private void UpdateActivityPresentation()
    {
        HeaderSubtitleText.Text =
            $"{SelectedActivity.DisplayName}  ·  {SelectedActivity.SlotCount} 个展位  ·  " +
            $"{CarCatalog.ForActivity(SelectedActivity.Id).Count} 辆已验证车辆";
        ActivityPeriodText.Text = SelectedActivity.FirstAvailabilityText;
        ApplyAllText.Text = $"应用{FormatSlotCount(SelectedActivity.SlotCount)}展位";
        CatalogDiagnosticText.Text =
            $"{SelectedActivity.DisplayName} / {CarCatalog.ForActivity(SelectedActivity.Id).Count} 辆 / 校验通过";

        var slotCounts = Enumerable.Range(1, SelectedActivity.SlotCount)
            .ToDictionary(slot => slot, slot => CarCatalog.ForSlot(SelectedActivity.Id, slot).Count);
        Slot1CountText.Text = $"{slotCounts[1]} 辆";
        Slot2CountText.Text = $"{slotCounts[2]} 辆";
        if (SelectedActivity.SlotCount >= 3)
        {
            Slot3CountText.Text = $"{slotCounts[3]} 辆";
        }

        var thirdSlotVisibility = SelectedActivity.SlotCount >= 3
            ? Visibility.Visible
            : Visibility.Collapsed;
        Slot3Panel.Visibility = thirdSlotVisibility;
        DiagnosticSlot3Item.Visibility = thirdSlotVisibility;
        Slot3SpacerColumn.Width = SelectedActivity.SlotCount >= 3
            ? new GridLength(12)
            : new GridLength(0);
        Slot3Column.Width = SelectedActivity.SlotCount >= 3
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        if (DiagnosticSlotCombo.SelectedIndex >= SelectedActivity.SlotCount)
        {
            DiagnosticSlotCombo.SelectedIndex = 0;
        }
    }

    private void ResetSnapshotPresentation()
    {
        Slot1CurrentText.Text = "当前：等待扫描";
        Slot2CurrentText.Text = "当前：等待扫描";
        Slot3CurrentText.Text = "当前：等待扫描";
        ManagerDiagnosticText.Text = "尚未扫描";
        SetterDiagnosticText.Text = "尚未扫描";
        RefreshDiagnosticText.Text = "尚未扫描";
        CompatibilitySummaryText.Text = "尚未执行兼容性扫描";
        CompatibilitySummaryText.Foreground = (Brush)FindResource("MutedBrush");
        PurchaseDiagnosticText.Text = "尚未读取";
        SnapshotGrid.ItemsSource = null;
    }

    private static string FormatSlotCount(int count) => count switch
    {
        2 => "两个",
        3 => "三个",
        _ => count.ToString(CultureInfo.InvariantCulture) + "个"
    };

    private bool FilterCar(object item)
    {
        if (item is not CarOption car)
        {
            return false;
        }
        var query = SearchBox?.Text ?? string.Empty;
        var limitedOnly = LimitedFilter?.IsChecked == true;
        var recommendedOnly = RecommendedFilter?.IsChecked == true;
        return car.Matches(query) &&
               (!limitedOnly || car.IsLimited) &&
               (!recommendedOnly || car.IsRecommended);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshProcessStatus();
        _processTimer.Start();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_isBusy)
        {
            return;
        }

        e.Cancel = true;
        MessageBox.Show(
            this,
            "游戏内事务仍在执行。请等待当前操作结束；直接退出游戏可以终止游戏侧线程。",
            "操作尚未结束",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void RefreshProcessStatus()
    {
        if (_isBusy)
        {
            return;
        }

        var status = _gameService.GetProcessStatus();
        if (status.IsRunning)
        {
            ProcessStatusDot.Fill = (Brush)FindResource("SuccessBrush");
            ProcessStatusText.Text = $"已检测到 Forza Horizon 6  ·  PID {status.ProcessId}";
            var version = string.IsNullOrWhiteSpace(status.Version) ? "版本待扫描" : status.Version;
            ProcessDetailText.Text = version;
        }
        else
        {
            ProcessStatusDot.Fill = new SolidColorBrush(Color.FromRgb(154, 161, 171));
            ProcessStatusText.Text = "等待 Forza Horizon 6";
            ProcessDetailText.Text = "游戏关闭时可浏览车辆目录";
        }
    }

    private async void Inspect_Click(object sender, RoutedEventArgs e)
    {
        var activity = SelectedActivity;
        await RunOperationAsync($"只读扫描 {activity.DisplayName}", async progress =>
        {
            var snapshot = await _gameService.InspectAsync(activity.Id, progress);
            ApplySnapshot(snapshot);
            FooterStatusText.Text = $"{activity.DisplayName}兼容性扫描完成，未写入游戏内存";
        });
    }

    private async void ApplyAll_Click(object sender, RoutedEventArgs e)
    {
        var targets = Enumerable.Range(1, SelectedActivity.SlotCount)
            .Select(GetSelectedCar)
            .ToArray();
        if (targets.Any(target => target is null))
        {
            ShowError("每个展位都需要选择一辆车。");
            return;
        }

        await ApplyCarsAsync(
            targets.Cast<CarOption>().ToArray(),
            $"应用{SelectedActivity.DisplayName}全部展位");
    }

    private async void ApplySlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string slotText } ||
            !int.TryParse(slotText, out var slot))
        {
            return;
        }
        var target = GetSelectedCar(slot);
        if (target is null)
        {
            ShowError($"请先为展位 {slot} 选择车辆。");
            return;
        }

        await ApplyCarsAsync([target], $"应用展位 {slot}");
    }

    private async Task ApplyCarsAsync(IReadOnlyList<CarOption> targets, string title)
    {
        await RunOperationAsync(title, async progress =>
        {
            var result = await _gameService.ApplyAsync(targets, progress);
            ApplySnapshot(result.Snapshot);
            var changed = result.Slots.Count(slot => !slot.WasAlreadySelected);
            var unchanged = result.Slots.Count - changed;
            var summary = $"车辆切换完成：{changed} 个展位已更新" +
                          (unchanged > 0 ? $"，{unchanged} 个无需更改" : string.Empty);
            FooterStatusText.Text = summary;
            AppendDiagnostic(summary + "。");
        });
    }

    private async void RebuySlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string slotText } ||
            !int.TryParse(slotText, out var slot))
        {
            return;
        }
        var target = GetSelectedCar(slot);
        if (target is null)
        {
            ShowError($"请先为展位 {slot} 选择车辆。");
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"为{SelectedActivity.DisplayName}展位 {slot} 恢复“{target.ChineseName}”的购买入口？\n\n" +
            "该操作会恢复资格、临时切换两辆车并让游戏重建交互。开始后请切回游戏并停留在活动场地。",
            "确认恢复重购",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        await RunOperationAsync($"恢复展位 {slot} 的购买入口", async progress =>
        {
            var result = await _gameService.RestorePurchaseAsync(target, progress);
            PurchaseDiagnosticText.Text = FormatPurchaseDiagnostic(result.FinalDiagnostic);
            FooterStatusText.Text = $"展位 {slot} 重购事务完成，资格判定已通过";
            AppendDiagnostic(
                $"重购完成：展位 {slot}，目标 {target.OriginalName}，" +
                $"临时车辆 {result.FirstStagingCar.ModelEn} -> {result.RefreshStagingCar.ModelEn}，" +
                $"耗时 {result.Elapsed.TotalSeconds:0.0} 秒。");
            MessageBox.Show(
                this,
                "重购事务已完成，游戏资格判定通过。请在活动场地确认购买入口。",
                "恢复完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        });
    }

    private async void DiagnosePurchase_Click(object sender, RoutedEventArgs e)
    {
        if (DiagnosticSlotCombo.SelectedItem is not ComboBoxItem { Tag: string slotText } ||
            !int.TryParse(slotText, out var slot))
        {
            return;
        }

        await RunOperationAsync($"读取展位 {slot} 购买资格", async progress =>
        {
            var diagnostic = await _gameService.DiagnosePurchaseAsync(
                SelectedActivity.Id,
                slot,
                progress);
            PurchaseDiagnosticText.Text = FormatPurchaseDiagnostic(diagnostic);
            AppendDiagnostic(
                $"资格诊断：展位 {slot}，车辆 {diagnostic.CurrentCar.OriginalName}，" +
                $"flag={diagnostic.EligibilityFlag}，eligible={diagnostic.IsEligible}。");
            FooterStatusText.Text = $"展位 {slot} 资格读取完成";
        });
    }

    private async Task RunOperationAsync(
        string title,
        Func<IProgress<GameProgress>, Task> operation)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        _lastProgressMessage = string.Empty;
        BusyTitleText.Text = title;
        BusyDetailText.Text = "正在准备";
        BusyOverlay.Visibility = Visibility.Visible;
        FooterStatusText.Text = title;
        var progress = new Progress<GameProgress>(UpdateProgress);
        try
        {
            await operation(progress);
        }
        catch (OperationCanceledException)
        {
            FooterStatusText.Text = "操作已取消";
            AppendDiagnostic($"{title}：已取消。");
        }
        catch (GameToolException exception)
        {
            FooterStatusText.Text = "操作未完成";
            AppendDiagnostic($"{title}：失败 - {exception.Message}");
            AppLogger.Error(title, exception);
            ShowError(exception.UserMessage);
        }
        catch (Exception exception)
        {
            FooterStatusText.Text = "操作未完成";
            AppendDiagnostic($"{title}：失败 - {exception.Message}");
            AppLogger.Error(title, exception);
            ShowError("操作未完成，详细信息已写入诊断日志。\n\n" + exception.Message);
        }
        finally
        {
            BusyOverlay.Visibility = Visibility.Collapsed;
            _isBusy = false;
            RefreshProcessStatus();
        }
    }

    private void UpdateProgress(GameProgress progress)
    {
        var byteText = progress.BytesScanned > 0
            ? $"  ·  已扫描 {FormatBytes(progress.BytesScanned)}"
            : string.Empty;
        BusyDetailText.Text = progress.Message + byteText;
        FooterStatusText.Text = progress.Message;
        if (!string.Equals(_lastProgressMessage, progress.Message, StringComparison.Ordinal))
        {
            _lastProgressMessage = progress.Message;
            AppendDiagnostic(progress.Message + byteText + "。");
        }
    }

    private void ApplySnapshot(GameSnapshot snapshot)
    {
        if (snapshot.ActivityId != SelectedActivity.Id ||
            snapshot.ExpectedSlotCount != SelectedActivity.SlotCount)
        {
            throw new InvalidOperationException(
                $"Snapshot activity {snapshot.ActivityId}/{snapshot.ExpectedSlotCount} does not match " +
                $"the selected activity {SelectedActivity.Id}/{SelectedActivity.SlotCount}.");
        }
        _lastSnapshot = snapshot;
        var bySlot = snapshot.Slots.ToDictionary(slot => slot.Slot);
        Slot1CurrentText.Text = "当前：" + bySlot[1].CurrentCar.ChineseName;
        Slot2CurrentText.Text = "当前：" + bySlot[2].CurrentCar.ChineseName;
        Slot3CurrentText.Text = bySlot.TryGetValue(3, out var thirdSlot)
            ? "当前：" + thirdSlot.CurrentCar.ChineseName
            : "当前：此活动无第三展位";
        ManagerDiagnosticText.Text = snapshot.ManagerDiscovery;
        SetterDiagnosticText.Text = snapshot.SetterAddress == 0
            ? "未通过特征校验"
            : $"{snapshot.SetterDiscovery}  ·  0x{snapshot.SetterAddress:X}";
        RefreshDiagnosticText.Text = snapshot.RefreshAddress == 0
            ? "未通过特征校验（重购停用）"
            : $"{snapshot.RefreshDiscovery}  ·  0x{snapshot.RefreshAddress:X}";
        var setterOk = snapshot.SetterAddress != 0;
        var refreshOk = snapshot.RefreshAddress != 0;
        CompatibilitySummaryText.Text = setterOk && refreshOk
            ? "车辆切换与重购特征均通过"
            : setterOk
                ? "车辆切换可用，重购特征未通过"
                : "当前版本未通过写入特征校验";
        CompatibilitySummaryText.Foreground = setterOk && refreshOk
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("WarningBrush");
        SnapshotGrid.ItemsSource = snapshot.Slots.Select(slot => new SnapshotRow(
            slot.Slot,
            slot.CurrentCar.OriginalName,
            slot.CurrentAftermarketId,
            slot.CurrentCarModelId,
            $"0x{slot.ManagerAddress:X16}")).ToArray();

        ProcessStatusDot.Fill = (Brush)FindResource("SuccessBrush");
        ProcessStatusText.Text = $"已连接 Forza Horizon 6  ·  PID {snapshot.ProcessId}";
        ProcessDetailText.Text = string.IsNullOrWhiteSpace(snapshot.Version)
            ? Path.GetFileName(snapshot.ExecutablePath)
            : snapshot.Version;
        AppendDiagnostic(
            $"快照完成：{snapshot.ActivityDisplayName}，PID {snapshot.ProcessId}，" +
            $"manager={snapshot.ManagerDiscovery}，" +
            $"setter={snapshot.SetterDiscovery}，refresh={snapshot.RefreshDiscovery}。");
    }

    private static string FormatPurchaseDiagnostic(PurchaseDiagnostic diagnostic) =>
        $"{diagnostic.CurrentCar.ChineseName}  ·  资格字节 {diagnostic.EligibilityFlag}  ·  " +
        (diagnostic.IsEligible ? "可购买" : "已锁定") +
        $"  ·  {diagnostic.ResolverDiscovery}";

    private CarOption? GetSelectedCar(int slot) => slot switch
    {
        1 => SelectedSlot1,
        2 => SelectedSlot2,
        3 => SelectedSlot3,
        _ => null
    };

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
        RefreshFilters();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsInitialized)
        {
            RefreshFilters();
        }
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void SlotList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: CarOption car, Tag: string slotText } ||
            !int.TryParse(slotText, out var slot))
        {
            return;
        }

        switch (slot)
        {
            case 1:
                SelectedSlot1 = car;
                break;
            case 2:
                SelectedSlot2 = car;
                break;
            case 3:
                SelectedSlot3 = car;
                break;
        }
    }

    private void RefreshFilters()
    {
        Slot1View?.Refresh();
        Slot2View?.Refresh();
        Slot3View?.Refresh();
        RestoreVisibleSelection(Slot1List, Slot1View, SelectedSlot1);
        RestoreVisibleSelection(Slot2List, Slot2View, SelectedSlot2);
        RestoreVisibleSelection(Slot3List, Slot3View, SelectedSlot3);
        if (VisibleCountText is not null &&
            Slot1View is not null &&
            Slot2View is not null &&
            Slot3View is not null)
        {
            var visible = Slot1View.Cast<object>().Count() +
                          Slot2View.Cast<object>().Count() +
                          Slot3View.Cast<object>().Count();
            var total = CarCatalog.ForActivity(SelectedActivity.Id).Count;
            VisibleCountText.Text = $"显示 {visible} / {total}";
        }
    }

    private static void RestoreVisibleSelection(
        ListBox? listBox,
        ICollectionView? view,
        CarOption? selection)
    {
        if (listBox is null || view is null || selection is null)
        {
            return;
        }
        listBox.SelectedItem = view.Contains(selection) ? selection : null;
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppLogger.LogDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{AppLogger.LogDirectory}\"",
            UseShellExecute = true
        });
    }

    private void AppendDiagnostic(string message)
    {
        _diagnosticLog
            .Append('[')
            .Append(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
            .Append("] ")
            .AppendLine(message);
        if (DiagnosticsTextBox is not null)
        {
            DiagnosticsTextBox.Text = _diagnosticLog.ToString();
            DiagnosticsTextBox.ScrollToEnd();
        }
    }

    private void ShowError(string message)
    {
        MessageBox.Show(
            this,
            message,
            "FH6 奇珍商店工具",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.0} {units[unit]}";
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed record SnapshotRow(
        int Slot,
        string CarName,
        int AftermarketId,
        int ModelId,
        string ManagerAddress);
}
