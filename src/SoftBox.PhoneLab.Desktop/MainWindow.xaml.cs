using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using SoftBox.PhoneLab.Core.Diagnostics;
using SoftBox.PhoneLab.Core.Activity;
using SoftBox.PhoneLab.Core.Settings;
using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Core.Security;
using SoftBox.PhoneLab.Core.Parsing;
using SoftBox.PhoneLab.Desktop.Models;
using SoftBox.PhoneLab.Desktop.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace SoftBox.PhoneLab.Desktop;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly AdbClient _adb = new();
    private readonly AndroidBackupSchedulerService _backupScheduler;
    private readonly DispatcherTimer _backupSchedulerTimer;
    private bool _processingBackupSchedules;
    private readonly PhoneDiscoveryService _discovery;
    private readonly WindowsUsbInventoryService _usbInventory = new();
    private IReadOnlyList<WindowsUsbDevice> _lastUsbDevices = Array.Empty<WindowsUsbDevice>();
    private readonly DeviceInspectionEngine _inspection;
    private readonly AgentBridgeClient _agentBridge;
    private readonly BoardMapCatalog _maps = new();
    private readonly DeviceProfileCatalog _deviceProfiles = new();
    private readonly ScreenMirrorService _screenMirror;
    private readonly PhoneLabAgentInstaller _agentInstaller;
    private readonly HashSet<string> _preparedSerials = new(StringComparer.Ordinal);
    private bool _refreshing;
    private bool _suppressDeviceSelection;
    private DateTime _lastAutoMirrorAttemptUtc = DateTime.MinValue;
    private string? _lastAutoMirrorAttemptSerial;
    private readonly TraceBoardViewerService _traceBoardViewer = new();
    private ExternalMeasurementSample? _measurementSnapshot;
    private readonly RepairKnowledgeService _repairKnowledge = new();
    private readonly ReportService _reports = new();
    private readonly LocalActivityStore _activity = new();
    private readonly DiagnosticEvidenceStore _diagnosticEvidenceStore = new();
    private readonly PhoneLabSettingsService _settingsService = new();
    private readonly SoftBoxAccountService _accountService;
    private AuthenticatedSoftBoxUser _currentUser;
    private PhoneLabSettings _settings = new();
    private readonly DispatcherTimer _timer;
    private bool _busy;
    private bool _dragging;
    private Point _dragOrigin;
    private Point _lastPointer;
    private double _yaw = -32;
    private double _pitch = 23;
    private double _zoom = 30;
    private double _explode = 0.8;
    private bool _showDisplayLayer = true;
    private string _deviceSignature = "\\0";
    private DeviceSnapshot? _snapshot;
    private AgentSnapshot? _agentSnapshot;
    private BoardMap _boardMap = new();
    private DeviceProfileMatch _profileMatch = DeviceProfileMatch.Unknown;
    private readonly DeviceComponentCatalog _componentCatalog = new();
    private DeviceComponentProfile? _componentProfile;
    private ComponentViewModel? _selectedComponent;
    private readonly Dictionary<ModelVisual3D, string> _visualParts = new();

    public ObservableCollection<DeviceListItem> Devices { get; } = [];
    public ObservableCollection<ComponentViewModel> Components { get; } = [];
    public ObservableCollection<PropertyLine> DeviceFacts { get; } = [];
    public string CurrentUserDisplay => $"{_currentUser.DisplayName} • {SoftBoxAccountService.RoleLabel(_currentUser.Role)}";
    public string LanguageToggleText => _settings.UiLanguage == "ar" ? "English" : "العربية";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string EngineStatus => _adb.IsAvailable ? "USB AUTO-DETECT / ADB READY" : "USB AUTO-DETECT READY / ADB MISSING";
    public string DiscoveryHint => _adb.IsAvailable
        ? "يتم فحص USB تلقائيًا، ثم قراءة خصائص Android عبر ADB عند منح تصريح USB debugging."
        : "فحص واجهات USB يعمل دون ADB؛ ثبّت Android Platform-Tools لقراءة خصائص الهاتف عبر ADB.";
    public string DeviceTitle => _snapshot is null ? SelectedDevice?.DisplayName ?? "لا يوجد هاتف متصل" : $"{_snapshot.Manufacturer} {_snapshot.Model}".Trim();
    public string DeviceSubtitle => _snapshot is null
        ? "بانتظار جهاز Android عبر USB"
        : $"Android {_snapshot.AndroidVersion} • {_snapshot.ModelNumber} • {_snapshot.DeviceCode}" +
          (_profileMatch.Profile is null ? " • قاعدة الموديلات: غير مدرج" : $" • {_profileMatch.DisplayName} ({_profileMatch.Confidence})");
    public string ScanStatus { get; private set; } = "بانتظار جهاز";
    public string UsbRecognitionStatus { get; private set; } = "فحص واجهات USB تلقائيًا عند بدء التشغيل.";
    public string StatusMessage { get; private set; } = "PhoneLab جاهز لاكتشاف أجهزة Android المصرّح بها.";
    public string BoardMapNotice => _boardMap.AccuracyNotice;
    public string AgentStatus { get; private set; } = "سيُثبت Agent تلقائيًا بعد تصريح ADB، ثم يطلب موافقتك على الهاتف.";
    public string MirrorStatus { get; private set; } = "بعد تصريح USB debugging ستظهر شاشة الهاتف هنا تلقائيًا ويمكن التحكم بها من داخل PhoneLab.";
    public string SelectedPartName => _selectedComponent?.Name ?? "اختر قطعة من المجسم";
    public string SelectedPartStatus => _selectedComponent?.StatusLabel ?? "لم تختبر";
    public Brush SelectedPartBrush => _selectedComponent?.StatusBrush ?? MakeBrush("#64748B");
    public string SelectedPartDescription => _selectedComponent is null
        ? "تظهر معلومات الجزء وسبب حالته هنا."
        : _selectedComponent.Description + ComponentKnowledgeText(_selectedComponent.Id);
    public string SelectedPartEvidence => _selectedComponent is null
        ? "لا توجد أدلة اختبار بعد."
        : _selectedComponent.Evidence + ComponentSourcesText(_selectedComponent.Id);

    public ComponentViewModel? SelectedComponent
    {
        get => _selectedComponent;
        set
        {
            if (ReferenceEquals(_selectedComponent, value)) return;
            _selectedComponent = value;
            NotifySelectedPart();
            BuildScene();
        }
    }

    private DeviceListItem? SelectedDevice => DevicesList?.SelectedItem as DeviceListItem;

    public MainWindow(SoftBoxAccountService accountService, AuthenticatedSoftBoxUser currentUser)
    {
        _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        InitializeComponent();

        // Keep the restored (non-maximized) window within the current monitor's work area.
        // The application still starts maximized and retains normal Windows minimize/restore behavior.
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(900, workArea.Width * 0.82);
        MinHeight = Math.Min(560, workArea.Height * 0.76);
        Width = Math.Min(1440, workArea.Width * 0.92);
        Height = Math.Min(900, workArea.Height * 0.88);
        WindowState = WindowState.Maximized;

        DataContext = this;
        EmbeddedWindowExtensions.Host = this;
        _discovery = new PhoneDiscoveryService(_adb);
        _inspection = new DeviceInspectionEngine(_adb);
        _agentBridge = new AgentBridgeClient(_adb);
        _agentInstaller = new PhoneLabAgentInstaller(_adb);
        _screenMirror = new ScreenMirrorService(_adb);
        _backupScheduler = new AndroidBackupSchedulerService(_adb);
        _backupSchedulerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _backupSchedulerTimer.Tick += async (_, _) => await ProcessBackupSchedulesAsync();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) => await RefreshDevicesAsync();
        Loaded += async (_, _) =>
        {
            try
            {
                _settings = await _settingsService.LoadAsync();
                SoftBoxLocalization.SetLanguage(_settings.UiLanguage);
                SoftBoxLocalization.Apply(this);
                Notify(nameof(LanguageToggleText));
                ApplyUiScaleVisual(_settings.UiScale);
            }
            catch (Exception ex)
            {
                _settings = new PhoneLabSettings();
                StatusMessage = "تعذر قراءة إعدادات SoftBox؛ تم استخدام القيم الافتراضية لهذه الجلسة: " + ex.Message;
                Notify(nameof(StatusMessage));
            }

            _timer.Interval = TimeSpan.FromSeconds(_settings.DiscoveryIntervalSeconds);
            await _deviceProfiles.LoadAsync();
            _boardMap = await _maps.LoadBestMapAsync(new DeviceSnapshot(), null);
            Notify(nameof(BoardMapNotice));
            BuildScene();
            RebuildDiagnostics(); // Show the full Not tested component list before any phone/ADB connection.
            await RefreshDevicesAsync();
            if (_settings.AutoDiscoverDevices)
                _timer.Start();

            if (!_adb.IsAvailable)
                await EnsureAdbInstalledAsync(askFirst: true);

            if ((SoftBoxAuthorizationPolicy.PermissionsForRole(_currentUser.Role) &
                 SoftBoxAccountPermission.DeviceOperations) != 0)
            {
                _backupSchedulerTimer.Start();
                await ProcessBackupSchedulesAsync();
            }
            _ = SyncDeviceCatalogQuietlyAsync();
        };
        Closed += (_, _) => { _timer.Stop(); _backupSchedulerTimer.Stop(); _screenMirror.Dispose(); };
        Notify(nameof(EngineStatus));
        Notify(nameof(DiscoveryHint));
        Notify(nameof(CurrentUserDisplay));
        Notify(nameof(AdbInstallVisibility));
        BuildScene();
        RebuildDiagnostics();
    }

    private async void LockSession_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _accountService.RecordSessionLockedAsync(_currentUser.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "تعذر تسجيل حدث قفل الجلسة: " + ex.Message,
                "SoftBox", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var authentication = new SoftBoxAuthenticationWindow(_accountService, initialSetup: false)
        {
            Owner = this
        };
        var result = authentication.ShowDialog();
        if (result != true || authentication.AuthenticatedUser is null)
        {
            Application.Current.Shutdown();
            return;
        }

        _currentUser = authentication.AuthenticatedUser;
        try
        {
            await _accountService.RecordSessionUnlockedAsync(_currentUser.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "تمت إعادة المصادقة، لكن تعذر تسجيل فتح الجلسة: " + ex.Message,
                "SoftBox", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Notify(nameof(CurrentUserDisplay));
    }

    public async Task RefreshDevicesAsync()
    {
        if (_busy || _refreshing) return;
        _refreshing = true;
        try
        {
            await RefreshUsbRecognitionAsync();
            if (!_adb.IsAvailable)
            {
                StatusMessage = "أدوات ADB غير موجودة. " + UsbRecognitionStatus;
                MirrorStatus = "تم فحص USB، لكن عرض الشاشة يحتاج Android يعمل وتصريح ADB. ثبّت Platform-Tools وتعريف الشركة ثم فعّل USB debugging ووافق على الكمبيوتر.";
                Notify(nameof(MirrorStatus));
                Notify(nameof(StatusMessage));
                return;
            }
            var list = await _discovery.DiscoverAsync();
            var signature = string.Join("|", list.Select(d => d.Serial + ":" + d.State));
            if (signature == _deviceSignature)
            {
                // Device inventory is unchanged, but scrcpy may have exited or failed to start.
                // Retry only the screen session; never repeat inspection or Agent installation.
                if (SelectedDevice is { IsAuthorized: true } stableDevice &&
                    _settings.AutoOpenScreenMirror &&
                    SoftBoxAuthorizationPolicy.IsAllowed(_currentUser.Role, "MirrorScreen") &&
                    (_screenMirror.ActiveSerial != stableDevice.Serial || !_screenMirror.IsRunning))
                {
                    await RetryAutoScreenMirrorAsync(stableDevice);
                }
                return;
            }
            _deviceSignature = signature;
            var previousDevices = Devices.ToDictionary(device => device.Serial, StringComparer.Ordinal);
            var previous = SelectedDevice?.Serial;
            if (_screenMirror.ActiveSerial is string active && !list.Any(d => d.Serial == active && d.IsAuthorized))
            {
                _screenMirror.Stop();
                PhoneScreenHost.DetachWindow();
                MirrorStatus = "انقطع اتصال الهاتف. سيُفتح العرض مجددًا عند توصيله والتصريح به.";
                Notify(nameof(MirrorStatus));
            }
            _preparedSerials.RemoveWhere(id => !list.Any(d => d.Serial == id));
            Devices.Clear();
            foreach (var device in list)
            {
                var item = device;
                if (device.IsAuthorized)
                {
                    try
                    {
                        var rawProperties = await _adb.RunForDeviceAsync(device.Serial, "shell", "getprop");
                        var identity = DevicePropertyParser.ParseIdentityHint(rawProperties);
                        item = device with
                        {
                            Model = identity.Model == "غير متاح" ? device.Model : identity.Model,
                            Manufacturer = identity.Manufacturer == "غير متاح" ? device.Manufacturer : identity.Manufacturer
                        };
                    }
                    catch { }
                }
                Devices.Add(item);
            }

            var currentSerials = new HashSet<string>(Devices.Select(device => device.Serial), StringComparer.Ordinal);
            foreach (var current in Devices)
            {
                if (!previousDevices.TryGetValue(current.Serial, out var prior))
                {
                    if (current.IsAuthorized)
                    {
                        await RecordDeviceHistoryAsync(
                            current.Serial, current.Manufacturer, current.Model,
                            "DeviceConnected", "تم اكتشاف جهاز Android مصرح به عبر ADB.");
                        await AddNotificationAsync(
                            "device", "تم اكتشاف جهاز", $"تم اكتشاف {current.DisplayName} عبر ADB.",
                            "device-center", current.Serial);
                    }
                }
                else if (!prior.IsAuthorized && current.IsAuthorized)
                {
                    await RecordDeviceHistoryAsync(
                        current.Serial, current.Manufacturer, current.Model,
                        "AdbAuthorized", "أصبح الجهاز مصرحًا به عبر ADB.");
                    await AddNotificationAsync(
                        "device", "تمت الموافقة على ADB", $"الجهاز {current.DisplayName} أصبح جاهزًا للفحص.",
                        "device-center", current.Serial);
                }
                else if (prior.IsAuthorized && !current.IsAuthorized)
                {
                    await RecordDeviceHistoryAsync(
                        current.Serial, prior.Manufacturer, prior.Model,
                        "AdbAuthorizationLost", "فقد الجهاز تصريح ADB أو تغيرت حالة الاتصال.");
                    await AddNotificationAsync(
                        "warning", "تغيرت حالة اتصال الجهاز", $"الجهاز {prior.DisplayName} لم يعد مصرحًا به عبر ADB.",
                        "device-center", current.Serial);
                }
            }

            foreach (var prior in previousDevices.Values.Where(device => !currentSerials.Contains(device.Serial)))
            {
                if (prior.IsAuthorized)
                {
                    await RecordDeviceHistoryAsync(
                        prior.Serial, prior.Manufacturer, prior.Model,
                        "DeviceDisconnected", "تم رصد فصل الجهاز عن قناة ADB.");
                    await AddNotificationAsync(
                        "device", "تم فصل جهاز", $"تم فصل {prior.DisplayName} عن ADB.",
                        "device-center", prior.Serial);
                }
            }

            _suppressDeviceSelection = true;
            DevicesList.SelectedItem = Devices.FirstOrDefault(d => d.Serial == previous)
                ?? Devices.FirstOrDefault(d => d.IsAuthorized) ?? Devices.FirstOrDefault();
            _suppressDeviceSelection = false;
            if (Devices.Count == 0)
            {
                ClearDevice("لا يوجد جهاز ADB متصل.");
                MirrorStatus = "بانتظار هاتف Android. سيظهر بث الشاشة بعد تشغيل النظام وتفعيل USB debugging والموافقة على هذا الكمبيوتر.";
                Notify(nameof(MirrorStatus));
                StatusMessage = "لم يتم رصد قناة ADB. " + UsbRecognitionStatus +
                    " ظهور الهاتف في مستكشف الملفات عبر MTP لا يمنح تصريح تشخيص ADB.";
                AgentStatus = "بانتظار اتصال ADB مصرح به.";
                Notify(nameof(StatusMessage)); Notify(nameof(AgentStatus)); return;
            }
            var selected = SelectedDevice;
            if (selected is null || !selected.IsAuthorized)
            {
                ClearDevice("الهاتف ظاهر لكن لم يمنح تصريح ADB بعد.");
                MirrorStatus = "تم رصد الهاتف عبر USB، لكن Android لم يمنح تصريح ADB. افتح قفل الهاتف ووافق على رسالة USB debugging؛ لا يمكن عرض الشاشة قبل الموافقة.";
                Notify(nameof(MirrorStatus));
                var waiting = Devices.FirstOrDefault(d => d.State.Equals("unauthorized", StringComparison.OrdinalIgnoreCase));
                StatusMessage = waiting is not null
                    ? "افتح قفل الشاشة ووافق على «السماح بتصحيح أخطاء USB»، واختر السماح دائمًا لهذا الكمبيوتر."
                    : "تم رصد USB دون قناة ADB جاهزة. افحص تعريف ADB ووضع USB ثم أعد التوصيل.";
                Notify(nameof(StatusMessage)); return;
            }
            StatusMessage = "تم رصد هاتف مصرح به. جارٍ فتح الشاشة وتجهيز Agent تلقائيًا...";
            Notify(nameof(StatusMessage));
            await EnsureSelectedDeviceReadyAsync(selected);
        }
        catch (Exception ex) { StatusMessage = "فشل اكتشاف USB/ADB: " + ex.Message; Notify(nameof(StatusMessage)); }
        finally { _suppressDeviceSelection = false; _refreshing = false; }
    }

    private async Task EnsureSelectedDeviceReadyAsync(DeviceListItem selected)
    {
        if (!selected.IsAuthorized) return;
        if (_preparedSerials.Add(selected.Serial) || (_settings.AutoOpenScreenMirror && _screenMirror.ActiveSerial != selected.Serial))
        {
            _preparedSerials.Add(selected.Serial);
            await PrepareConnectedDeviceAsync(selected);
        }
        if (_settings.AutoInspectOnConnect && SelectedDevice?.Serial == selected.Serial)
            await InspectSelectedAsync();
    }

    private async Task RetryAutoScreenMirrorAsync(DeviceListItem selected)
    {
        if (!selected.IsAuthorized ||
            !_settings.AutoOpenScreenMirror ||
            !SoftBoxAuthorizationPolicy.IsAllowed(_currentUser.Role, "MirrorScreen") ||
            (_screenMirror.ActiveSerial == selected.Serial && _screenMirror.IsRunning))
            return;

        if (string.Equals(_lastAutoMirrorAttemptSerial, selected.Serial, StringComparison.Ordinal) &&
            DateTime.UtcNow - _lastAutoMirrorAttemptUtc < TimeSpan.FromSeconds(12))
            return;

        _lastAutoMirrorAttemptSerial = selected.Serial;
        _lastAutoMirrorAttemptUtc = DateTime.UtcNow;
        try
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            var deadline = DateTime.UtcNow.AddSeconds(6);
            while (PhoneScreenHost.NativeHostHandle == IntPtr.Zero && DateTime.UtcNow < deadline)
                await Task.Delay(100);
            if (PhoneScreenHost.NativeHostHandle == IntPtr.Zero)
                throw new TimeoutException("لم يتم إنشاء مساحة عرض الهاتف الجانبية.");
            await _screenMirror.StartEmbeddedAsync(
                selected.Serial,
                selected.Model ?? selected.DisplayName,
                PhoneScreenHost);
            MirrorStatus = "متصل الآن: " + selected.DisplayName + " — تم استرجاع بث الشاشة تلقائيًا.";
        }
        catch (Exception ex)
        {
            var report = ScreenMirrorFailureAnalyzer.Analyze(ex);
            MirrorStatus = string.Join(Environment.NewLine,
                "فشل الاسترجاع التلقائي: " + report.Title,
                report.Cause, report.RecommendedAction, report.TechnicalDetail);
        }
        Notify(nameof(MirrorStatus));
    }

    private async Task PrepareConnectedDeviceAsync(DeviceListItem selected)
    {
        var canControlDevice = (SoftBoxAuthorizationPolicy.PermissionsForRole(_currentUser.Role) &
            SoftBoxAccountPermission.DeviceOperations) == SoftBoxAccountPermission.DeviceOperations;
        if (_settings.AutoOpenScreenMirror && canControlDevice)
        {
            _lastAutoMirrorAttemptSerial = selected.Serial;
            _lastAutoMirrorAttemptUtc = DateTime.UtcNow;
            try
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                var deadline = DateTime.UtcNow.AddSeconds(6);
                while (PhoneScreenHost.NativeHostHandle == IntPtr.Zero && DateTime.UtcNow < deadline)
                    await Task.Delay(100);
                if (PhoneScreenHost.NativeHostHandle == IntPtr.Zero) throw new TimeoutException("لم يتم إنشاء مساحة عرض الهاتف.");
                await _screenMirror.StartEmbeddedAsync(selected.Serial, selected.Model ?? selected.DisplayName, PhoneScreenHost);
                MirrorStatus = "متصل الآن: " + selected.DisplayName + " — الشاشة مدمجة داخل PhoneLab.";
            }
            catch (Exception ex)
            {
                var report = ScreenMirrorFailureAnalyzer.Analyze(ex);
                MirrorStatus = string.Join(Environment.NewLine,
                    "تعذر فتح العرض داخل البرنامج: " + report.Title,
                    report.Cause, report.RecommendedAction, report.TechnicalDetail);
            }
        }
        else if (!canControlDevice)
        {
            MirrorStatus = "تم إيقاف التحكم التلقائي في شاشة الهاتف لأن دور الحساب الحالي للقراءة فقط.";
        }
        else
        {
            MirrorStatus = "فتح شاشة الهاتف تلقائيًا معطّل من الإعدادات. يمكنك تشغيله يدويًا من Phone Link Studio.";
        }
        Notify(nameof(MirrorStatus));

        var canInstallAgent = (SoftBoxAuthorizationPolicy.PermissionsForRole(_currentUser.Role) &
            SoftBoxAccountPermission.DeviceOperations) == SoftBoxAccountPermission.DeviceOperations;
        if (_settings.AutoInstallAgent && canInstallAgent)
        {
            try
            {
                await _agentInstaller.InstallAndRequestConsentAsync(selected.Serial);
                AgentStatus = "تم تثبيت Agent وإرسال طلب الموافقة. وافق على شاشة الهاتف لتفعيل الاختبارات.";
                _ = WatchForAgentConsentAsync(selected.Serial);
            }
            catch (Exception ex)
            {
                AgentStatus = "تعذر تجهيز Agent تلقائيًا: " + ex.Message + " قد تحتاج إلى السماح بالتثبيت عبر USB في إعدادات الشركة المصنّعة.";
            }
        }
        else if (!canInstallAgent)
        {
            AgentStatus = "دور القراءة فقط يمنع تثبيت Agent أو إرسال طلب تثبيت إلى الهاتف.";
        }
        else
        {
            AgentStatus = "تثبيت Agent التلقائي معطّل من الإعدادات. يمكنك تهيئته يدويًا من زر تحديث Agent.";
        }
        StatusMessage = "الاتصال الأساسي جاهز. راجع حالة تصريح ADB وموافقة Agent.";
        Notify(nameof(AgentStatus)); Notify(nameof(StatusMessage));
    }

    private async Task WatchForAgentConsentAsync(string serial)
    {
        for (var i = 0; i < 24; i++)
        {
            if (!Devices.Any(d => d.Serial == serial && d.IsAuthorized)) return;
            try
            {
                var snapshot = await _agentBridge.ReadSnapshotAsync(serial);
                if (snapshot is not null)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _agentSnapshot = snapshot; ApplyDeviceFacts(); ApplyAgentFacts(snapshot); RebuildDiagnostics();
                        AgentStatus = "تم الاتصال بـ Agent " + snapshot.AgentVersion + "؛ الاختبارات والقراءات متاحة.";
                        Notify(nameof(AgentStatus));
                    });
                    return;
                }
            }
            catch { }
            await Task.Delay(1200);
        }
        await Dispatcher.InvokeAsync(() =>
        {
            AgentStatus = "لم يصل رد Agent. وافق على طلب الاتصال في الهاتف، أو اضغط تهيئة USB لإعادة المحاولة.";
            Notify(nameof(AgentStatus));
        });
    }

    private async Task OpenEmbeddedMirrorAsync(DeviceListItem selected)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        var deadline = DateTime.UtcNow.AddSeconds(6);
        while (PhoneScreenHost.NativeHostHandle == IntPtr.Zero && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        await _screenMirror.StartEmbeddedAsync(selected.Serial, selected.Model ?? selected.DisplayName, PhoneScreenHost);
        MirrorStatus = "متصل الآن: " + selected.DisplayName + " — عرض وتحكم مباشر داخل PhoneLab.";
        Notify(nameof(MirrorStatus));
    }


    private async Task InspectSelectedAsync()
    {
        var selected = SelectedDevice;
        if (selected is null || !selected.IsAuthorized)
        {
            StatusMessage = "اختر جهازًا حالته متصل ومصرّح به أولًا.";
            Notify(nameof(StatusMessage));
            return;
        }
        if (_busy) return;
        _busy = true;
        ScanStatus = "جارٍ قراءة مواصفات الهاتف...";
        StatusMessage = "قراءة معلومات النظام بعمليات ADB للقراءة فقط.";
        Notify(nameof(ScanStatus)); Notify(nameof(StatusMessage));
        try
        {
            _snapshot = await _inspection.InspectAsync(selected.Serial);
            await RecordDeviceHistoryAsync(
                selected.Serial,
                _snapshot.Manufacturer,
                _snapshot.Model,
                "SystemInspection",
                "تمت قراءة خصائص Android المتاحة عبر ADB بوضع القراءة فقط.",
                $"Android {_snapshot.AndroidVersion}; API {_snapshot.ApiLevel}; SoC {_snapshot.Soc}; Build {_snapshot.BuildId}");
            _agentSnapshot = null;
            _measurementSnapshot = null;
            _profileMatch = await _deviceProfiles.MatchAsync(_snapshot);
            _boardMap = await _maps.LoadBestMapAsync(_snapshot, _profileMatch.Profile);
            ApplyDeviceFacts();
            RebuildDiagnostics();
            ScanStatus = "تمت قراءة بيانات الهاتف";
            StatusMessage = "اكتملت قراءة المعلومات المتاحة. القطع غير المختبرة لن تُعرض كسليمة.";
            Notify(nameof(DeviceTitle)); Notify(nameof(DeviceSubtitle));
            Notify(nameof(BoardMapNotice)); Notify(nameof(ScanStatus)); Notify(nameof(StatusMessage));
        }
        catch (Exception ex)
        {
            ScanStatus = "تعذر الفحص";
            StatusMessage = ex.Message;
            Notify(nameof(ScanStatus)); Notify(nameof(StatusMessage));
        }
        finally { _busy = false; }
    }

    private async Task RefreshAgentAsync()
    {
        var selected = SelectedDevice;
        if (selected is null || !selected.IsAuthorized || _snapshot is null)
        {
            AgentStatus = "اختر هاتفًا مصرحًا به عبر ADB. سيُثبت Agent تلقائيًا ويطلب موافقتك على الهاتف.";
            Notify(nameof(AgentStatus)); return;
        }
        try
        {
            var agent = await _agentBridge.ReadSnapshotAsync(selected.Serial);
            if (agent is null)
            {
                AgentStatus = "لم يصل رد Agent. افتح التطبيق المساعد واتركه في الواجهة الأمامية ثم أعد المحاولة.";
                Notify(nameof(AgentStatus)); return;
            }
            _agentSnapshot = agent;
            await RecordDeviceHistoryAsync(
                selected.Serial,
                _snapshot.Manufacturer,
                _snapshot.Model,
                "HardwareDiagnostics",
                $"تم استلام {agent.Tests.Count} نتيجة اختبار من Agent بإذن المستخدم.",
                string.Join("; ", agent.Tests.Take(20).Select(test => $"{test.Key}: {test.Value.Status}")));
            await AddNotificationAsync(
                "diagnostic", "اكتملت مزامنة الاختبارات", $"استلم SoftBox نتائج {agent.Tests.Count} اختبار من الهاتف.",
                "hardware-diagnostics", selected.Serial);
            ApplyDeviceFacts();
            ApplyAgentFacts(agent);
            RebuildDiagnostics();
            AgentStatus = $"Agent {agent.AgentVersion}: {agent.Tests.Count} نتيجة اختبار، {agent.Cameras.Count} كاميرا/مسار كاميرا، {agent.Sensors.Count} مستشعر/مستشعرات.";
            StatusMessage = "تمت مزامنة نتائج الاختبارات التفاعلية من الهاتف.";
            Notify(nameof(AgentStatus)); Notify(nameof(StatusMessage));
        }
        catch (Exception ex)
        {
            AgentStatus = "تعذر الاتصال بالـ Agent: " + ex.Message;
            Notify(nameof(AgentStatus));
        }
    }

    private void ApplyDeviceFacts()
    {
        if (_snapshot is null) return;
        _componentProfile = _componentCatalog.Find(_profileMatch.Profile, _snapshot);
        DeviceFacts.Clear();
        Add("الشركة", _snapshot.Manufacturer);
        Add("الموديل", _snapshot.Model);
        Add("رقم الموديل", _snapshot.ModelNumber);
        Add("Product / Device", $"{_snapshot.Product} / {_snapshot.DeviceCode}");
        Add("Hardware SKU", _snapshot.HardwareSku);
        Add("Build fingerprint", _snapshot.BuildFingerprint);
        Add("Bootloader", _snapshot.Bootloader);
        Add("Board / Hardware", _snapshot.Board);
        Add("Hardware revision", _snapshot.HardwareRevision);
        Add("Board revision", _snapshot.BoardRevision);
        Add("قاعدة الموديلات", _profileMatch.Profile is null ? "غير مدرج — تم استخدام معرفات النظام" : $"{_profileMatch.DisplayName} • {_profileMatch.Confidence} • Score {_profileMatch.Score}");
        if (_profileMatch.Profile is not null)
            Add("مصدر التعرف", _profileMatch.Profile.IdentitySource);
        Add("قاعدة المكونات", _componentProfile is null
            ? "لا توجد بيانات مكونات موثّقة لهذا الموديل بعد (راجع data/device-components/README.md)"
            : $"{_componentProfile.DisplayName} • {_componentProfile.DocumentedCount} من {_componentProfile.Components.Count} مكوّن موثّق • {_componentProfile.Sources.Count} مصادر");
        if (_componentProfile is not null)
            foreach (var record in _componentProfile.Components.Where(c => !c.Confidence.Equals("unknown", StringComparison.OrdinalIgnoreCase)))
                Add("▸ " + record.Name, record.Value + "  [" + record.ConfidenceLabel + "]");
        if (_profileMatch.Profile?.DesignBatteryCapacityMah is int designCapacity)
            Add("سعة البطارية الاسمية", $"{designCapacity} mAh (مواصفة ملف الموديل، ليست قياسًا حاليًا)");
        Add("SoC", _snapshot.Soc);
        Add("وصف CPU", _snapshot.CpuDescription);
        Add("أنوية CPU", _snapshot.CpuCores > 0 ? _snapshot.CpuCores.ToString() : "غير متاح");
        Add("CPU ABI", _snapshot.CpuAbi);
        Add("RAM", _snapshot.RamGiB is null ? "غير متاح" : $"{_snapshot.RamGiB:0.0} GiB");
        Add("التخزين /data", _snapshot.DataTotalKiB is null ? "غير متاح" :
            $"{_snapshot.DataUsedKiB / 1024d / 1024d:0.0} / {_snapshot.DataTotalKiB / 1024d / 1024d:0.0} GiB مستخدم");
        Add("دقة الشاشة", _snapshot.DisplayResolution);
        Add("كثافة الشاشة", _snapshot.DisplayDensity);
        Add("Android / API", $"{_snapshot.AndroidVersion} / {_snapshot.ApiLevel}");
        Add("Build ID", _snapshot.BuildId);
        Add("Security patch", _snapshot.SecurityPatch);
        Add("Baseband", _snapshot.Baseband);
        Add("مستوى البطارية", _snapshot.BatteryPercent is null ? "غير متاح" : $"{_snapshot.BatteryPercent}%");
        Add("جهد البطارية", _snapshot.BatteryVoltageMillivolts is null ? "غير متاح" : $"{_snapshot.BatteryVoltageMillivolts} mV");
        Add("حرارة البطارية", _snapshot.BatteryTemperatureCelsius is null ? "غير متاح" : $"{_snapshot.BatteryTemperatureCelsius:0.0} °C");
        Add("Battery health", _snapshot.BatteryHealth);
        Add("حالة الشحن", _snapshot.BatteryStatus);
    }

    private void ApplyAgentFacts(AgentSnapshot agent)
    {
        Add("Battery charge counter", agent.BatteryChargeCounterMicroAh is null ? "غير متاح" : $"{agent.BatteryChargeCounterMicroAh / 1000d:0} mAh reported");
        Add("Battery current now", agent.BatteryCurrentNowMicroA is null ? "غير متاح" : $"{agent.BatteryCurrentNowMicroA / 1000d:0.0} mA");
        Add("Battery current average", agent.BatteryCurrentAverageMicroA is null ? "غير متاح" : $"{agent.BatteryCurrentAverageMicroA / 1000d:0.0} mA");
        Add("Battery remaining energy", agent.BatteryEnergyCounterNWh is null ? "غير متاح" : $"{agent.BatteryEnergyCounterNWh / 1_000_000d:0.0} mWh");
        Add("Battery cycle count", agent.BatteryCycleCount is null ? "غير مدعوم / غير مكشوف" : agent.BatteryCycleCount.ToString()!);
        Add("تقدير السعة من التفريغ", agent.BatteryCapacityEstimateMah is null ? "لم يتم قياسه" : $"{agent.BatteryCapacityEstimateMah:0} mAh — تقدير Fuel Gauge يحتاج مراجعة");
        if (agent.ScheduledBatteryLatest is { } scheduledBattery)
        {
            Add("آخر عينة بطارية مجدولة من Agent",
                $"{scheduledBattery.BatteryPercent}% • {scheduledBattery.TimestampUtc.ToLocalTime():g}");
            Add("جهد البطارية المجدول",
                scheduledBattery.VoltageMillivolts is int voltage ? $"{voltage} mV" : "غير متاح");
            Add("حرارة البطارية المجدولة",
                scheduledBattery.TemperatureCelsius is double temperature ? $"{temperature:0.0} °C" : "غير متاح");
            Add("عداد دورات البطارية المجدول",
                scheduledBattery.CycleCount is int cycles ? cycles.ToString() : "غير مكشوف من الجهاز");
            Add("حالة الشحن المجدولة", scheduledBattery.Plugged ? "متصل بالشاحن" : "غير متصل بالشاحن");
            Add("عدد عينات البطارية المحفوظة", agent.ScheduledBatterySamples.ToString());
        }
        else
        {
            Add("قراءة البطارية المجدولة من Agent", "لم تصل عينة بعد؛ ينتظر WorkManager موعد أخذ العينة.");
        }

        if (agent.AgentTelemetrySummary is { } telemetry)
        {
            Add("سجلات Agent المحلية", $"{telemetry.EventCountSampled} حدثًا حديثًا • {telemetry.CrashCountSampled} تقرير تعطل محفوظ");
            if (!string.IsNullOrWhiteSpace(telemetry.LastCrashType))
                Add("آخر نوع تعطل في Agent", telemetry.LastCrashType + " • " + (telemetry.LastCrashAtUtc?.ToLocalTime().ToString("g") ?? "وقت غير متاح"));
        }
        if (agent.BatteryCapacityEstimateMah is double estimated && _profileMatch.Profile?.DesignBatteryCapacityMah is int design && design > 0)
            Add("مقارنة السعة الاسمية", $"{estimated / design * 100:0}% من السعة الاسمية المسجلة للموديل (مقارنة تقريبية)");
        foreach (var camera in agent.Cameras)
        {
            var facing = camera.Facing switch { "front" => "أمامية", "back" => "خلفية", "external" => "خارجية", _ => "غير محدد" };
            Add($"Camera {camera.Id}", $"{facing} • {camera.HardwareLevel} • {camera.SensorResolution} • Focal {string.Join("/", camera.FocalLengthsMm.Select(x => x.ToString("0.0")))} mm • Flash {camera.HasFlash}");
            Add($"Camera AF {camera.Id}", camera.AutoFocusModes.Length == 0 ? "غير معلن / ثابت التركيز" : string.Join(", ", camera.AutoFocusModes));
            if (camera.PhysicalCameraIds.Length > 0)
                Add($"Physical IDs {camera.Id}", string.Join(", ", camera.PhysicalCameraIds));
        }
    }

    private void RebuildDiagnostics()
    {
        Components.Clear();
        foreach (var diagnostic in DiagnosticEngine.Build(_snapshot, _agentSnapshot, _measurementSnapshot))
            Components.Add(new ComponentViewModel(diagnostic));
        ComponentsList.SelectedItem = Components.FirstOrDefault(x => x.Id == _selectedComponent?.Id)
            ?? Components.FirstOrDefault();
        BuildScene();

        // Persist only evidence associated with the currently selected, authorized ADB device.
        // The store masks the serial, uses a one-way fingerprint and protects the local file with DPAPI.
        _ = PersistDiagnosticEvidenceAsync();
    }

    private async Task PersistDiagnosticEvidenceAsync()
    {
        var selected = SelectedDevice;
        if (_snapshot is null || selected is not { IsAuthorized: true } ||
            !_snapshot.Serial.Equals(selected.Serial, StringComparison.Ordinal) ||
            Components.Count == 0)
            return;

        var source = _measurementSnapshot is not null && _agentSnapshot is not null
            ? "ADB + PhoneLab Agent + external measurement rig"
            : _agentSnapshot is not null
                ? "ADB + PhoneLab Agent"
                : _measurementSnapshot is not null
                    ? "ADB + external measurement rig"
                    : "ADB system information";

        try
        {
            await _diagnosticEvidenceStore.RecordSnapshotAsync(
                selected.Serial,
                _snapshot.Manufacturer,
                _snapshot.Model,
                Components.Select(component => component.Diagnostic).ToArray(),
                source);
        }
        catch (Exception ex)
        {
            StatusMessage = "تعذر حفظ سجل أدلة التشخيص المحلي: " + ex.Message;
            Notify(nameof(StatusMessage));
        }
    }

    private void DiagnosticEvidenceHistory_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireActionPermission("OpenDiagnosticEvidenceHistory", "فتح سجل أدلة الاختبارات")) return;
        var serial = SelectedDevice is { IsAuthorized: true } device ? device.Serial : null;
        new DiagnosticEvidenceHistoryWindow(_diagnosticEvidenceStore, serial)
        { Owner = this }.ShowEmbedded(this);
    }

    private void Add(string label, string value) => DeviceFacts.Add(new PropertyLine(label, value));

    private void ClearDevice(string message)
    {
        _snapshot = null; _agentSnapshot = null; _measurementSnapshot = null; _componentProfile = null;
        DeviceFacts.Clear();
        RebuildDiagnostics();
        ScanStatus = "بانتظار جهاز";
        StatusMessage = message;
        Notify(nameof(DeviceTitle)); Notify(nameof(DeviceSubtitle));
        Notify(nameof(ScanStatus)); Notify(nameof(StatusMessage));
    }

    private async Task RefreshUsbRecognitionAsync()
    {
        try
        {
            var devices = await _usbInventory.EnumeratePresentDevicesAsync();
            _lastUsbDevices = devices;
            var summary = UsbRecognitionSummary.Build(devices);
            if (string.Equals(summary, UsbRecognitionStatus, StringComparison.Ordinal)) return;
            UsbRecognitionStatus = summary;
            Notify(nameof(UsbRecognitionStatus));
        }
        catch (Exception ex)
        {
            _lastUsbDevices = Array.Empty<WindowsUsbDevice>();
            var summary = "تعذر فحص Windows USB PnP: " + ex.Message;
            if (!string.Equals(summary, UsbRecognitionStatus, StringComparison.Ordinal))
            {
                UsbRecognitionStatus = summary;
                Notify(nameof(UsbRecognitionStatus));
            }
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();

    private async void SetupUsb_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _adb.StartServerAsync();
            _deviceSignature = "\\0";
            await RefreshDevicesAsync();
            if (SelectedDevice is { IsAuthorized: true } selected)
            {
                _preparedSerials.Remove(selected.Serial);
                await EnsureSelectedDeviceReadyAsync(selected);
            }
            else
            {
                StatusMessage = "تم تشغيل ADB. فعّل USB debugging ووافق على رسالة الثقة في الهاتف.";
                Notify(nameof(StatusMessage));
            }
        }
        catch (Exception ex) { StatusMessage = "تعذر بدء ADB: " + ex.Message; Notify(nameof(StatusMessage)); }
    }

    private void InstallSamsungUsbDriver_Click(object sender, RoutedEventArgs e)
    {
        var driver = Path.Combine(AppContext.BaseDirectory, "Drivers", "Samsung_USB_Driver_v1.9.5.0.exe");
        if (!File.Exists(driver))
        {
            MessageBox.Show(this, "مثبت تعريف Samsung غير موجود داخل هذه الحزمة.", "تعريف USB", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var answer = MessageBox.Show(this,
            "هذا المثبت الرسمي مخصص لهواتف Samsung فقط.\n\n" +
            "احفظ عملك وافصل الهاتف قبل التثبيت. سيطلب Windows صلاحية المسؤول. بعد انتهاء التثبيت أعد توصيل الهاتف، افتح قفل الشاشة ووافق على USB debugging.\n\n" +
            "هل تريد تشغيل المثبت الآن؟",
            "تثبيت تعريف Samsung USB", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = driver,
                UseShellExecute = true
            });
            StatusMessage = "تم فتح مثبت تعريف Samsung. أكمل التثبيت ثم أعد توصيل الهاتف واضغط بحث USB.";
            Notify(nameof(StatusMessage));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "تعذر تشغيل مثبت التعريف: " + ex.Message, "تعريف USB", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ConnectionHelp_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "ظهور الهاتف في مستكشف الملفات يعني نقل ملفات MTP فقط، وليس اتصال ADB.\n\n" +
            "1) إعدادات الهاتف > حول الهاتف > اضغط رقم الإصدار 7 مرات.\n" +
            "2) افتح خيارات المطور وفعّل تصحيح أخطاء USB.\n" +
            "3) افتح قفل الشاشة ثم وصّل كابل بيانات.\n" +
            "4) وافق على «السماح بتصحيح أخطاء USB» واختر «السماح دائمًا من هذا الكمبيوتر».\n" +
            "5) سيُثبت PhoneLab Agent تلقائيًا ويطلب موافقة ثانية على الهاتف.\n\n" +
            "إذا استمر نقل الملفات دون ADB فقد تحتاج إلى تعريف ADB الخاص بالشركة المصنّعة أو إلغاء تصاريح USB debugging القديمة ثم إعادة التوصيل.",
            "تهيئة اتصال USB", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void WorkshopCenter_Click(object sender, RoutedEventArgs e)
    {
        var window = new WorkshopCenterWindow { Owner = this };
        window.ShowEmbedded(this);
    }
    private void ModuleNavigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string moduleId)
            return;

        // Phone Link is a persistent dock, not a full-page overlay.
        if (moduleId.Equals("phone-link", StringComparison.OrdinalIgnoreCase))
        {
            MirrorScreen_Click(this, new RoutedEventArgs());
            return;
        }

        var module = SoftBoxModuleCatalog.Find(moduleId);
        if (module is null)
        {
            StatusMessage = "تعذر العثور على تعريف القسم في كتالوج SoftBox.";
            Notify(nameof(StatusMessage));
            return;
        }

        CloseAllEmbeddedPages();
        var window = new ModuleSectionWindow(
            module,
            OpenModuleAction,
            actionId => SoftBoxAuthorizationPolicy.IsAllowed(_currentUser.Role, actionId)) { Owner = this };
        window.ShowEmbedded(this);
    }

    private async void OpenModuleAction(string actionId)
    {
        if (!SoftBoxAuthorizationPolicy.IsAllowed(_currentUser.Role, actionId))
        {
            try
            {
                await _accountService.RecordAccessDeniedAsync(_currentUser.Id, actionId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "تم منع الإجراء، لكن تعذر حفظ سجل محاولة الصلاحيات: " + ex.Message,
                    "سجل الأمان • SoftBox",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show(this,
                "حسابك الحالي لا يملك الصلاحية المطلوبة لتنفيذ هذه الأداة. تواصل مع مدير SoftBox إذا كنت تحتاج إلى هذه الصلاحية.",
                "الصلاحيات • SoftBox",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            await _accountService.RecordActionInvokedAsync(_currentUser.Id, actionId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "تعذر تسجيل العملية في سجل التدقيق المحلي، لذلك لم يتم تشغيل الأداة.\n\n" + ex.Message,
                "سجل الأمان • SoftBox",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        switch (actionId)
        {
            case "RefreshDevices":
                _ = RefreshDevicesAsync();
                break;
            case "InspectDevice":
                Inspect_Click(this, new RoutedEventArgs());
                break;
            case "UpdateDeviceCatalog":
                DeviceCatalog_Click(this, new RoutedEventArgs());
                break;
            case "OpenPhoneTests":
                PhoneTests_Click(this, new RoutedEventArgs());
                break;
            case "AndroidLogs":
                AndroidLogs_Click(this, new RoutedEventArgs());
                break;
            case "InstallSamsungDriver":
                InstallSamsungUsbDriver_Click(this, new RoutedEventArgs());
                break;
            case "OpenUsbDriverSignatureVerifier":
                new UsbDriverSignatureVerifierWindow { Owner = this }.ShowEmbedded(this);
                break;
            case "InspectFirmware":
                FirmwareInspect_Click(this, new RoutedEventArgs());
                break;
            case "UsbTransport":
                UsbTransport_Click(this, new RoutedEventArgs());
                break;
            case "OpenWorkshop":
                WorkshopCenter_Click(this, new RoutedEventArgs());
                break;
            case "MirrorScreen":
                MirrorScreen_Click(this, new RoutedEventArgs());
                break;
            case "OpenMultiScreenSessions":
                new MultiScreenSessionsWindow(_adb) { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenScreenRecording":
                OpenScreenRecordingWindow();
                break;
            case "AnalyzeScreen":
                AnalyzeScreen_Click(this, new RoutedEventArgs());
                break;
            case "MeasurementRig":
                MeasurementRig_Click(this, new RoutedEventArgs());
                break;
            case "RepairGuides":
                RepairGuides_Click(this, new RoutedEventArgs());
                break;
            case "OpenApkLab":
                new ApkLabWindow { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenNetworkDiagnostics":
            {
                var networkDevice = SelectedDevice;
                var serial = networkDevice is { IsAuthorized: true } ? networkDevice.Serial : null;
                new NetworkDiagnosticsWindow(_adb, serial) { Owner = this }.ShowEmbedded(this);
                break;
            }
            case "OpenWirelessAdbPairing":
                new WirelessAdbPairingWindow(_adb) { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenAndroidFileManager":
                OpenAndroidToolsWindow(0);
                break;
            case "OpenAndroidAppManager":
                OpenAndroidToolsWindow(1);
                break;
            case "OpenAndroidCommandCenter":
                OpenAndroidToolsWindow(2);
                break;
            case "OpenAndroidBatchTools":
                OpenAndroidToolsWindow(3);
                break;
            case "OpenAndroidServiceOperations":
                OpenAndroidToolsWindow(4);
                break;
            case "OpenAndroidWorkflow":
                OpenAndroidWorkflowWindow();
                break;
            case "OpenFastbootManager":
                new FastbootManagerWindow { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenDeviceAccessRecoveryCenter":
            {
                var accessDevice = SelectedDevice;
                var hasAuthorizedAccessDevice = accessDevice is { IsAuthorized: true };
                new DeviceAccessRecoveryWindow(
                    hasAuthorizedAccessDevice ? _adb : null,
                    hasAuthorizedAccessDevice ? accessDevice!.Serial : null,
                    accessDevice?.Manufacturer,
                    accessDevice?.Model ?? accessDevice?.DisplayName)
                { Owner = this }.ShowEmbedded(this);
                break;
            }
            case "OpenSamsungOdinAssistant":
            {
                var samsungDevice = SelectedDevice;
                var hasAuthorizedSamsung = samsungDevice is not null && samsungDevice.IsAuthorized &&
                    AndroidDeviceToolsService.NormalizeVendor(samsungDevice.Manufacturer) == AndroidVendorKind.Samsung;
                new SamsungOdinAssistantWindow(
                    hasAuthorizedSamsung ? _adb : null,
                    hasAuthorizedSamsung ? samsungDevice!.Serial : null,
                    hasAuthorizedSamsung ? samsungDevice!.Manufacturer : null,
                    hasAuthorizedSamsung ? samsungDevice!.Model ?? samsungDevice.DisplayName : null)
                { Owner = this }.ShowEmbedded(this);
                break;
            }
            case "OpenSamsungOfficialTools":
                new OemOfficialToolsWindow(OemServiceVendor.Samsung) { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenSamsungServiceOperations":
                OpenSamsungServiceOperations();
                break;
            case "OpenXiaomiFastbootService":
            {
                var xiaomiDevice = SelectedDevice;
                if (xiaomiDevice is null || !xiaomiDevice.IsAuthorized)
                {
                    ExplainNoAuthorizedDevice("Xiaomi Fastboot Service");
                    break;
                }
                if (AndroidDeviceToolsService.NormalizeVendor(xiaomiDevice.Manufacturer) != AndroidVendorKind.Xiaomi)
                {
                    MessageBox.Show(this,
                        "الجهاز المحدد ليس Xiaomi/Redmi/POCO (الشركة المقروءة: " + xiaomiDevice.Manufacturer + "). اختر هاتف Xiaomi أولًا.",
                        "Xiaomi Fastboot Service", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                }
                var manufacturer = _snapshot is not null && _snapshot.Serial.Equals(xiaomiDevice.Serial, StringComparison.Ordinal)
                    ? _snapshot.Manufacturer : xiaomiDevice.Manufacturer;
                var model = _snapshot is not null && _snapshot.Serial.Equals(xiaomiDevice.Serial, StringComparison.Ordinal)
                    ? _snapshot.Model : xiaomiDevice.DisplayName;
                new XiaomiFastbootDiagnosticsWindow(_adb, xiaomiDevice.Serial, manufacturer, model) { Owner = this }.ShowEmbedded(this);
                break;
            }
            case "OpenXiaomiOfficialTools":
                new OemOfficialToolsWindow(OemServiceVendor.Xiaomi) { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenBrandOfficialTools":
            {
                var selectedVendor = SelectedDevice?.Manufacturer ?? _snapshot?.Manufacturer ?? "";
                var vendor = selectedVendor.Contains("samsung", StringComparison.OrdinalIgnoreCase)
                    ? OemServiceVendor.Samsung
                    : selectedVendor.Contains("xiaomi", StringComparison.OrdinalIgnoreCase) ||
                      selectedVendor.Contains("redmi", StringComparison.OrdinalIgnoreCase) ||
                      selectedVendor.Contains("poco", StringComparison.OrdinalIgnoreCase)
                        ? OemServiceVendor.Xiaomi
                        : selectedVendor.Contains("google", StringComparison.OrdinalIgnoreCase) ||
                          selectedVendor.Contains("pixel", StringComparison.OrdinalIgnoreCase)
                            ? OemServiceVendor.GooglePixel
                            : selectedVendor.Contains("motorola", StringComparison.OrdinalIgnoreCase) ||
                              selectedVendor.Contains("lenovo", StringComparison.OrdinalIgnoreCase)
                                ? OemServiceVendor.MotorolaLenovo
                                : selectedVendor.Contains("qualcomm", StringComparison.OrdinalIgnoreCase)
                                    ? OemServiceVendor.Qualcomm
                                    : selectedVendor.Contains("mediatek", StringComparison.OrdinalIgnoreCase)
                                        ? OemServiceVendor.MediaTek
                                        : OemServiceVendor.OtherAndroid;
                new OemOfficialToolsWindow(vendor) { Owner = this }.ShowEmbedded(this);
                break;
            }
            case "OpenQualcommRecovery":
            {
                var recoveryDevice = SelectedDevice;
                if (recoveryDevice is null || !recoveryDevice.IsAuthorized || _snapshot is null ||
                    !_snapshot.Serial.Equals(recoveryDevice.Serial, StringComparison.Ordinal))
                {
                    MessageBox.Show(this, "اختر هاتف Qualcomm متصلًا ومصرحًا عبر ADB، ثم حدّث قراءة خصائصه قبل الانتقال إلى EDL.", "Qualcomm Recovery",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                }
                var qcomIdentity = _snapshot.Soc + " " + _snapshot.CpuDescription;
                if (!qcomIdentity.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase) &&
                    !qcomIdentity.Contains("Snapdragon", StringComparison.OrdinalIgnoreCase) &&
                    !System.Text.RegularExpressions.Regex.IsMatch(qcomIdentity, @"\b(SM\d{4,5}|MSM\d{3,4}|QCM\d{4,5})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    MessageBox.Show(this, "بيانات SoC الحالية لا تقدم دليلًا كافيًا على Qualcomm. لم يتم فتح EDL.", "Qualcomm Recovery",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                }
                new QualcommRecoveryWindow(_snapshot) { Owner = this }.ShowEmbedded(this);
                break;
            }
            case "OpenMediaTekRecovery":
            {
                var recoveryDevice = SelectedDevice;
                if (recoveryDevice is null || !recoveryDevice.IsAuthorized || _snapshot is null ||
                    !_snapshot.Serial.Equals(recoveryDevice.Serial, StringComparison.Ordinal))
                {
                    MessageBox.Show(this, "اختر هاتف MediaTek متصلًا ومصرحًا عبر ADB، ثم حدّث قراءة خصائصه قبل الانتقال إلى Preloader/BROM.", "MediaTek Recovery",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                }
                var mtkIdentity = _snapshot.Soc + " " + _snapshot.CpuDescription;
                if (!mtkIdentity.Contains("MediaTek", StringComparison.OrdinalIgnoreCase) &&
                    !mtkIdentity.Contains("MTK", StringComparison.OrdinalIgnoreCase) &&
                    !System.Text.RegularExpressions.Regex.IsMatch(mtkIdentity, @"\bMT\d{3,5}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    MessageBox.Show(this, "بيانات SoC الحالية لا تقدم دليلًا كافيًا على MediaTek. لم يتم فتح BROM/Preloader.", "MediaTek Recovery",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                }
                new MediaTekRecoveryWindow(_snapshot.ModelNumber, _snapshot.HardwareSku) { Owner = this }.ShowEmbedded(this);
                break;
            }
            case "OpenAndroidLogAnalyzer":
                if (SelectedDevice is { IsAuthorized: true } logAnalyzerDevice)
                    new AndroidLogAnalyzerWindow(_adb, logAnalyzerDevice.Serial) { Owner = this }.ShowEmbedded(this);
                else
                    ExplainNoAuthorizedDevice("محلل Logcat");
                break;
            case "OpenDeviceCapabilityCenter":
            {
                var capabilitySelectedDevice = SelectedDevice;
                if (capabilitySelectedDevice is null || _snapshot is null ||
                    !_snapshot.Serial.Equals(capabilitySelectedDevice.Serial, StringComparison.Ordinal))
                {
                    MessageBox.Show(this,
                        "اختر جهازًا، ثم نفّذ قراءة خصائصه أولًا لفتح محرك القدرات والتوافق.",
                        "محرك قدرات الجهاز", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                }

                new DeviceCapabilityCenterWindow(
                    _adb,
                    _snapshot,
                    capabilitySelectedDevice.IsAuthorized,
                    _agentSnapshot is not null)
                {
                    Owner = this
                }.ShowEmbedded(this);
                break;
            }
            case "OpenUsbDoctor":
                new UsbDoctorWindow { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenUsbDriverCenter":
            {
                var adbManufacturer = SelectedDevice?.Manufacturer ?? _snapshot?.Manufacturer;
                var usbMatch = _lastUsbDevices
                    .Select(device => new
                    {
                        Device = device,
                        Resource = OemUsbDriverCatalog.FindForUsbIdentity(
                            device.Manufacturer, device.FriendlyName, device.VendorId)
                    })
                    .FirstOrDefault(match => match.Resource is not null);

                var adbResource = OemUsbDriverCatalog.FindForManufacturer(adbManufacturer);
                var detectedResource = adbResource ?? usbMatch?.Resource;
                var manufacturer = detectedResource?.Name ?? adbManufacturer;
                var model = SelectedDevice?.Model ?? _snapshot?.Model;
                var source = adbResource is not null ? "ADB" :
                    usbMatch?.Resource is not null ? "Windows USB / VID" : "غير مؤكد";

                new UsbDriverCenterWindow(manufacturer, model, source)
                { Owner = this }.ShowEmbedded(this);
                break;
            }
            case "OpenUniversalDeviceDiscovery":
                new UniversalDeviceDiscoveryWindow(_usbInventory, Devices.ToArray())
                { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenAirPlayReceiver":
                new AirPlayReceiverWindow(
                    _settings,
                    _settingsService,
                    PhoneScreenHost,
                    async () =>
                    {
                        if (_screenMirror.IsRecording)
                            throw new InvalidOperationException("أوقف التسجيل الحالي واحفظ ملفه قبل تبديل مسار الشاشة إلى AirPlay.");
                        _screenMirror.Stop();
                        PhoneScreenHost.DetachWindow();
                        await Task.CompletedTask;
                    })
                { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenDiagnosticEvidenceHistory":
            {
                var evidenceSerial = SelectedDevice is { IsAuthorized: true } evidenceDevice ? evidenceDevice.Serial : null;
                new DiagnosticEvidenceHistoryWindow(_diagnosticEvidenceStore, evidenceSerial)
                { Owner = this }.ShowEmbedded(this);
                break;
            }
            case "OpenLockRecoveryAssistant":
                new LockRecoveryAssistantWindow(
                    SelectedDevice?.Manufacturer ?? _snapshot?.Manufacturer,
                    SelectedDevice?.Model ?? _snapshot?.Model)
                { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenQualcommUsbDiagnostics":
                new UsbDoctorWindow("qualcomm") { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenMediaTekUsbDiagnostics":
                new UsbDoctorWindow("mediatek") { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenFirmwareCenter":
            {
                var preflightDevice = SelectedDevice;
                var hasMatchingAuthorizedSnapshot = preflightDevice is { IsAuthorized: true } &&
                    _snapshot is not null &&
                    _snapshot.Serial.Equals(preflightDevice.Serial, StringComparison.Ordinal);
                new FirmwareCenterWindow(
                    hasMatchingAuthorizedSnapshot ? _snapshot : null,
                    hasMatchingAuthorizedSnapshot)
                {
                    Owner = this
                }.ShowEmbedded(this);
                break;
            }
            case "OpenDeviceHistory":
                OpenActivityCenter(0, true);
                break;
            case "OpenNotificationCenter":
                OpenActivityCenter(1, false);
                break;
            case "OpenDashboardAnalytics":
                new DashboardAnalyticsWindow(_activity) { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenSupportBundle":
                new SupportBundleWindow { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenSupportCenter":
                new SupportCenterWindow { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenUpdateCenter":
                new UpdateCenterWindow { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenHelpCenter":
                new HelpCenterWindow { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenOfflineCenter":
                new OfflineCenterWindow() { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenApplicationSettings":
                new ApplicationSettingsWindow(_settingsService, _settings, ApplyApplicationSettings) { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenAbout":
                new AboutWindow { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenUserManagement":
                new UserManagementWindow(_accountService, _currentUser) { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenPartsInventory":
                new PartsInventoryWindow() { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenSamsungDiagnostics":
                OpenVendorDiagnostics(AndroidVendorKind.Samsung, "Samsung Tools");
                break;
            case "OpenXiaomiDiagnostics":
                OpenVendorDiagnostics(AndroidVendorKind.Xiaomi, "Xiaomi Tools");
                break;
            case "OpenBrandDiagnostics":
                OpenVendorDiagnostics(AndroidVendorKind.Generic, "Android Brands");
                break;
            case "OpenPluginStore":
            {
                var pluginDevice = SelectedDevice;
                var pluginSerial = pluginDevice is { IsAuthorized: true } ? pluginDevice.Serial : null;
                new PluginStoreWindow(_adb, pluginSerial) { Owner = this }.ShowEmbedded(this);
                break;
            }
            case "OpenRepairAdvisor":
                if (_snapshot is null)
                {
                    MessageBox.Show(this, "افحص جهازًا مصرحًا به أولًا حتى تتوفر أدلة فعلية للتحليل.",
                        "مساعد الإصلاح", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    new RepairAdvisorWindow(
                        _snapshot,
                        _agentSnapshot,
                        Components.Select(component => component.Diagnostic).ToArray(),
                        SelectedDevice?.IsAuthorized == true) { Owner = this }.ShowEmbedded(this);
                }
                break;
            case "OpenOneClickDiagnostic":
                if (SelectedDevice is not { IsAuthorized: true } selected)
                {
                    MessageBox.Show(this, "اختر هاتف Android مصرحًا به عبر ADB أولًا.",
                        "الفحص الشامل", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    new OneClickDiagnosticWindow(
                        _adb,
                        selected.Serial,
                        _snapshot,
                        _agentSnapshot,
                        Components.Select(component => component.Diagnostic).ToArray()) { Owner = this }.ShowEmbedded(this);
                }
                break;
            case "OpenPhoneBackup":
                OpenAndroidBackupWindow();
                break;
            case "OpenAndroidBackupScheduler":
                OpenAndroidBackupSchedulerWindow();
                break;
            case "OpenWorkshopBackup":
                OpenWorkshopAtTab(4);
                break;
            case "OpenWorkshopBackupScheduler":
                new WorkshopBackupSchedulerWindow { Owner = this }.ShowEmbedded(this);
                break;
            case "OpenWorkshopInventory":
                OpenWorkshopAtTab(2);
                break;
            case "OpenGlobalWorkshopHistory":
                OpenGlobalWorkshopAtTab(0);
                break;
            case "OpenGlobalWorkshopReports":
                OpenGlobalWorkshopAtTab(2);
                break;
            case "OpenGlobalWorkshopSettings":
                OpenGlobalWorkshopAtTab(1);
                break;
            case "ExportDeviceReport":
                Export_Click(this, new RoutedEventArgs());
                break;
            case "ConnectionHelp":
                ConnectionHelp_Click(this, new RoutedEventArgs());
                break;
            case "AgentHelp":
                AgentHelp_Click(this, new RoutedEventArgs());
                break;
            case "UpdateAgent":
                Agent_Click(this, new RoutedEventArgs());
                break;
            case "SetupUsb":
                SetupUsb_Click(this, new RoutedEventArgs());
                break;
            case "ImportBoardMap":
                ImportBoardMap_Click(this, new RoutedEventArgs());
                break;
            case "OpenBoardView":
                OpenBoardView_Click(this, new RoutedEventArgs());
                break;
            default:
                MessageBox.Show(this,
                    "لا يوجد إجراء تنفيذي مربوط بهذا المعرف. لم يتم تشغيل أي عملية.",
                    "إجراء غير متاح", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
        }
    }


    private void OpenAndroidToolsWindow(int selectedTabIndex = 0, string? manufacturerOverride = null)
    {
        var selected = SelectedDevice;
        if (selected is null || !selected.IsAuthorized)
        {
            ExplainNoAuthorizedDevice("أدوات Android");
            return;
        }

        var authorized = Devices.Where(device => device.IsAuthorized)
            .Select(device => device.Serial).ToArray();
        var manufacturer = manufacturerOverride ??
            (_snapshot is not null && _snapshot.Serial.Equals(selected.Serial, StringComparison.Ordinal)
                ? _snapshot.Manufacturer
                : selected.Manufacturer);
        new AndroidDeviceToolsWindow(
            _adb, selected.Serial, authorized, _settings.MaximumBatchConcurrency, selectedTabIndex, manufacturer)
        { Owner = this }.ShowEmbedded(this);
    }

    private void OpenSamsungServiceOperations()
    {
        var selected = SelectedDevice;
        if (selected is null || !selected.IsAuthorized)
        {
            ExplainNoAuthorizedDevice("Samsung Service");
            return;
        }

        var manufacturer = _snapshot is not null &&
                           _snapshot.Serial.Equals(selected.Serial, StringComparison.Ordinal)
            ? _snapshot.Manufacturer
            : selected.Manufacturer;
        if (AndroidDeviceToolsService.NormalizeVendor(manufacturer) != AndroidVendorKind.Samsung)
        {
            MessageBox.Show(this,
                "لم يتم تأكيد هوية Samsung للجهاز المحدد. لم يتم فتح أو إرسال أي عملية خاصة بالشركة.",
                "Samsung Service", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        OpenAndroidToolsWindow(4, manufacturer);
    }

    private void OpenVendorDiagnostics(AndroidVendorKind expectedVendor, string sectionName)
    {
        var selected = SelectedDevice;
        if (selected is null || !selected.IsAuthorized)
        {
            ExplainNoAuthorizedDevice(sectionName);
            return;
        }

        var detectedVendor = AndroidDeviceToolsService.NormalizeVendor(selected.Manufacturer);
        if (expectedVendor != AndroidVendorKind.Generic && detectedVendor != expectedVendor)
        {
            MessageBox.Show(this,
                $"القسم المحدد هو {sectionName}، لكن الشركة التي قرأها SoftBox للجهاز الحالي هي: {selected.Manufacturer}. اختر جهاز الشركة المطلوبة أولًا.",
                sectionName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        new VendorDiagnosticsWindow(_adb, selected.Serial, selected.Manufacturer, selected.Model ?? selected.DisplayName)
        {
            Owner = this
        }.ShowEmbedded(this);
    }

    private void OpenAndroidWorkflowWindow()
    {
        var selected = SelectedDevice;
        if (selected is null || !selected.IsAuthorized)
        {
            ExplainNoAuthorizedDevice("Workflow Automation");
            return;
        }

        new AndroidWorkflowWindow(_adb, selected.Serial) { Owner = this }.ShowEmbedded(this);
    }

    private void OpenAndroidBackupWindow()
    {
        var selected = SelectedDevice;
        if (selected is null || !selected.IsAuthorized)
        {
            ExplainNoAuthorizedDevice("Smart Backup");
            return;
        }

        new AndroidBackupWindow(_adb, selected.Serial) { Owner = this }.ShowEmbedded(this);
    }

    private void OpenAndroidBackupSchedulerWindow()
    {
        var selected = SelectedDevice;
        if (selected is null || !selected.IsAuthorized)
        {
            ExplainNoAuthorizedDevice("جدولة النسخ الاحتياطي");
            return;
        }

        new BackupScheduleWindow(_backupScheduler, selected.Serial, selected.DisplayName)
        {
            Owner = this
        }.ShowEmbedded(this);
    }

    private async Task ProcessBackupSchedulesAsync()
    {
        if (_processingBackupSchedules || !_adb.IsAvailable ||
            (SoftBoxAuthorizationPolicy.PermissionsForRole(_currentUser.Role) &
             SoftBoxAccountPermission.DeviceOperations) == 0)
            return;

        _processingBackupSchedules = true;
        try
        {
            // Independent of the visible device list so scheduled jobs continue when auto-discovery UI is disabled.
            var devices = await _discovery.DiscoverAsync();
            var authorizedSerials = devices
                .Where(device => device.IsAuthorized)
                .Select(device => device.Serial)
                .ToArray();
            await _backupScheduler.ProcessAsync(authorizedSerials, waitForCompletion: true);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("SoftBox scheduled Android backup polling failed: {0}", ex.Message);
        }
        finally
        {
            _processingBackupSchedules = false;
        }
    }

    private async Task RecordDeviceHistoryAsync(
        string serial,
        string manufacturer,
        string model,
        string eventType,
        string summary,
        string evidence = "")
    {
        try
        {
            await _activity.AddDeviceHistoryAsync(serial, manufacturer, model, eventType, summary, evidence);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("SoftBox device history was not persisted: {0}", ex.Message);
        }
    }

    private async Task AddNotificationAsync(
        string category,
        string title,
        string message,
        string moduleId,
        string? serial = null)
    {
        try
        {
            await _activity.AddNotificationAsync(category, title, message, moduleId, serial);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("SoftBox notification was not persisted: {0}", ex.Message);
        }
    }

    private void ApplyApplicationSettings(PhoneLabSettings settings)
    {
        _settings = PhoneLabSettingsService.Validate(settings);
        SoftBoxLocalization.SetLanguage(_settings.UiLanguage);
        SoftBoxLocalization.Apply(this);
        Notify(nameof(LanguageToggleText));
        ApplyUiScaleVisual(_settings.UiScale);
        _timer.Interval = TimeSpan.FromSeconds(_settings.DiscoveryIntervalSeconds);
        if (_settings.AutoDiscoverDevices)
        {
            if (!_timer.IsEnabled)
                _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
        StatusMessage = "تم تطبيق إعدادات اكتشاف الأجهزة وعمليات الاتصال. التغييرات محفوظة للمرة القادمة.";
        Notify(nameof(StatusMessage));
    }

    private void ApplyUiScaleVisual(double requestedScale)
    {
        // Window sizing is controlled by WPF and the active monitor. This preference
        // changes content scale only; it must never force a fixed window size.
        var scale = Math.Clamp(requestedScale, 0.70, 1.05);
        MainLayoutRoot.LayoutTransform = new ScaleTransform(scale, scale);
        UiScaleLabel.Text = scale.ToString("P0", System.Globalization.CultureInfo.InvariantCulture);
    }

    private async void ToggleLanguage_Click(object sender, RoutedEventArgs e)
        => await SetUiLanguageAsync(_settings.UiLanguage == "ar" ? "en" : "ar");

    private async Task SetUiLanguageAsync(string language)
    {
        try
        {
            var updated = PhoneLabSettingsService.Validate(_settings with { UiLanguage = language });
            await _settingsService.SaveAsync(updated);
            _settings = updated;
            SoftBoxLocalization.SetLanguage(_settings.UiLanguage);
            SoftBoxLocalization.Apply(this);
            Notify(nameof(LanguageToggleText));
            StatusMessage = language == "en" ? "Application language set to English." : "تم تغيير لغة التطبيق إلى العربية.";
            Notify(nameof(StatusMessage));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "تعذر حفظ لغة التطبيق: " + ex.Message,
                "SoftBox", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DecreaseUiScale_Click(object sender, RoutedEventArgs e)
        => await SetUiScaleAsync(Math.Max(0.70, Math.Round(_settings.UiScale - 0.05, 2)));

    private async void IncreaseUiScale_Click(object sender, RoutedEventArgs e)
        => await SetUiScaleAsync(Math.Min(1.05, Math.Round(_settings.UiScale + 0.05, 2)));

    private async Task SetUiScaleAsync(double scale)
    {
        try
        {
            var updated = PhoneLabSettingsService.Validate(_settings with { UiScale = scale });
            await _settingsService.SaveAsync(updated);
            _settings = updated;
            ApplyUiScaleVisual(_settings.UiScale);
            StatusMessage = "تم حفظ حجم واجهة الكمبيوتر.";
            Notify(nameof(StatusMessage));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "تعذر حفظ حجم الواجهة: " + ex.Message,
                "SoftBox", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenActivityCenter(int initialTab, bool filterToSelectedDevice)
    {
        var selected = SelectedDevice;
        var serial = filterToSelectedDevice && selected is { IsAuthorized: true }
            ? selected.Serial
            : null;
        new ActivityCenterWindow(_activity, serial, initialTab) { Owner = this }.ShowEmbedded(this);
    }

    private void GlobalWorkshop_Click(object sender, RoutedEventArgs e)
    {
        var window = new GlobalWorkshopWindow { Owner = this };
        window.ShowEmbedded(this);
    }
    private void OpenWorkshopAtTab(int tabIndex)
    {
        var window = new WorkshopCenterWindow(tabIndex) { Owner = this };
        window.ShowEmbedded(this);
    }

    private void OpenGlobalWorkshopAtTab(int tabIndex)
    {
        var window = new GlobalWorkshopWindow(tabIndex) { Owner = this };
        window.ShowEmbedded(this);
    }


    private async void UsbTransport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusMessage = "جارٍ قراءة حالات ADB وFastboot المتاحة دون تغيير وضع الهاتف...";
            Notify(nameof(StatusMessage));
            var report = await new UsbTransportInspector(_adb).InspectAsync();
            var message =
                "=== ADB ===\n" + report.AdbDevices +
                "\n=== FASTBOOT ===\n" + report.FastbootDevices +
                "\n\n" + string.Join("\n\n", report.Notes);
            MessageBox.Show(this, message, "تشخيص أوضاع اتصال USB", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusMessage = "اكتمل استعلام أوضاع USB. راجع الملاحظات؛ بعض الأوضاع تحتاج تعريفات أو أدوات خاصة بالشركة.";
        }
        catch (Exception ex) { StatusMessage = "تعذر تشخيص أوضاع USB: " + ex.Message; }
        Notify(nameof(StatusMessage));
    }

    private async void AndroidLogs_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedDevice;
        if (selected is null || !selected.IsAuthorized)
        {
            StatusMessage = "اختر هاتفًا حاصلًا على تصريح ADB أولًا.";
            Notify(nameof(StatusMessage));
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "حفظ سجلات Android للقراءة والتحليل",
            Filter = "Android diagnostic log (*.txt)|*.txt",
            FileName = $"PhoneLab-Android-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            StatusMessage = "جارٍ جمع logcat وخصائص النظام والبطارية والحرارة بوضع القراءة فقط...";
            Notify(nameof(StatusMessage));
            var service = new AndroidForensicsService(_adb);
            var logs = await service.CaptureAndroidLogsAsync(selected.Serial, dialog.FileName);
            var assessment = AndroidForensicsService.AnalyzeBootLoop(logs);
            StatusMessage = "تم حفظ سجلات Android. نتائج تحليل الإقلاع مؤشرات أولية فقط.";
            MessageBox.Show(this,
                $"تم حفظ السجلات في:\n{dialog.FileName}\n\nالنتيجة الأولية: {assessment.Verdict}\n\n" +
                (assessment.Evidence.Count == 0 ? "لم تظهر أنماط مطابقة في الجزء المحفوظ." : string.Join("\n", assessment.Evidence)) +
                "\n\n" + assessment.Limitation +
                "\n\nتنبيه خصوصية: قد تحتوي السجلات على بيانات تطبيقات أو أسماء حسابات أو مسارات خاصة؛ راجع الملف قبل إرساله لأي شخص.",
                "سجلات Android وتحليل أولي", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { StatusMessage = "فشل جمع السجلات: " + ex.Message; }
        Notify(nameof(StatusMessage));
    }

    private async void FirmwareInspect_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختيار حزمة Firmware / OTA لفحصها دون تثبيت",
            Filter = "Android firmware archives|*.zip;*.bin;*.tar;*.md5;*.img|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            StatusMessage = "جارٍ حساب SHA-256 وفحص بنية الأرشيف محليًا دون تنفيذ أوامر على الهاتف...";
            Notify(nameof(StatusMessage));
            var result = await AndroidForensicsService.InspectFirmwareArchiveAsync(dialog.FileName);
            var details =
                $"الملف: {result.FileName}\nالحجم: {result.SizeBytes:N0} bytes\nSHA-256: {result.Sha256}\n" +
                $"ZIP: {result.IsZip}\nAndroid metadata: {result.HasAndroidMetadata}\nPayload: {result.HasPayload}\nUpdater script: {result.HasUpdaterScript}\n\n" +
                string.Join("\n", result.MetadataLines) + "\n\n" + result.Assessment;
            MessageBox.Show(this, details, "فحص حزمة Firmware — دون تفليش", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusMessage = "اكتمل فحص الحزمة وحساب SHA-256. لم يتم تفليش أو تعديل أي جهاز.";
        }
        catch (Exception ex) { StatusMessage = "تعذر فحص الحزمة: " + ex.Message; }
        Notify(nameof(StatusMessage));
    }

    private async void Inspect_Click(object sender, RoutedEventArgs e) => await InspectSelectedAsync();
    private async void Agent_Click(object sender, RoutedEventArgs e) => await RefreshAgentAsync();

    private void OpenScreenRecordingWindow()
    {
        if (!RequireActionPermission("OpenScreenRecording", "تسجيل شاشة الهاتف")) return;
        var selected = SelectedDevice;
        if (selected is null || !selected.IsAuthorized)
        {
            ExplainNoAuthorizedDevice("تسجيل شاشة الهاتف");
            return;
        }

        new ScreenRecordingWindow(
            _screenMirror,
            selected.Serial,
            selected.Model ?? selected.DisplayName)
        {
            Owner = this
        }.ShowEmbedded(this);
    }

    private void OpenScreenRecording()
    {
        var selected = SelectedDevice;
        if (selected is null || !selected.IsAuthorized)
        {
            MessageBox.Show(this, "اختر هاتفًا مصرحًا به عبر ADB قبل تسجيل الشاشة.",
                "تسجيل الشاشة", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_screenMirror.IsRecording)
        {
            MessageBox.Show(this, "يوجد تسجيل نشط بالفعل. أوقف التسجيل الحالي واحفظه أولًا.",
                "تسجيل الشاشة", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new ScreenRecordingWindow(_screenMirror, selected.Serial, selected.Model ?? selected.DisplayName)
        {
            Owner = this
        };
        window.ShowEmbedded(this);
    }

    private async void MirrorScreen_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireActionPermission("MirrorScreen", "عرض شاشة الهاتف")) return;
        var selected = SelectedDevice;
        if (selected is null || !selected.IsAuthorized)
        {
            StatusMessage = "اختر هاتفًا مصرحًا به. نقل الملفات MTP وحده لا يكفي لعرض الشاشة.";
            Notify(nameof(StatusMessage)); return;
        }
        try
        {
            StatusMessage = "جارٍ فتح شاشة الهاتف داخل مساحة العمل...";
            Notify(nameof(StatusMessage));
            await OpenEmbeddedMirrorAsync(selected);
            StatusMessage = "تم دمج شاشة الهاتف الحقيقية داخل PhoneLab.";
        }
        catch (Exception ex)
        {
            var report = ScreenMirrorFailureAnalyzer.Analyze(ex);
            MirrorStatus = string.Join(Environment.NewLine,
                report.Title, report.Cause, report.RecommendedAction, report.TechnicalDetail);
            StatusMessage = "تعذر تشغيل عرض الشاشة: " + report.Title + " — " + report.RecommendedAction;
            Notify(nameof(MirrorStatus));
        }
        Notify(nameof(StatusMessage));
    }


    private void StopMirror_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireActionPermission("MirrorScreen", "إيقاف عرض شاشة الهاتف")) return;
        if (_screenMirror.IsRecording)
        {
            StatusMessage = "يوجد تسجيل شاشة نشط. أوقف التسجيل واحفظ ملفه من نافذة التسجيل قبل إيقاف جلسة العرض.";
            Notify(nameof(StatusMessage));
            return;
        }

        _screenMirror.Stop();
        PhoneScreenHost.DetachWindow();
        MirrorStatus = "تم إيقاف عرض الشاشة. الجهاز ما زال متصلًا؛ اضغط تشغيل / تحديث الشاشة لإعادة الاتصال.";
        StatusMessage = "تم إيقاف جلسة عرض الشاشة فقط؛ لم يتم فصل الهاتف أو تغيير بياناته.";
        Notify(nameof(MirrorStatus));
        Notify(nameof(StatusMessage));
    }

    private void PhoneScreenRecording_Click(object sender, RoutedEventArgs e)
        => OpenScreenRecordingWindow();

    private bool RequireActionPermission(string actionId, string actionName)
    {
        if (SoftBoxAuthorizationPolicy.IsAllowed(_currentUser.Role, actionId)) return true;
        MessageBox.Show(this,
            "حساب القراءة فقط لا يملك صلاحية " + actionName + ". لم يتم إرسال أي أمر إلى الهاتف.",
            "صلاحيات SoftBox", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private async void AnalyzeScreen_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختر صورة ملتقطة بالكاميرا الفعلية لشاشة الهاتف أثناء اختبار لون موحد",
            Filter = "Screen photo|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff"
        };
        if (dialog.ShowDialog(this) != true) return;
        var options = new ScreenAnalysisOptionsWindow { Owner = this };
        if (options.ShowDialogEmbedded() != true) return;

        try
        {
            var result = await Task.Run(() => ScreenImageAnalyzer.AnalyzePhysicalScreenPhoto(dialog.FileName, options.PatternKind));
            var reportPath = dialog.FileName + ".phonelab-analysis.json";
            await File.WriteAllTextAsync(reportPath, System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            StatusMessage = $"تحليل {options.PatternKind}: {result.SuspectedOutlierPixels} مرشح (داكن {result.DarkCandidatePixels} / مضيء {result.BrightCandidatePixels} / لوني {result.ColorCandidatePixels})، تفاوت إضاءة {result.UniformityDeviationPercent:0.0}%.";
            Notify(nameof(StatusMessage));
            MessageBox.Show(this,
                $"النمط: {result.PatternKind}\nالمقاس المحلل: {result.Width} × {result.Height}\nمرشحات اختلاف: {result.SuspectedOutlierPixels}\nمرشحات داكنة: {result.DarkCandidatePixels}\nمرشحات مضيئة: {result.BrightCandidatePixels}\nمرشحات لونية: {result.ColorCandidatePixels}\nتفاوت الإضاءة: {result.UniformityDeviationPercent:0.0}%\n\n{result.Interpretation}\n\nالتقرير: {reportPath}",
                "تحليل صورة الشاشة — مرشحات أولية", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { StatusMessage = "تعذر تحليل الصورة: " + ex.Message; Notify(nameof(StatusMessage)); }
    }

    private async void OpenBoardView_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "فتح ملف BoardView / PCB أصلي",
            Filter = "BoardView / PCB|*.brd;*.brd2;*.bdv;*.bvr;*.bv;*.bv2;*.gr;*.cad;*.gcd;*.pcb;*.kicad_pcb;*.PcbDoc;*.asc;*.fz;*.tvw;*.cae;*.epro;*.epro2;*.f2b;*.farc;*.faz;*.ict;*.tgz;*.tar;*.gz;*.zip|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var format = await BoardViewNativeFormatDetector.DetectAsync(dialog.FileName);
            StatusMessage = format.ContentRecognized
                ? $"تعرّف SoftBox على صيغة الملف: {format.DisplayName}. جارٍ فتح القارئ الأصلي."
                : $"لم يطابق رأس الملف توقيعًا معروفًا ({Path.GetExtension(dialog.FileName)}). سيحاول TRACE قراءة الصيغة مباشرةً.";
            Notify(nameof(StatusMessage));
            await _traceBoardViewer.OpenAsync(dialog.FileName);
            StatusMessage = $"تم فتح {format.DisplayName} محليًا في TRACE Boardviewer. إدخال المكوّنات إلى مجسم SoftBox يتطلب خريطة JSON/CSV موثّقة ومطابقة للمراجعة؛ لم يتم اختراع إحداثيات.";
        }
        catch (Exception ex) { StatusMessage = "تعذر فتح ملف BoardView: " + ex.Message; }
        Notify(nameof(StatusMessage));
    }

    private void PhoneTests_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedDevice;
        if (selected is null || !selected.IsAuthorized) { StatusMessage = "اختر هاتفًا مصرحًا به أولًا."; Notify(nameof(StatusMessage)); return; }
        var window = new PhoneTestWindow(
            selected.DisplayName,
            command => _agentBridge.SendCommandAsync(selected.Serial, command),
            async () =>
            {
                var latest = await _agentBridge.ReadSnapshotAsync(selected.Serial);
                if (latest is not null)
                {
                    await Dispatcher.InvokeAsync(() => { _agentSnapshot = latest; ApplyDeviceFacts(); ApplyAgentFacts(latest); RebuildDiagnostics(); });
                }
                return latest;
            }, () => _agentBridge.ReadTelemetryAsync(selected.Serial)) { Owner = this };
        window.ShowEmbedded(this);
    }
    private async void ImportBoardMap_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot is null)
        {
            StatusMessage = "اقرأ هوية هاتف متصل أولًا حتى نتحقق من تطابق الخريطة معه.";
            Notify(nameof(StatusMessage));
            return;
        }
        var dialog = new OpenFileDialog
        {
            Title = "استيراد خريطة PCB / BoardView موثقة لهذا الموديل",
            Filter = "PhoneLab BoardMap JSON أو BoardView CSV (*.json;*.csv)|*.json;*.csv",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            string path;
            if (Path.GetExtension(dialog.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                var importWindow = new BoardMapCsvImportWindow(_maps, _snapshot, dialog.FileName) { Owner = this };
                if (importWindow.ShowDialogEmbedded() != true || string.IsNullOrWhiteSpace(importWindow.ImportedPath))
                    return;
                path = importWindow.ImportedPath;
            }
            else
            {
                path = await _maps.ImportVerifiedMapAsync(dialog.FileName, _snapshot);
            }

            _boardMap = await _maps.LoadBestMapAsync(_snapshot, _profileMatch.Profile);
            Notify(nameof(BoardMapNotice));
            BuildScene();
            StatusMessage = "تم استيراد خريطة الجهاز المطابقة: " + Path.GetFileName(path);
            MessageBox.Show(this,
                "تم استيراد الخريطة بعد مطابقة هوية الموديل والمراجعة والمصدر وبنية الإحداثيات. " +
                "يحسب SoftBox بصمة SHA-256 للملف؛ مسؤولية التحقق من أصل المصدر والترخيص ودقة الإحداثيات تظل على الفني المراجع.",
                "تم استيراد خريطة PCB", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = "رفض استيراد الخريطة: " + ex.Message;
            MessageBox.Show(this, StatusMessage, "تعذر استيراد الخريطة", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Notify(nameof(StatusMessage));
    }

    private async void DeviceCatalog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var count = await _deviceProfiles.UpdateAsync();
            if (_snapshot is not null)
            {
                _profileMatch = await _deviceProfiles.MatchAsync(_snapshot);
                _boardMap = await _maps.LoadBestMapAsync(_snapshot, _profileMatch.Profile);
                ApplyDeviceFacts();
                Notify(nameof(DeviceSubtitle)); Notify(nameof(BoardMapNotice));
                BuildScene();
            }
            StatusMessage = $"تم تحديث قاعدة هوية الأجهزة: {count} ملف موديل.";
        }
        catch (Exception ex) { StatusMessage = "تعذر تحديث قاعدة الموديلات: " + ex.Message; }
        Notify(nameof(StatusMessage));
    }

    private void MeasurementRig_Click(object sender, RoutedEventArgs e)
    {
        var window = new MeasurementWindow { Owner = this };
        window.SampleReceived += sample => Dispatcher.InvokeAsync(() =>
        {
            _measurementSnapshot = sample;
            ApplyDeviceFacts();
            Add("قياس USB الخارجي", sample.UsbVoltageV is null ? "غير متاح" : $"{sample.UsbVoltageV:0.000} V");
            Add("جهد البطارية الخارجي", sample.BatteryVoltageV is null ? "غير متاح" : $"{sample.BatteryVoltageV:0.000} V");
            Add("تيار البطارية الخارجي", sample.BatteryCurrentA is null ? "غير متاح" : $"{sample.BatteryCurrentA:0.000} A");
            Add("سعة البطارية من جهاز القياس", sample.BatteryCapacityMah is null ? "غير متاحة" : $"{sample.BatteryCapacityMah:0} mAh");
            Add("المقاومة الداخلية للبطارية", sample.BatteryInternalResistanceMilliOhm is null ? "غير متاحة" : $"{sample.BatteryInternalResistanceMilliOhm:0.0} mΩ");
            RebuildDiagnostics();
            StatusMessage = "تم ربط عينة القياس الخارجية بمكونات USB والبطارية ودوائر الطاقة.";
            Notify(nameof(StatusMessage));
        });
        window.ShowEmbedded(this);
    }

    private async void RepairGuides_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot is null) { StatusMessage = "وصّل هاتفًا واقرأ هويته أولًا."; Notify(nameof(StatusMessage)); return; }
        try
        {
            var query = $"{_snapshot.Manufacturer} {_snapshot.ModelNumber} {_snapshot.Model} phone repair";
            var results = await _repairKnowledge.SearchAsync(query);
            if (results.Count == 0) { StatusMessage = "لم يعثر مصدر الإصلاح على دليل مطابق."; Notify(nameof(StatusMessage)); return; }
            var first = results[0];
            var list = string.Join("\n", results.Take(6).Select((x, i) => $"{i + 1}. [{x.Kind}] {x.Title}"));
            var answer = MessageBox.Show(this, $"نتائج أدلة إصلاح خارجية للموديل:\n\n{list}\n\nفتح أول نتيجة؟", "Repair Knowledge", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo(first.Url) { UseShellExecute = true });
            StatusMessage = $"تم العثور على {results.Count} نتيجة من iFixit. الروابط تفتح المصدر الأصلي.";
        }
        catch (Exception ex) { StatusMessage = "تعذر البحث في أدلة الإصلاح: " + ex.Message; }
        Notify(nameof(StatusMessage));
    }

    private async Task SyncDeviceCatalogQuietlyAsync()
    {
        try
        {
            var count = await _deviceProfiles.UpdateAsync();
            if (_snapshot is not null)
            {
                _profileMatch = await _deviceProfiles.MatchAsync(_snapshot);
                _boardMap = await _maps.LoadBestMapAsync(_snapshot, _profileMatch.Profile);
                ApplyDeviceFacts();
                Notify(nameof(DeviceSubtitle)); Notify(nameof(BoardMapNotice));
                BuildScene();
            }
            StatusMessage = $"قاعدة الموديلات محدثة تلقائيًا ({count} ملف).";
            Notify(nameof(StatusMessage));
        }
        catch
        {
            // Keep the bundled/offline catalog. Manual update reports the network error.
        }
    }

    private void Devices_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressDeviceSelection) return;
        var selected = SelectedDevice;
        if (selected is null) return;
        if (!selected.IsAuthorized)
        {
            ClearDevice(selected.StateText);
            StatusMessage = "الهاتف غير مصرح به عبر ADB. افتح قفل الشاشة ووافق على رسالة USB debugging.";
            Notify(nameof(StatusMessage)); return;
        }
        _ = EnsureSelectedDeviceReadyAsync(selected);
    }


    private void Components_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        SelectedComponent = ComponentsList.SelectedItem as ComponentViewModel;
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot is null)
        {
            StatusMessage = "لا يوجد تقرير: اقرأ بيانات هاتف أولًا.";
            Notify(nameof(StatusMessage)); return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "حفظ تقرير PhoneLab",
            Filter = "PhoneLab JSON report (*.json)|*.json",
            FileName = $"PhoneLab-{_snapshot.Manufacturer}-{_snapshot.Model}-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        await _reports.ExportAsync(dialog.FileName, _snapshot, _agentSnapshot, Components.Select(x => x.Diagnostic).ToList(), _measurementSnapshot);
        if (SelectedDevice is { IsAuthorized: true } selected)
        {
            await RecordDeviceHistoryAsync(
                selected.Serial, _snapshot.Manufacturer, _snapshot.Model,
                "ReportExported", "تم تصدير تقرير تشخيص محلي بموافقة المستخدم.",
                Path.GetFileName(dialog.FileName));
            await AddNotificationAsync(
                "report", "تم تصدير تقرير", $"تم حفظ تقرير الجهاز {selected.DisplayName}.",
                "service-reports", selected.Serial);
        }
        StatusMessage = "تم حفظ التقرير: " + dialog.FileName;
        Notify(nameof(StatusMessage));
    }

    private void AgentHelp_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "1) ثبّت تطبيق SoftBox PhoneLab Agent على هاتف Android.\n" +
            "2) افتح التطبيق وامنحه صلاحية الميكروفون عند اختبار الميكروفون.\n" +
            "3) اترك التطبيق مفتوحًا في الواجهة الأمامية.\n" +
            "4) في Windows اضغط «تحديث Agent» بعد قراءة الهاتف.\n\n" +
            "يستخدم الاتصال ADB forward إلى 127.0.0.1:27831. لا يتم رفع بيانات الهاتف إلى خادم خارجي.",
            "PhoneLab Agent", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BuildScene()
    {
        if (PhoneViewport is null) return;
        PhoneViewport.Children.Clear();
        _visualParts.Clear();
        PhoneViewport.Camera = CreateCamera();
        PhoneViewport.Children.Add(new ModelVisual3D
        {
            Content = new Model3DGroup
            {
                Children =
                {
                    new AmbientLight(Color.FromRgb(120,140,168)),
                    new DirectionalLight(Colors.White,new Vector3D(-1,-2,-3)),
                    new DirectionalLight(Color.FromRgb(68,145,205),new Vector3D(2,1,-1)),
                    new DirectionalLight(Color.FromRgb(200,210,225),new Vector3D(1,2,3))
                }
            }
        });

        var state = Components.ToDictionary(x => x.Id, x => x.StatusHex, StringComparer.OrdinalIgnoreCase);
        var glassLayers = new List<(BoardPart Part, string Color, double Opacity)>();

        foreach (var originalPart in _boardMap.Parts)
        {
            if (!_showDisplayLayer && IsDisplayLayer(originalPart.Id)) continue;

            var part = originalPart;
            if (_boardMap.SchemaVersion >= 2 && _boardMap.CoordinateUnit.Equals("mm", StringComparison.OrdinalIgnoreCase))
            {
                const double millimeterToScene = 0.1;
                part = originalPart with
                {
                    X = (originalPart.X - _boardMap.BoardWidthMm / 2) * millimeterToScene,
                    Y = (originalPart.Y - _boardMap.BoardHeightMm / 2) * millimeterToScene,
                    Z = originalPart.Side.Equals("Bottom", StringComparison.OrdinalIgnoreCase)
                        ? -Math.Max(Math.Abs(originalPart.Z), 0.35)
                        : Math.Max(Math.Abs(originalPart.Z), 0.35),
                    Width = originalPart.Width * millimeterToScene,
                    Height = originalPart.Height * millimeterToScene,
                    Depth = Math.Clamp(originalPart.Depth * millimeterToScene, 0.02, 0.12)
                };
            }

            // Real exploded view: every layer is lifted apart so nothing hides the parts below it.
            part = part with { Z = part.Z + ExplodeOffset(part.Id) * _explode };

            var color = state.GetValueOrDefault(originalPart.Id, originalPart.BaseColor);
            if (originalPart.Id == "frame") color = originalPart.BaseColor;
            if (originalPart.Id == _selectedComponent?.Id) color = Lighten(color);

            var opacity = GlassOpacity(originalPart.Id);
            if (opacity < 1)
            {
                glassLayers.Add((part, color, opacity)); // transparent layers are drawn last
                continue;
            }
            var visual = CreateBox(part, color);
            PhoneViewport.Children.Add(visual);
            _visualParts[visual] = part.Id;
        }

        var rearCover = CreateBox(new BoardPart
        {
            Id = "rear-cover", X = 0, Y = 0,
            Z = -0.68 + ExplodeOffset("rear-cover") * _explode,
            Width = 6.55, Height = 13.15, Depth = 0.10, BaseColor = "#1A314B"
        }, "#2B4A6B", 0.85);
        PhoneViewport.Children.Add(rearCover);

        foreach (var (part, color, opacity) in glassLayers)
        {
            var visual = CreateBox(part, color, opacity);
            PhoneViewport.Children.Add(visual);
            _visualParts[visual] = part.Id;
        }
        UpdateCamera();
    }

    private static bool IsDisplayLayer(string id) => id is "display" or "touch";

    private static double GlassOpacity(string id) => id switch
    {
        "display" => 0.42,
        "touch" => 0.22,
        "frame" => 0.55,
        _ => 1.0
    };

    private static double ExplodeOffset(string id) => id switch
    {
        "display" => 3.6,
        "touch" => 2.7,
        "front-camera" => 2.7,
        "mainboard" or "processor" or "ram" or "storage" or "pmic" or "charge-ic" or "rf" or "sensors" or "rear-camera" => 1.1,
        "battery" => -0.9,
        "frame" => -1.9,
        "rear-cover" => -3.6,
        "usb-port" or "speaker" or "microphone" or "vibrator" or "charging-board" or "sim" => -0.5,
        _ => 0
    };

    private static ModelVisual3D CreateBox(BoardPart part, string colorHex, double opacity = 1.0)
    {
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection
            {
                new(-0.5,-0.5,-0.5),new(0.5,-0.5,-0.5),new(0.5,0.5,-0.5),new(-0.5,0.5,-0.5),
                new(-0.5,-0.5,0.5),new(0.5,-0.5,0.5),new(0.5,0.5,0.5),new(-0.5,0.5,0.5)
            },
            TriangleIndices = new Int32Collection { 0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,1,2,6,1,6,5,2,3,7,2,7,6,3,0,4,3,4,7 }
        };
        var brush = new SolidColorBrush(ParseColor(colorHex)) { Opacity = opacity };
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(brush));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(120, 150, 185)), 24));
        var geometry = new GeometryModel3D(mesh, material) { BackMaterial = new DiffuseMaterial(brush) };
        var transform = new Transform3DGroup();
        transform.Children.Add(new RotateTransform3D(
            new AxisAngleRotation3D(new Vector3D(0, 0, 1), part.RotationDegrees)));
        transform.Children.Add(new ScaleTransform3D(part.Width, part.Height, part.Depth));
        transform.Children.Add(new TranslateTransform3D(part.X, part.Y, part.Z));
        return new ModelVisual3D { Content = geometry, Transform = transform };
    }

    private void Explode_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _explode = Math.Clamp(e.NewValue / 100d, 0, 1);
        if (IsLoaded) BuildScene();
    }

    private void DisplayLayer_Changed(object sender, RoutedEventArgs e)
    {
        _showDisplayLayer = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
        if (IsLoaded) BuildScene();
    }

    private void ResetView_Click(object sender, RoutedEventArgs e)
    {
        _yaw = -32; _pitch = 23; _zoom = 30; _explode = 0.8;
        _showDisplayLayer = true;
        if (ExplodeSlider is not null) ExplodeSlider.Value = 80;
        if (DisplayLayerCheck is not null) DisplayLayerCheck.IsChecked = true;
        BuildScene();
    }

    private void Viewport_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2) ResetView_Click(sender, e);
    }

    private PerspectiveCamera CreateCamera()
    {
        var camera = new PerspectiveCamera();
        UpdateCamera(camera);
        return camera;
    }

    private void UpdateCamera()
    {
        if (PhoneViewport.Camera is PerspectiveCamera camera) UpdateCamera(camera);
    }

    private void UpdateCamera(PerspectiveCamera camera)
    {
        var yaw = _yaw * Math.PI / 180d;
        var pitch = _pitch * Math.PI / 180d;
        var x = _zoom * Math.Cos(pitch) * Math.Sin(yaw);
        var y = -_zoom * Math.Cos(pitch) * Math.Cos(yaw);
        var z = _zoom * Math.Sin(pitch);
        camera.Position = new Point3D(x,y,z);
        camera.LookDirection = new Vector3D(-x,-y,-z);
        camera.UpDirection = new Vector3D(0,0,1);
        camera.FieldOfView = 38;
    }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _dragging = true;
        _dragOrigin = e.GetPosition(PhoneViewport);
        _lastPointer = _dragOrigin;
        PhoneViewport.CaptureMouse();
    }
    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(PhoneViewport);
        var delta = point - _lastPointer;
        _lastPointer = point;
        _yaw += delta.X * 0.7;
        _pitch = Math.Clamp(_pitch - delta.Y * 0.55, 7, 78);
        UpdateCamera();
    }
    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        var point = e.GetPosition(PhoneViewport);
        var moved = (point - _dragOrigin).Length;
        _dragging = false;
        PhoneViewport.ReleaseMouseCapture();
        if (moved < 5)
        {
            var hit = VisualTreeHelper.HitTest(PhoneViewport, point) as RayMeshGeometry3DHitTestResult;
            if (hit?.VisualHit is ModelVisual3D visual && _visualParts.TryGetValue(visual, out var id))
                ComponentsList.SelectedItem = Components.FirstOrDefault(x => x.Id == id);
        }
    }
    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _zoom = Math.Clamp(_zoom + (e.Delta > 0 ? -1.5 : 1.5), 14, 70);
        UpdateCamera();
    }

    private static Brush MakeBrush(string hex) => new SolidColorBrush(ParseColor(hex));
    private static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.Gray; }
    }
    private static string Lighten(string hex)
    {
        var c = ParseColor(hex);
        return Color.FromRgb((byte)Math.Min(255,c.R+42),(byte)Math.Min(255,c.G+42),(byte)Math.Min(255,c.B+42)).ToString();
    }
    private string ComponentKnowledgeText(string partId)
    {
        if (_componentProfile is null)
            return "\n\nقاعدة المكونات: لا توجد بيانات موثّقة لهذا الموديل بعد.";
        var record = _componentProfile.Find(partId);
        if (record is null || record.Confidence.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            return "\n\nقاعدة المكونات (" + _componentProfile.DisplayName + "): هذا الجزء غير موثّق — " +
                   (record?.Details is { Length: > 0 } d ? d : "لن يعرض SoftBox رقم قطعة غير مؤكد.");
        var text = "\n\nالمواصفات الموثقة (" + _componentProfile.DisplayName + "): " + record.Value;
        if (!string.IsNullOrWhiteSpace(record.Details)) text += "\n" + record.Details;
        return text + "\nدرجة التأكد: " + record.ConfidenceLabel;
    }

    private string ComponentSourcesText(string partId)
    {
        var record = _componentProfile?.Find(partId);
        if (_componentProfile is null || record is null) return "";
        var names = _componentProfile.SourcesFor(record).Select(source => source.Name).ToList();
        return names.Count == 0 ? "" : "\nالمصادر: " + string.Join(" • ", names);
    }

    private void NotifySelectedPart()
    {
        Notify(nameof(SelectedPartName)); Notify(nameof(SelectedPartStatus));
        Notify(nameof(SelectedPartBrush)); Notify(nameof(SelectedPartDescription));
        Notify(nameof(SelectedPartEvidence));
    }
    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
