using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Plugins;
using SoftBox.PhoneLab.Core.Workflow;
using SoftBox.PhoneLab.Desktop.Services;
using System.IO;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Local plugin package inspector, signed workflow runner and isolated native DLL launcher.
/// Native code runs only in a temporary Windows AppContainer worker.
/// </summary>
public sealed class PluginStoreWindow : Window
{
    private readonly PluginPublisherTrustStore _trustStore = new();
    private readonly PluginPackageInspector _inspector;
    private readonly PluginWorkflowRunner _workflowRunner;
    private readonly NativePluginRuntimeService _nativeRuntime;
    private readonly AdbClient? _adb;
    private readonly string? _selectedSerial;
    private readonly TextBox _path = new();
    private readonly TextBox _nativeRequest = new();
    private readonly TextBox _report = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _libraryLocation = new();
    private readonly ListBox _libraryList = new();
    private readonly ObservableCollection<PackageLibraryItem> _libraryItems = [];
    private readonly string _libraryDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SoftBox", "PhoneLab", "PluginPackages");
    private readonly Button _inspectButton;
    private readonly Button _saveButton;
    private readonly Button _copyToLibraryButton;
    private readonly Button _refreshLibraryButton;
    private readonly Button _trustPublisherButton;
    private readonly Button _signPackageButton;
    private readonly Button _runWorkflowButton;
    private readonly Button _runNativeButton;
    private readonly Button _testSandboxButton;
    private readonly Button _removeLibraryButton;
    private PluginPackageInspection? _inspection;

