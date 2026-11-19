using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using SoftBox.PhoneLab.Core.Diagnostics;
using SoftBox.PhoneLab.Core.Models;
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
    private readonly PhoneDiscoveryService _discovery;
    private readonly DeviceInspectionEngine _inspection;
    private readonly AgentBridgeClient _agentBridge;
    private readonly BoardMapCatalog _maps = new();
    private readonly DeviceProfileCatalog _deviceProfiles = new();
    private readonly ScreenMirrorService _screenMirror = new();
    private readonly RepairKnowledgeService _repairKnowledge = new();
    private readonly ReportService _reports = new();
    private readonly DispatcherTimer _timer;
    private bool _busy;
    private bool _dragging;
    private Point _dragOrigin;
    private Point _lastPointer;
    private double _yaw = -32;
    private double _pitch = 23;
    private double _zoom = 25;
    private string _deviceSignature = "";
    private DeviceSnapshot? _snapshot;
    private AgentSnapshot? _agentSnapshot;
    private BoardMap _boardMap = new();
    private DeviceProfileMatch _profileMatch = DeviceProfileMatch.Unknown;
    private ComponentViewModel? _selectedComponent;
    private readonly Dictionary<ModelVisual3D, string> _visualParts = new();

    public ObservableCollection<DeviceListItem> Devices { get; } = [];
    public ObservableCollection<ComponentViewModel> Components { get; } = [];
    public ObservableCollection<PropertyLine> DeviceFacts { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public string EngineStatus => _adb.IsAvailable ? "USB / ADB ENGINE READY" : "ADB TOOL MISSING";
    public string DiscoveryHint => _adb.IsAvailable
        ? "وصّل Android بكابل بيانات وفعّل USB debugging. وافق على رسالة الثقة في الهاتف."
        : "ثبّت Android SDK Platform-Tools، ثم ضع adb.exe في PATH أو Tools/platform-tools.";
    public string DeviceTitle => _snapshot is null ? SelectedDevice?.DisplayName ?? "لا يوجد هاتف متصل" : $"{_snapshot.Manufacturer} {_snapshot.Model}".Trim();
    public string DeviceSubtitle => _snapshot is null
        ? "بانتظار جهاز Android عبر USB"
        : $"Android {_snapshot.AndroidVersion} • {_snapshot.ModelNumber} • {_snapshot.DeviceCode}" +
          (_profileMatch.Profile is null ? " • قاعدة الموديلات: غير مدرج" : $" • {_profileMatch.DisplayName} ({_profileMatch.Confidence})");
    public string ScanStatus { get; private set; } = "بانتظار جهاز";
    public string StatusMessage { get; private set; } = "PhoneLab جاهز لاكتشاف أجهزة Android المصرّح بها.";
    public string BoardMapNotice => _boardMap.AccuracyNotice;
    public string AgentStatus { get; private set; } = "لم تتم مزامنة تطبيق PhoneLab Agent بعد.";
    public string SelectedPartName => _selectedComponent?.Name ?? "اختر قطعة من المجسم";
    public string SelectedPartStatus => _selectedComponent?.StatusLabel ?? "لم تختبر";
    public Brush SelectedPartBrush => _selectedComponent?.StatusBrush ?? MakeBrush("#64748B");
    public string SelectedPartDescription => _selectedComponent?.Description ?? "تظهر معلومات الجزء وسبب حالته هنا.";
    public string SelectedPartEvidence => _selectedComponent?.Evidence ?? "لا توجد أدلة اختبار بعد.";

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

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _discovery = new PhoneDiscoveryService(_adb);
        _inspection = new DeviceInspectionEngine(_adb);
        _agentBridge = new AgentBridgeClient(_adb);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) => await RefreshDevicesAsync();
        Loaded += async (_, _) =>
        {
            await _deviceProfiles.LoadAsync();
            _boardMap = await _maps.LoadBestMapAsync(new DeviceSnapshot(), null);
            Notify(nameof(BoardMapNotice));
            BuildScene();
            await RefreshDevicesAsync();
            _timer.Start();
            _ = SyncDeviceCatalogQuietlyAsync();
        };
        Closed += (_, _) => _timer.Stop();
        Notify(nameof(EngineStatus));
        Notify(nameof(DiscoveryHint));
        BuildScene();
    }

    public async Task RefreshDevicesAsync()
    {
        if (_busy || !_adb.IsAvailable) return;
        try
        {
            var list = await _discovery.DiscoverAsync();
            var signature = string.Join("|", list.Select(x => $"{x.Serial}:{x.State}"));
            if (signature == _deviceSignature) return;
            _deviceSignature = signature;
            var previous = SelectedDevice?.Serial;
            Devices.Clear();
            foreach (var device in list)
            {
                var item = device;
                if (device.IsAuthorized)
                {
                    try
                    {
                        var model = await _adb.RunForDeviceAsync(device.Serial, "shell", "getprop", "ro.product.model");
                        var maker = await _adb.RunForDeviceAsync(device.Serial, "shell", "getprop", "ro.product.manufacturer");
                        item = device with { Model = model.Trim(), Manufacturer = maker.Trim() };
                    }
                    catch { }
                }
                Devices.Add(item);
            }
            DevicesList.SelectedItem = Devices.FirstOrDefault(x => x.Serial == previous)
                ?? Devices.FirstOrDefault(x => x.IsAuthorized) ?? Devices.FirstOrDefault();
            if (Devices.Count == 0)
            {
                ClearDevice("لم يتم العثور على جهاز ADB. تأكد من الكابل وتعريف USB وموافقة الهاتف.");
            }
            StatusMessage = Devices.Count == 0 ? "لا يوجد جهاز مصرح." : $"تم اكتشاف {Devices.Count} جهاز/أجهزة.";
            Notify(nameof(StatusMessage));
        }
        catch (Exception ex)
        {
            StatusMessage = "فشل اكتشاف USB/ADB: " + ex.Message;
            Notify(nameof(StatusMessage));
        }
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
            _agentSnapshot = null;
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
            AgentStatus = "اقرأ بيانات الهاتف أولًا، ثم افتح PhoneLab Agent على الهاتف.";
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
        DeviceFacts.Clear();
        Add("الشركة", _snapshot.Manufacturer);
        Add("الموديل", _snapshot.Model);
        Add("رقم الموديل", _snapshot.ModelNumber);
        Add("Product / Device", $"{_snapshot.Product} / {_snapshot.DeviceCode}");
        Add("Hardware SKU", _snapshot.HardwareSku);
        Add("Build fingerprint", _snapshot.BuildFingerprint);
        Add("Bootloader", _snapshot.Bootloader);
        Add("Board / Hardware", _snapshot.Board);
        Add("قاعدة الموديلات", _profileMatch.Profile is null ? "غير مدرج — تم استخدام معرفات النظام" : $"{_profileMatch.DisplayName} • {_profileMatch.Confidence} • Score {_profileMatch.Score}");
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
        foreach (var camera in agent.Cameras)
        {
            var facing = camera.Facing switch { "front" => "أمامية", "back" => "خلفية", "external" => "خارجية", _ => "غير محدد" };
            Add($"Camera {camera.Id}", $"{facing} • {camera.HardwareLevel} • {camera.SensorResolution} • Focal {string.Join("/", camera.FocalLengthsMm.Select(x => x.ToString("0.0")))} mm • Flash {camera.HasFlash}");
            if (camera.PhysicalCameraIds.Length > 0)
                Add($"Physical IDs {camera.Id}", string.Join(", ", camera.PhysicalCameraIds));
        }
    }

    private void RebuildDiagnostics()
    {
        Components.Clear();
        foreach (var diagnostic in DiagnosticEngine.Build(_snapshot, _agentSnapshot))
            Components.Add(new ComponentViewModel(diagnostic));
        ComponentsList.SelectedItem = Components.FirstOrDefault(x => x.Id == _selectedComponent?.Id)
            ?? Components.FirstOrDefault();
        BuildScene();
    }

    private void Add(string label, string value) => DeviceFacts.Add(new PropertyLine(label, value));

    private void ClearDevice(string message)
    {
        _snapshot = null; _agentSnapshot = null;
        DeviceFacts.Clear();
        RebuildDiagnostics();
        ScanStatus = "بانتظار جهاز";
        StatusMessage = message;
        Notify(nameof(DeviceTitle)); Notify(nameof(DeviceSubtitle));
        Notify(nameof(ScanStatus)); Notify(nameof(StatusMessage));
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();
    private async void Inspect_Click(object sender, RoutedEventArgs e) => await InspectSelectedAsync();
    private async void Agent_Click(object sender, RoutedEventArgs e) => await RefreshAgentAsync();

    private async void MirrorScreen_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedDevice;
        if (selected is null || !selected.IsAuthorized) { StatusMessage = "اختر هاتفًا مصرحًا به أولًا."; Notify(nameof(StatusMessage)); return; }
        try
        {
            StatusMessage = "جارٍ تجهيز scrcpy الرسمي والتحقق من SHA-256...";
            Notify(nameof(StatusMessage));
            await _screenMirror.StartAsync(selected.Serial, _snapshot?.Model ?? selected.DisplayName);
            StatusMessage = "تم تشغيل عرض شاشة الهاتف الحقيقية في نافذة مستقلة.";
        }
        catch (Exception ex) { StatusMessage = "تعذر تشغيل عرض الشاشة: " + ex.Message; }
        Notify(nameof(StatusMessage));
    }

    private async void AnalyzeScreen_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختر صورة ملتقطة بالكاميرا الفعلية لشاشة الهاتف أثناء اختبار لون موحد",
            Filter = "Screen photo|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var result = await Task.Run(() => ScreenImageAnalyzer.AnalyzePhysicalScreenPhoto(dialog.FileName));
            var reportPath = dialog.FileName + ".phonelab-analysis.json";
            await File.WriteAllTextAsync(reportPath, System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            StatusMessage = $"تحليل الصورة انتهى: {result.SuspectedOutlierPixels} بكسل مرشح، تفاوت إضاءة {result.UniformityDeviationPercent:0.0}%.";
            Notify(nameof(StatusMessage));
            MessageBox.Show(this,
                $"المقاس: {result.Width} × {result.Height}\nمرشحات اختلاف بكسل: {result.SuspectedOutlierPixels}\nتفاوت الإضاءة بين المناطق: {result.UniformityDeviationPercent:0.0}%\n\n{result.Interpretation}\n\nالتقرير: {reportPath}",
                "تحليل صورة الشاشة — نتيجة أولية", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { StatusMessage = "تعذر تحليل الصورة: " + ex.Message; Notify(nameof(StatusMessage)); }
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
        => new MeasurementWindow { Owner = this }.Show();

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
        var selected = SelectedDevice;
        if (selected is null) return;
        if (!selected.IsAuthorized)
        {
            ClearDevice(selected.StateText);
            return;
        }
        _ = InspectSelectedAsync();
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
        await _reports.ExportAsync(dialog.FileName, _snapshot, _agentSnapshot, Components.Select(x => x.Diagnostic).ToList());
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
                    new AmbientLight(Color.FromRgb(165,185,210)),
                    new DirectionalLight(Colors.White,new Vector3D(-1,-2,-3)),
                    new DirectionalLight(Color.FromRgb(68,145,205),new Vector3D(2,1,-1))
                }
            }
        });

        var state = Components.ToDictionary(x => x.Id, x => x.StatusHex, StringComparer.OrdinalIgnoreCase);
        foreach (var part in _boardMap.Parts)
        {
            var color = state.GetValueOrDefault(part.Id, part.BaseColor);
            if (part.Id == "frame") color = part.BaseColor;
            if (part.Id == _selectedComponent?.Id) color = Lighten(color);
            var visual = CreateBox(part, color);
            PhoneViewport.Children.Add(visual);
            _visualParts[visual] = part.Id;
        }

        var rearCover = CreateBox(new BoardPart
        {
            Id = "rear-cover", X = 0, Y = 0, Z = -0.68, Width = 6.55, Height = 13.15, Depth = 0.10, BaseColor = "#1A314B"
        }, "#1A314B");
        PhoneViewport.Children.Add(rearCover);
        UpdateCamera();
    }

    private static ModelVisual3D CreateBox(BoardPart part, string colorHex)
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
        var material = new DiffuseMaterial(new SolidColorBrush(ParseColor(colorHex)));
        var geometry = new GeometryModel3D(mesh, material) { BackMaterial = material };
        var transform = new Transform3DGroup();
        transform.Children.Add(new ScaleTransform3D(part.Width, part.Height, part.Depth));
        transform.Children.Add(new TranslateTransform3D(part.X, part.Y, part.Z));
        return new ModelVisual3D { Content = geometry, Transform = transform };
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
        camera.FieldOfView = 42;
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
        _zoom = Math.Clamp(_zoom + (e.Delta > 0 ? -1.2 : 1.2), 16, 42);
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
    private void NotifySelectedPart()
    {
        Notify(nameof(SelectedPartName)); Notify(nameof(SelectedPartStatus));
        Notify(nameof(SelectedPartBrush)); Notify(nameof(SelectedPartDescription));
        Notify(nameof(SelectedPartEvidence));
    }
    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
