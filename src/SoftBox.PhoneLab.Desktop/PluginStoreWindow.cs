using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Plugins;
using System.IO;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Local plugin package inspector. It does not install or execute plugin assemblies.
/// </summary>
public sealed class PluginStoreWindow : Window
{
    private readonly PluginPackageInspector _inspector = new();
    private readonly TextBox _path = new();
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
    private PluginPackageInspection? _inspection;

    public PluginStoreWindow()
    {
        Title = "SoftBox | متجر الإضافات — فحص الحزم";
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
        _copyToLibraryButton = ButtonControl("حفظ في المكتبة المحلية", "#243B56", CopyToLibrary_Click);
        _refreshLibraryButton = ButtonControl("تحديث المكتبة", "#243B56", RefreshLibrary_Click);
        _saveButton.IsEnabled = false;
        _copyToLibraryButton.IsEnabled = false;
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
            Text = "اختر ملف Plugin محليًا لقراءة Manifest وفحص الصلاحيات وبنية ZIP وحساب بصمة الحزمة. هذه النسخة لا تثبت ولا تشغّل أي DLL ولا تتحقق بعد من توقيع الناشر.",
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
                Text = "حالة الأمان: التوقيع الرقمي غير متحقق، لذلك لا توجد أزرار تثبيت أو تشغيل. لا تعتبر SHA-256 إثباتًا لهوية المطوّر. الصلاحيات غير المعروفة أو الخطرة تُرفض.",
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

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(_inspectButton);
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
            _report.Text = "اضغط فحص حزمة Plugin لقراءة Manifest والصلاحيات.";
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
            _copyToLibraryButton.IsEnabled =
                _inspection.PackageErrors.Count == 0 &&
                _inspection.ManifestValidation.IsValid &&
                _inspection.Manifest is not null;
            _status.Text = _copyToLibraryButton.IsEnabled
                ? "اجتازت الحزمة الفحص البنيوي. يمكنك حفظ نسخة خاملة في المكتبة المحلية؛ لن يتم تثبيتها أو تشغيلها."
                : "فشل الفحص البنيوي أو تحقق Manifest؛ لا يمكن حفظ الحزمة في المكتبة.";
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
        _report.Text = "اختر فحص حزمة Plugin لقراءة Manifest والصلاحيات والبصمة.";
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
        text.AppendLine("PUBLISHER SIGNATURE VERIFIED: " + result.PublisherSignatureVerified);
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
            text.AppendLine("ENTRY ASSEMBLY: " + manifest.EntryAssembly);
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