    public PluginStoreWindow(AdbClient? adb = null, string? selectedSerial = null)
    {
        _adb = adb;
        _selectedSerial = !string.IsNullOrWhiteSpace(selectedSerial) && AdbClient.IsSafeSerial(selectedSerial)
            ? selectedSerial
            : null;
        _inspector = new PluginPackageInspector(_trustStore);
        _workflowRunner = new PluginWorkflowRunner(_inspector);
        _nativeRuntime = new NativePluginRuntimeService(_inspector);
        Title = "SoftBox | متجر الإضافات — فحص وWorkflow وAppContainer";
        Width = 1020;
        Height = 760;
        MinWidth = 800;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _inspectButton = ButtonControl("فحص حزمة Plugin", "#14517A", Inspect_Click);
        _saveButton = ButtonControl("حفظ تقرير الفحص JSON", "#176044", Save_Click);
        _copyToLibraryButton = ButtonControl("تثبيت الحزمة في المكتبة", "#243B56", CopyToLibrary_Click);
        _refreshLibraryButton = ButtonControl("تحديث المكتبة", "#243B56", RefreshLibrary_Click);
        _trustPublisherButton = ButtonControl("إضافة مفتاح ناشر موثوق", "#704B20", TrustPublisher_Click);
        _signPackageButton = ButtonControl("توقيع الحزمة بمفتاح الناشر", "#704B20", SignPackage_Click);
        _runWorkflowButton = ButtonControl("تشغيل Workflow موقّع", "#176044", RunWorkflow_Click);
        _runNativeButton = ButtonControl("تشغيل DLL معزول AppContainer", "#704B20", RunNative_Click);
        _testSandboxButton = ButtonControl("اختبار بيئة العزل", "#28435D", TestSandbox_Click);
        _removeLibraryButton = ButtonControl("إلغاء تثبيت الحزمة", "#713B43", RemoveLibrary_Click);
        _saveButton.IsEnabled = false;
        _copyToLibraryButton.IsEnabled = false;
        _runWorkflowButton.IsEnabled = false;
        _runNativeButton.IsEnabled = false;
        _testSandboxButton.IsEnabled = _nativeRuntime.IsHostAvailable;
        _removeLibraryButton.IsEnabled = false;
        Content = BuildContent();
        _libraryList.SelectionChanged += (_, _) => SelectLibraryItem();
        Loaded += async (_, _) => await RefreshLibraryAsync();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "متجر الإضافات — فحص الحزم المحلية",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "PLUGIN MANIFEST • PERMISSION VALIDATION • ZIP SAFETY • SHA-256",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "استورد المفتاح العام للناشر من PEM، ثم افحص الحزمة. Workflow البيانات يعمل بقائمة القراءة المسموحة، وملفات DLL الموقعة تعمل فقط في AppContainer مؤقت بلا شبكة أو صلاحيات هاتف أو ملفات مستخدم.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 11
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var warning = new Border
        {
            Background = Brush("#211D2A"),
            BorderBrush = Brush("#66516B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = new TextBlock
            {
                Text = "كل تشغيل يتطلب توقيع ناشر موثوق. Workflow لا ينفذ إلا أوامر قراءة محددة. Native DLL يعمل داخل AppContainer منفصل بلا قدرات شبكة أو ADB أو وصول لملفات المستخدم، وبحد أقصى 256 MiB و120 ثانية.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#F0C9A5"),
                FontSize = 11
            }
        };
        DockPanel.SetDock(warning, Dock.Top);
        root.Children.Add(warning);

        var library = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        _libraryLocation.Text = "المكتبة المحلية: " + _libraryDirectory;
        _libraryLocation.Foreground = Brush("#9FC7E8");
        _libraryLocation.TextWrapping = TextWrapping.Wrap;
        _libraryLocation.FontSize = 10;
        _libraryLocation.Margin = new Thickness(0, 0, 0, 5);
        library.Children.Add(_libraryLocation);
        _libraryList.ItemsSource = _libraryItems;
        _libraryList.DisplayMemberPath = nameof(PackageLibraryItem.DisplayName);
        _libraryList.Height = 95;
        _libraryList.Background = Brush("#071625");
        _libraryList.Foreground = Brush("#DCEBFA");
        _libraryList.BorderBrush = Brush("#263D58");
        _libraryList.Padding = new Thickness(6);
        library.Children.Add(_libraryList);
        var libraryActions = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
        libraryActions.Children.Add(_refreshLibraryButton);
        libraryActions.Children.Add(_copyToLibraryButton);
        libraryActions.Children.Add(_removeLibraryButton);
        library.Children.Add(libraryActions);
        DockPanel.SetDock(library, Dock.Top);
        root.Children.Add(library);

        var form = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _path.IsReadOnly = true;
        _path.Height = 36;
        _path.Padding = new Thickness(9, 6, 9, 6);
        _path.Background = Brush("#0A1B2D");
        _path.Foreground = Brush("#EAF3FF");
        _path.BorderBrush = Brush("#35516F");
        Grid.SetColumn(_path, 0);
        form.Children.Add(_path);

        var browse = ButtonControl("اختيار ملف", "#243B56", Browse_Click);
        Grid.SetColumn(browse, 1);
        form.Children.Add(browse);
        DockPanel.SetDock(form, Dock.Top);
        root.Children.Add(form);

        var nativeInput = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        nativeInput.Children.Add(new TextBlock
        {
            Text = "مدخل JSON للـNative Plugin (لا يمنحه صلاحية للهاتف أو ملفات النظام):",
            Foreground = Brush("#C4D5E8"),
            Margin = new Thickness(0, 0, 0, 5)
        });
        _nativeRequest.Text = "{}";
        _nativeRequest.AcceptsReturn = true;
        _nativeRequest.TextWrapping = TextWrapping.Wrap;
        _nativeRequest.Height = 54;
        _nativeRequest.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _nativeRequest.Background = Brush("#071625");
        _nativeRequest.Foreground = Brush("#DCEBFA");
        _nativeRequest.BorderBrush = Brush("#38536F");
        _nativeRequest.Padding = new Thickness(8);
        nativeInput.Children.Add(_nativeRequest);
        DockPanel.SetDock(nativeInput, Dock.Top);
        root.Children.Add(nativeInput);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(_inspectButton);
        actions.Children.Add(_trustPublisherButton);
        actions.Children.Add(_signPackageButton);
        actions.Children.Add(_runWorkflowButton);
        actions.Children.Add(_runNativeButton);
        actions.Children.Add(_testSandboxButton);
        actions.Children.Add(_saveButton);
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        _report.IsReadOnly = true;
        _report.AcceptsReturn = true;
        _report.TextWrapping = TextWrapping.NoWrap;
        _report.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _report.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _report.Background = Brush("#071625");
        _report.Foreground = Brush("#DCEBFA");
        _report.BorderBrush = Brush("#263D58");
        _report.Padding = new Thickness(10);
        _report.FontFamily = new FontFamily("Consolas");
        _report.FontSize = 10;
        _report.Text = "لم يتم فحص أي إضافة بعد.";
        root.Children.Add(_report);

        _status.Text = "اختر حزمة محلية للفحص.";
        _status.Foreground = Brush("#9FC7E8");
        _status.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        return root;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختيار Plugin package",
            Filter = "SoftBox Plugin (*.zip;*.sbxplugin)|*.zip;*.sbxplugin|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            _path.Text = dialog.FileName;
            _inspection = null;
            _saveButton.IsEnabled = false;
            _copyToLibraryButton.IsEnabled = false;
            _runWorkflowButton.IsEnabled = false;
            _runNativeButton.IsEnabled = false;
            _report.Text = "اضغط فحص حزمة Plugin لقراءة Manifest والصلاحيات والتوقيع.";
        }
    }

    private async void Inspect_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_path.Text))
        {
            Browse_Click(sender, e);
            if (string.IsNullOrWhiteSpace(_path.Text))
                return;
        }

        _inspectButton.IsEnabled = false;
        _status.Text = "جارٍ فحص ZIP وManifest وحساب SHA-256...";
        try
        {
            _inspection = await _inspector.InspectAsync(_path.Text);
            _report.Text = FormatReport(_inspection);
            _saveButton.IsEnabled = true;
            var accepted = _inspection.PackageErrors.Count == 0 &&
                           _inspection.ManifestValidation.IsValid &&
                           _inspection.Manifest is not null &&
                           _inspection.PublisherSignatureVerified;
            _copyToLibraryButton.IsEnabled = accepted;
            _runWorkflowButton.IsEnabled = accepted &&
                _inspection.Manifest?.ExecutionMode == "read-only-workflow" &&
                _adb is not null && _selectedSerial is not null;
            _runNativeButton.IsEnabled = accepted &&
                _inspection.Manifest?.ExecutionMode == "native-assembly" &&
                _inspection.Manifest.RequestedPermissions is { Count: 0 } &&
                _nativeRuntime.IsHostAvailable;
            _status.Text = accepted
                ? (_runWorkflowButton.IsEnabled
                    ? "التوقيع موثوق. يمكن تثبيت الحزمة وتشغيل Workflow القراءة بعد منح الصلاحيات."
                    : "التوقيع موثوق. يمكن تثبيت الحزمة؛ تشغيل Workflow يحتاج هاتف ADB مصرحًا به.")
                : "لا يمكن التثبيت أو التشغيل: يلزم Manifest صالح وتوقيع ناشر موثوق وصحيح.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر فحص الحزمة: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Plugin Store",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _inspectButton.IsEnabled = true;
        }
    }

    private void SelectLibraryItem()
    {
        if (_libraryList.SelectedItem is not PackageLibraryItem item)
            return;
        _path.Text = item.FullPath;
        _inspection = null;
        _saveButton.IsEnabled = false;
        _copyToLibraryButton.IsEnabled = false;
        _runWorkflowButton.IsEnabled = false;
        _runNativeButton.IsEnabled = false;
        _removeLibraryButton.IsEnabled = true;
        _report.Text = "اختر فحص حزمة Plugin لقراءة Manifest والصلاحيات والتوقيع.";
    }

    private async void RefreshLibrary_Click(object sender, RoutedEventArgs e)
        => await RefreshLibraryAsync();

    private Task RefreshLibraryAsync()
    {
        _libraryItems.Clear();
        try
        {
            Directory.CreateDirectory(_libraryDirectory);
            var files = Directory.EnumerateFiles(_libraryDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(file =>
                    Path.GetExtension(file).Equals(".sbxplugin", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Take(500);
            foreach (var file in files)
                _libraryItems.Add(new PackageLibraryItem(file, Path.GetFileName(file)));
            _libraryLocation.Text = $"المكتبة المحلية: {_libraryDirectory} | الحزم: {_libraryItems.Count:N0}";
            _status.Text = "تم تحديث فهرس المكتبة المحلية. الحزم المخزنة لا يتم تشغيلها.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر قراءة المكتبة المحلية: " + ex.Message;
        }
        return Task.CompletedTask;
    }

    private async void CopyToLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (_inspection is null ||
            _inspection.PackageErrors.Count != 0 ||
            !_inspection.ManifestValidation.IsValid ||
            !_inspection.PublisherSignatureVerified ||
            _inspection.Manifest is null)
        {
            _status.Text = "افحص حزمة صالحة بنيويًا أولًا.";
            return;
        }

        try
        {
            Directory.CreateDirectory(_libraryDirectory);
            var sourceName = Path.GetFileNameWithoutExtension(_inspection.PackagePath);
            var safeName = new string(sourceName
                .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
                .Take(64).ToArray());
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "plugin";
            var destination = Path.Combine(
                _libraryDirectory,
                _inspection.PackageSha256[..16] + "-" + safeName + ".sbxplugin");

            if (!File.Exists(destination))
                File.Copy(_inspection.PackagePath, destination, overwrite: false);

            await RefreshLibraryAsync();
            _libraryList.SelectedItem = _libraryItems.FirstOrDefault(item =>
                item.FullPath.Equals(destination, StringComparison.OrdinalIgnoreCase));

            _status.Text = File.Exists(destination)
                ? "تم حفظ الحزمة كملف خامل في المكتبة المحلية. لم يتم تثبيتها أو منحها صلاحيات أو تشغيل DLL."
                : "لم يتم العثور على ملف الحزمة في المكتبة بعد النسخ.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر حفظ الحزمة في المكتبة: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Plugin Store",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void TrustPublisher_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختيار مفتاح الناشر العام RSA بصيغة PEM",
            Filter = "RSA public key (*.pem;*.pub)|*.pem;*.pub|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var pem = await File.ReadAllTextAsync(dialog.FileName);
            if (pem.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("استورد المفتاح العام فقط. لا تختَر ملفًا يحتوي المفتاح الخاص.");
            var publisherName = Path.GetFileNameWithoutExtension(dialog.FileName);
            var keyId = PluginPublisherTrustStore.GetKeyId(pem);
            var confirmation = MessageBox.Show(this,
                $"بصمة المفتاح العام (SHA-256):\n{keyId}\n\nاسم المفتاح: {publisherName}\n\nلا تثق بالمفتاح لمجرد استلامه مع الحزمة. قارن البصمة مع الناشر عبر قناة مستقلة موثوقة قبل المتابعة. هل تحققت منها وتريد إضافته؟",
                "تأكيد جذر ثقة Plugin", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
            {
                _status.Text = "لم تتم إضافة المفتاح؛ لم يتم تغيير مخزن الثقة.";
                return;
            }
            var trusted = await _trustStore.TrustPublicKeyAsync(publisherName, pem);
            _status.Text = $"تم حفظ مفتاح الناشر العام داخل مخزن DPAPI لهذا المستخدم. Key ID: {trusted.KeyId}";
            if (!string.IsNullOrWhiteSpace(_path.Text) && File.Exists(_path.Text))
                await InspectCurrentPackageAsync();
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر إضافة مفتاح الناشر: " + ex.Message;
        }
    }

    private async Task InspectCurrentPackageAsync()
    {
        _inspection = await _inspector.InspectAsync(_path.Text);
        _report.Text = FormatReport(_inspection);
        var accepted = _inspection.PackageErrors.Count == 0 &&
                       _inspection.ManifestValidation.IsValid &&
                       _inspection.Manifest is not null &&
                       _inspection.PublisherSignatureVerified;
        _copyToLibraryButton.IsEnabled = accepted;
        _runWorkflowButton.IsEnabled = accepted &&
            _inspection.Manifest?.ExecutionMode == "read-only-workflow" &&
            _adb is not null && _selectedSerial is not null;
    }

    private async void SignPackage_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_path.Text) || !File.Exists(_path.Text))
        {
            _status.Text = "اختر حزمة Plugin أولًا.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "اختيار مفتاح RSA الخاص بالناشر لتوقيع الحزمة",
            Filter = "RSA private key (*.pem;*.key)|*.pem;*.key|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var warning = MessageBox.Show(this,
            "سيستخدم SoftBox المفتاح الخاص الموجود في الملف لتوقيع هذه الحزمة فقط. لن يتم نسخ المفتاح إلى المكتبة أو حفظه في إعدادات SoftBox. لا تستخدم المفتاح الخاص لمفتاح ناشر لا تملكه.",
            "تأكيد توقيع Plugin", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (warning != MessageBoxResult.OK)
            return;

        try
        {
            _signPackageButton.IsEnabled = false;
            var privatePem = await File.ReadAllTextAsync(dialog.FileName);
            if (!privatePem.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase) ||
                privatePem.Contains("PUBLIC KEY", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("الملف المحدد لا يحتوي مفتاح RSA خاصًا صالحًا.");

            var keyId = await PluginPackageSigner.SignPackageAsync(_path.Text, privatePem);
            _status.Text = "تم توقيع الحزمة. Key ID: " + keyId +
                ". أضف المفتاح العام المقابل إلى مخزن الثقة ثم أعد الفحص للتحقق من التوقيع.";
            await InspectCurrentPackageAsync();
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر توقيع الحزمة: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Plugin Signer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _signPackageButton.IsEnabled = true;
        }
    }

    private async void RunWorkflow_Click(object sender, RoutedEventArgs e)
    {
        if (_adb is null || _selectedSerial is null || _inspection?.Manifest is null ||
            _inspection.Manifest.ExecutionMode != "read-only-workflow")
        {
            _status.Text = "اختر هاتف ADB مصرحًا به وحزمة Workflow موقعة أولًا.";
            return;
        }

        try
        {
            await InspectCurrentPackageAsync();
            if (_inspection is null || _inspection.PackageErrors.Count > 0 ||
                !_inspection.PublisherSignatureVerified || _inspection.Manifest is null)
                throw new InvalidDataException("فشل التحقق النهائي من توقيع أو محتوى الإضافة.");

            var manifest = _inspection.Manifest;
            var permissions = manifest.RequestedPermissions;
            var message = "ستعمل الإضافة كـWorkflow قراءة فقط على الهاتف المحدد. الصلاحيات المطلوبة:" +
                Environment.NewLine + string.Join(Environment.NewLine, permissions.Select(permission => "• " + permission)) +
                Environment.NewLine + "لن يتم تشغيل DLL أو Shell حر أو تفليش أو حذف بيانات. هل تمنح هذه الصلاحيات لهذه الجلسة؟";
            if (MessageBox.Show(this, message, "منح صلاحيات إضافة موقعة",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                _status.Text = "لم تمنح صلاحيات الإضافة؛ لم يبدأ التشغيل.";
                return;
            }

            _runWorkflowButton.IsEnabled = false;
            _status.Text = "جارٍ إعادة التحقق من تصريح الهاتف ثم تشغيل خطوات القراءة المسموحة...";
            var state = await _adb.RunAsync(
                ["-s", _selectedSerial, "get-state"], CancellationToken.None, TimeSpan.FromSeconds(12));
            if (!string.Equals(state.Trim(), "device", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("الهاتف لم يعد في حالة ADB device مصرح بها.");

            var deviceTools = new AndroidDeviceToolsService(_adb);
            var report = await _workflowRunner.RunAsync(
                _path.Text,
                manifest,
                permissions.ToHashSet(StringComparer.Ordinal),
                (preset, token) => deviceTools.RunReadOnlyPresetAsync(_selectedSerial, preset, token));

            var output = new StringBuilder();
            output.AppendLine("SIGNED PLUGIN READ-ONLY WORKFLOW");
            output.AppendLine("PLUGIN: " + manifest.Name);
            output.AppendLine("PUBLISHER: " + _inspection.TrustedPublisher);
            output.AppendLine("WORKFLOW: " + report.WorkflowName);
            output.AppendLine("STARTED UTC: " + report.StartedAtUtc.ToString("O"));
            output.AppendLine("COMPLETED UTC: " + report.CompletedAtUtc.ToString("O"));
            foreach (var step in report.Steps)
            {
                output.AppendLine();
                output.AppendLine((step.Success ? "[SUCCESS] " : "[FAILED] ") + ReadOnlyWorkflowPolicy.GetStepLabel(step.PresetId));
                if (!string.IsNullOrWhiteSpace(step.Error))
                    output.AppendLine("ERROR: " + step.Error);
                output.AppendLine(step.Output);
            }
            _report.Text = output.ToString();
            _status.Text = report.Success
                ? "انتهى Workflow القراءة. لم يتم تشغيل كود خارجي أو تغيير بيانات الهاتف."
                : "توقفت سلسلة Workflow عند أول فشل. راجع التقرير.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تشغيل Workflow: " + ex.Message;
            _report.Text = ex.ToString();
        }
        finally
        {
            _runWorkflowButton.IsEnabled = _inspection?.PublisherSignatureVerified == true &&
                _inspection.Manifest?.ExecutionMode == "read-only-workflow" &&
                _adb is not null && _selectedSerial is not null;
            _runNativeButton.IsEnabled = _inspection?.PublisherSignatureVerified == true &&
                _inspection.Manifest?.ExecutionMode == "native-assembly" &&
                _inspection.Manifest.RequestedPermissions is { Count: 0 } &&
                File.Exists(Path.Combine(AppContext.BaseDirectory, "PluginHost", "SoftBox.PluginHost.exe"));
        }
    }

    private async void TestSandbox_Click(object sender, RoutedEventArgs e)
    {
        _testSandboxButton.IsEnabled = false;
        _status.Text = "جارٍ اختبار إنشاء AppContainer وفحص Token للعملية المعزولة...";
        try
        {
            var result = await _nativeRuntime.VerifySandboxAsync();
            _report.Text = string.Join(Environment.NewLine,
                "SOFTBOX APPCONTAINER SELF-TEST",
                "SUCCESS: " + result.Success,
                "CONTAINER: " + result.AppContainerName,
                "CHECKED UTC: " + result.CheckedAtUtc.ToString("O"),
                "ERROR: " + (result.Error ?? "None"));
            _status.Text = result.Success
                ? "نجح اختبار AppContainer: الـHost تحقق بنفسه أن Token العملية معزول."
                : "فشل اختبار AppContainer؛ سيتم منع تشغيل DLL حتى تنجح بيئة العزل.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر اختبار AppContainer: " + ex.Message;
            _report.Text = ex.ToString();
        }
        finally
        {
            _testSandboxButton.IsEnabled = _nativeRuntime.IsHostAvailable;
        }
    }

    private async void RunNative_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await InspectCurrentPackageAsync();
            if (_inspection is null || _inspection.PackageErrors.Count != 0 ||
                !_inspection.PublisherSignatureVerified || _inspection.Manifest is null ||
                _inspection.Manifest.ExecutionMode != "native-assembly" ||
                _inspection.Manifest.RequestedPermissions is not { Count: 0 })
                throw new UnauthorizedAccessException("Native execution requires a valid trusted package and an empty permission list.");

            var confirmation = MessageBox.Show(
                this,
                "سيعمل DLL موقّع في عملية Windows AppContainer منفصلة، بلا شبكة أو ADB أو USB أو صلاحيات هاتف أو وصول لملفات المستخدم. حدود التنفيذ: 120 ثانية و256 MiB و1 MiB مخرجات JSON. هل تبدأ؟",
                "تشغيل Native Plugin معزول",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
            {
                _status.Text = "تم إلغاء تشغيل Native Plugin.";
                return;
            }

            _runNativeButton.IsEnabled = false;
            _status.Text = "جارٍ إعادة فحص التوقيع واختبار AppContainer قبل تحميل DLL...";
            var sandboxCheck = await _nativeRuntime.VerifySandboxAsync();
            if (!sandboxCheck.Success)
                throw new UnauthorizedAccessException("فشل تحقق AppContainer: " + sandboxCheck.Error);
            var report = await _nativeRuntime.RunAsync(_path.Text, _nativeRequest.Text);
            var output = new StringBuilder();
            output.AppendLine("SOFTBOX NATIVE APPCONTAINER EXECUTION");
            output.AppendLine("PLUGIN: " + report.PluginName + " (" + report.PluginId + ")");
            output.AppendLine("PUBLISHER: " + report.Publisher);
            output.AppendLine("PACKAGE SHA-256: " + report.PackageSha256);
            output.AppendLine("APP CONTAINER: " + report.AppContainerName);
            output.AppendLine("SANDBOXED: " + report.Sandboxed);
            output.AppendLine("STARTED UTC: " + report.StartedAtUtc.ToString("O"));
            output.AppendLine("COMPLETED UTC: " + report.CompletedAtUtc.ToString("O"));
            output.AppendLine("EXIT CODE: " + (report.ExitCode?.ToString() ?? "N/A"));
            output.AppendLine("TIMED OUT: " + report.TimedOut);
            output.AppendLine();
            output.AppendLine(report.ResponseJson ?? report.Error ?? "No response.");
            _report.Text = output.ToString();
            _status.Text = report.Success
                ? "اكتمل Native Plugin داخل AppContainer وأُغلقت العملية المؤقتة."
                : "فشل أو أُلغي Native Plugin. راجع التقرير؛ لم يحصل على صلاحية للهاتف أو الملفات الشخصية.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تشغيل Native Plugin المعزول: " + ex.Message;
            _report.Text = ex.ToString();
        }
        finally
        {
            _runNativeButton.IsEnabled = _inspection?.PublisherSignatureVerified == true &&
                _inspection.Manifest?.ExecutionMode == "native-assembly" &&
                _inspection.Manifest.RequestedPermissions is { Count: 0 } &&
                File.Exists(Path.Combine(AppContext.BaseDirectory, "PluginHost", "SoftBox.PluginHost.exe"));
        }
    }

    private async void RemoveLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (_libraryList.SelectedItem is not PackageLibraryItem item)
        {
            _status.Text = "حدد حزمة من المكتبة لإزالتها.";
            return;
        }

        var root = Path.GetFullPath(_libraryDirectory) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(item.FullPath);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            _status.Text = "مسار الحزمة المحددة خارج مجلد المكتبة أو لم يعد موجودًا.";
            return;
        }
        if (MessageBox.Show(this, "سيتم حذف ملف الإضافة المحدد من المكتبة المحلية. لن تتغير بيانات الهاتف.",
                "إلغاء تثبيت الإضافة", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            File.Delete(fullPath);
            _inspection = null;
            _copyToLibraryButton.IsEnabled = false;
            _runWorkflowButton.IsEnabled = false;
            _runNativeButton.IsEnabled = false;
            await RefreshLibraryAsync();
            _status.Text = "تم حذف حزمة الإضافة من المكتبة المحلية.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر حذف حزمة الإضافة: " + ex.Message;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_inspection is null)
            return;
        var dialog = new SaveFileDialog
        {
            Title = "حفظ تقرير Plugin",
            Filter = "JSON (*.json)|*.json",
            FileName = "SoftBox-Plugin-Inspection.json"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            File.WriteAllText(dialog.FileName,
                JsonSerializer.Serialize(_inspection, new JsonSerializerOptions { WriteIndented = true }));
            _status.Text = "تم حفظ تقرير الفحص: " + dialog.FileName;
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر حفظ التقرير: " + ex.Message;
        }
    }

    private static string FormatReport(PluginPackageInspection result)
    {
        var text = new StringBuilder();
        text.AppendLine("PLUGIN PACKAGE INSPECTION");
        text.AppendLine("PATH: " + result.PackagePath);
        text.AppendLine("SIZE: " + result.PackageSizeBytes);
        text.AppendLine("SHA-256: " + result.PackageSha256);
        text.AppendLine("MANIFEST VALID: " + result.ManifestValidation.IsValid);
        text.AppendLine("SIGNATURE PRESENT: " + result.SignaturePresent);
        text.AppendLine("PUBLISHER SIGNATURE VERIFIED: " + result.PublisherSignatureVerified);
        text.AppendLine("PUBLISHER KEY ID: " + (result.PublisherKeyId ?? "Not present"));
        text.AppendLine("TRUSTED PUBLISHER: " + (result.TrustedPublisher ?? "Not trusted"));
        text.AppendLine("CODE LOADED OR EXECUTED: " + result.CodeLoadedOrExecuted);
        if (result.Manifest is { } manifest)
        {
            text.AppendLine();
            text.AppendLine("ID: " + manifest.Id);
            text.AppendLine("NAME: " + manifest.Name);
            text.AppendLine("VERSION: " + manifest.Version);
            text.AppendLine("API VERSION: " + manifest.ApiVersion);
            text.AppendLine("MINIMUM SOFTBOX: " + manifest.MinimumSoftBoxVersion);
            text.AppendLine("PUBLISHER: " + manifest.Publisher);
            text.AppendLine("ENTRY POINT: " + manifest.EntryAssembly);
            text.AppendLine("EXECUTION MODE: " + manifest.ExecutionMode);
            text.AppendLine("DESCRIPTION: " + manifest.Description);
            text.AppendLine("REQUESTED PERMISSIONS:");
            foreach (var permission in manifest.RequestedPermissions ?? Array.Empty<string>())
                text.AppendLine("- " + permission);
        }
        text.AppendLine();
        text.AppendLine("PACKAGE ERRORS:");
        foreach (var error in result.PackageErrors)
            text.AppendLine("- " + error);
        text.AppendLine("MANIFEST ERRORS:");
        foreach (var error in result.ManifestValidation.Errors)
            text.AppendLine("- " + error);
        text.AppendLine("WARNINGS:");
        foreach (var warning in result.Warnings)
            text.AppendLine("- " + warning);
        return text.ToString();
    }

    private static Button ButtonControl(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(11, 7, 11, 7),
            Margin = new Thickness(0, 0, 7, 0),
            MinWidth = 110,
            Background = Brush(color),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096"),
            FontWeight = FontWeights.SemiBold
        };
        button.Click += handler;
        return button;
    }

    private sealed record PackageLibraryItem(string FullPath, string DisplayName);

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));
}
