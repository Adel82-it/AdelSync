using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AdelSync.Services;
using AdelSync.Windows;
using Forms = System.Windows.Forms;
using MediaBrush = System.Windows.Media.Brush;

namespace AdelSync;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly AdelSyncApi _api;
    private readonly DispatcherTimer _timer = new();
    private TrayService? _tray;
    private string _lastStatus = "";
    private int _lastSkips = -1;
    private int _localCooldown;

    public ObservableCollection<EventRow> Events { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        _api = new AdelSyncApi(_settings);
        EventsList.ItemsSource = Events;

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _tray = new TrayService(this);
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(3, _settings.PollSeconds));
        _timer.Tick += async (_, _) => await RefreshAsync(false);
        _timer.Start();
        _ = RefreshAsync(true);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Сворачиваем в трей вместо закрытия — как мониторинг.
        e.Cancel = true;
        Hide();
        _tray?.Balloon("AdelSync", "Работаю в трее. Двойной клик — открыть.");
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync(true);

    private async Task RefreshAsync(bool manual)
    {
        try
        {
            var dto = await _api.FetchAsync();
            Apply(dto, manual);
            ErrorText.Text = "";
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            StatusPillText.Text = "нет связи";
            StatusPill.Background = new SolidColorBrush(Color.FromRgb(0x3F, 0x1D, 0x1D));
            StatusPillText.Foreground = (MediaBrush)FindResource("DangerBrush");
            if (manual)
                ToastWindow.ShowToast("AdelSync", "Не удалось получить статус: " + ex.Message);
        }
    }

    private void Apply(SyncStatusDto dto, bool manual)
    {
        StatusPillText.Text = dto.StatusLabel;
        switch (dto.Status)
        {
            case "syncing":
                StatusPill.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x3C, 0x3A));
                StatusPillText.Foreground = (MediaBrush)FindResource("TealBrush");
                break;
            case "cooldown":
                StatusPill.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x2E, 0x14));
                StatusPillText.Foreground = (MediaBrush)FindResource("AmberBrush");
                break;
            default:
                StatusPill.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x2E, 0x24));
                StatusPillText.Foreground = (MediaBrush)FindResource("OkBrush");
                break;
        }

        CpuGauge.Value = dto.Stats.CpuSavedScore;
        CpuLabel.Text = dto.Stats.CpuSavedLabel;
        var skipScore = Math.Min(100, dto.Stats.SkipsToday * 10);
        SkipGauge.Value = skipScore;
        SkipLabel.Text = $"{dto.Stats.SkipsToday} отбито · всего {dto.Stats.SkipsTotal}";

        _localCooldown = dto.CooldownSeconds;
        UpdateCooldownUi(dto.CooldownTotal > 0 ? dto.CooldownTotal : 2700);
        ImportsText.Text = $"Разборов сегодня: {dto.Stats.ImportsToday}";
        FooterText.Text = $"AdelSync · {dto.Author} · API {dto.Version} · {dto.Site}";

        Events.Clear();
        foreach (var ev in dto.Events.Take(30))
            Events.Add(EventRow.From(ev));

        _tray?.SetTip($"AdelSync: {dto.StatusLabel}");

        if (_lastStatus != "" && _lastStatus != dto.Status)
        {
            if (dto.Status == "syncing")
            {
                ToastWindow.ShowToast("AdelSync", "Сейчас идёт обмен с 1С. Можно свернуть окно — я в трее.");
                _tray?.Balloon("AdelSync", "Идёт обмен каталога с 1С", Forms.ToolTipIcon.Info);
            }
            else if (_lastStatus == "syncing")
            {
                ToastWindow.ShowToast("AdelSync", "Обмен завершён или на паузе.");
            }
        }

        if (_lastSkips >= 0 && dto.Stats.SkipsToday > _lastSkips)
        {
            var msg = $"Отбит повтор. Следующий обмен через {FormatHms(_localCooldown)}";
            ToastWindow.ShowToast("AdelSync · защита", msg);
            _tray?.Balloon("AdelSync", msg, Forms.ToolTipIcon.Warning);
        }

        if (manual && dto.Status == "cooldown")
            ToastWindow.ShowToast("AdelSync", $"Следующий полный разбор доступен через {FormatHms(_localCooldown)}");

        _lastStatus = dto.Status;
        _lastSkips = dto.Stats.SkipsToday;
    }

    private void UpdateCooldownUi(int total)
    {
        CooldownText.Text = _localCooldown > 0 ? FormatHms(_localCooldown) : "можно сейчас";
        var pct = total <= 0 ? 0 : Math.Clamp(100.0 * (total - _localCooldown) / total, 0, 100);
        CooldownBar.Value = _localCooldown <= 0 ? 100 : pct;
        CooldownHint.Text = _localCooldown > 0
            ? "Защита держит окно: повторный разбор того же пакета отсекается до конца таймера."
            : "Окно открыто — Руслан может запускать выгрузку. Днём это разрешено.";
    }

    private static string FormatHms(int seconds)
    {
        if (seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Тикаем локальный таймер каждую секунду для плавного countdown.
        var local = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        local.Tick += (_, _) =>
        {
            if (_localCooldown > 0)
            {
                _localCooldown--;
                UpdateCooldownUi(2700);
            }
        };
        local.Start();
    }
}

public sealed class EventRow
{
    public string Message { get; init; } = "";
    public string TypeLabel { get; init; } = "";
    public string TimeLabel { get; init; } = "";
    public MediaBrush DotBrush { get; init; } = Brushes.Gray;

    public static EventRow From(SyncEventDto ev)
    {
        MediaBrush brush = ev.Type switch
        {
            "skip" => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
            "sync_start" => new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xBF)),
            "sync_end" => new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)),
            "import" => new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA)),
            _ => new SolidColorBrush(Color.FromRgb(0x8B, 0x9B, 0xB8))
        };
        var label = ev.Type switch
        {
            "skip" => "антиретрай",
            "sync_start" => "обмен",
            "sync_end" => "готово",
            "import" => "разбор",
            _ => ev.Type
        };
        var local = DateTimeOffset.FromUnixTimeSeconds(ev.Ts).ToLocalTime();
        return new EventRow
        {
            Message = ev.Message,
            TypeLabel = label,
            TimeLabel = local.ToString("HH:mm:ss"),
            DotBrush = brush
        };
    }
}
