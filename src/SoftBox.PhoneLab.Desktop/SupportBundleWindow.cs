using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Support;
using SoftBox.PhoneLab.Desktop.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Lets the user review exactly which local log files are included in an offline redacted support archive.
/// </summary>
public sealed class SupportBundleWindow : Window
{
    private readonly SupportBundleService _service = new();
    private readonly ObservableCollection<string> _selectedLogs = [];
    private readonly ListBox _files = new();
    private readonly TextBox _notes = new();
    private readonly TextBox _result = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _addButton;
    private readonly Button _removeButton;
    private readonly Button _createButton;
    private CancellationTokenSource? _operation;

    public SupportBundleWindow()
    {
        Title = "SoftBox | الدعم والسجلات — Support Bundle";
        Width = 950;
        Height = 720;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _addButton = ButtonControl("إضافة ملفات سجلات", "#14517A", AddFiles_Click);
        _removeButton = ButtonControl("إزالة المحدد", "#51333A", RemoveFile_Click);
        _createButton = ButtonControl("إنشاء حزمة الدعم محليًا", "#176044", CreateBundle_Click);
        Content = BuildContent();
        if (File.Exists(SoftBoxDesktopTelemetry.LogPath))
            _selectedLogs.Add(SoftBoxDesktopTelemetry.LogPath);
        Closed += (_, _) => _operation?.Cancel();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "حزمة الدعم والسجلات",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "LOCAL SUPPORT ZIP • SYSTEM RUNTIME DETAILS • SECRET REDACTION",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "اختر ملفات TXT أو LOG أو JSON أو JSONL التي تريد تضمينها. يُضاف سجل أعطال Windows الحالي تلقائيًا إن وُجد، ويمكنك إزالته قبل الإنشاء. ينقّح SoftBox الحقول الشائعة مثل Password وToken وAuthorization وSerial قبل الإضافة. لا يتم رفع الحزمة أو إرسالها تلقائيًا.",
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
                Text = "مهم: التنقيح آلي وليس ضمانًا كاملًا لإزالة كل البيانات الشخصية. راجع الملفات الأصلية والحزمة الناتجة بنفسك قبل مشاركتها. لا تضف ملفات العملاء أو الصور أو قواعد بيانات الورشة إلا عند الضرورة وبموافقة مالك البيانات.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#F0C9A5"),
                FontSize = 11
            }
        };
        DockPanel.SetDock(warning, Dock.Top);
        root.Children.Add(warning);

        var filesPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        actions.Children.Add(_addButton);
        actions.Children.Add(_removeButton);
        DockPanel.SetDock(actions, Dock.Top);
        filesPanel.Children.Add(actions);

        _files.ItemsSource = _selectedLogs;
        _files.SelectionMode = SelectionMode.Extended;
        _files.Background = Brush("#071625");
        _files.Foreground = Brush("#DCEBFA");
        _files.BorderBrush = Brush("#263D58");
        _files.Padding = new Thickness(8);
        _files.Height = 150;
        filesPanel.Children.Add(_files);
        DockPanel.SetDock(filesPanel, Dock.Top);
        root.Children.Add(filesPanel);

        var notesPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        notesPanel.Children.Add(new TextBlock
        {
            Text = "ملاحظات إضافية اختيارية",
            Foreground = Brush("#9FC7E8"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 5)
        });
        _notes.Height = 60;
        _notes.AcceptsReturn = true;
        _notes.TextWrapping = TextWrapping.Wrap;
        _notes.Padding = new Thickness(8);
        _notes.Background = Brush("#0A1B2D");
        _notes.Foreground = Brush("#EAF3FF");
        _notes.BorderBrush = Brush("#35516F");
        notesPanel.Children.Add(_notes);
        DockPanel.SetDock(notesPanel, Dock.Top);
        root.Children.Add(notesPanel);

        _result.IsReadOnly = true;
        _result.AcceptsReturn = true;
        _result.TextWrapping = TextWrapping.Wrap;
        _result.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _result.Background = Brush("#071625");
        _result.Foreground = Brush("#DCEBFA");
        _result.BorderBrush = Brush("#263D58");
        _result.Padding = new Thickness(10);
        _result.FontFamily = new FontFamily("Consolas");
        _result.FontSize = 10;
        _result.Text = "بعد إنشاء الحزمة ستظهر هنا قائمة الملفات المنقحة وبصماتها.";
        root.Children.Add(_result);

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        footer.Children.Add(_createButton);
        _status.Text = "يمكنك إنشاء حزمة تتضمن معلومات النظام فقط دون إضافة سجلات.";
        _status.Foreground = Brush("#9FC7E8");
        _status.FontSize = 11;
        footer.Children.Add(_status);
        _progress.Height = 4;
        _progress.IsIndeterminate = true;
        _progress.Visibility = Visibility.Collapsed;
        _progress.Foreground = Brush("#38BDF8");
        footer.Children.Add(_progress);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        return root;
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختيار سجلات محلية لإضافتها إلى حزمة الدعم",
            Filter = "Text logs and JSON|*.txt;*.log;*.json;*.jsonl|All files|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        foreach (var path in dialog.FileNames)
        {
            if (!_selectedLogs.Contains(path, StringComparer.OrdinalIgnoreCase))
                _selectedLogs.Add(path);
        }

        _status.Text = $"تم اختيار {_selectedLogs.Count:N0} ملف. ستتم قراءة محتواه وتنقيحه قبل تضمينه.";
    }

    private void RemoveFile_Click(object sender, RoutedEventArgs e)
    {
        var selected = _files.SelectedItems.Cast<string>().ToArray();
        foreach (var path in selected)
            _selectedLogs.Remove(path);
        _status.Text = $"الملفات المختارة: {_selectedLogs.Count:N0}.";
    }

    private async void CreateBundle_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null)
            return;

        var confirm = MessageBox.Show(this,
            $"سيتم إنشاء ZIP محلي بمعلومات النظام و{_selectedLogs.Count} ملفًا اخترتها، بعد تطبيق تنقيح آلي للبيانات الحساسة. لن يتم رفع الحزمة إلى الإنترنت. هل تتابع؟",
            "مراجعة حزمة الدعم", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "حفظ حزمة دعم SoftBox",
            Filter = "SoftBox Support Bundle (*.zip)|*.zip",
            FileName = $"SoftBox-Support-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            AddExtension = true,
            DefaultExt = ".zip"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        _operation = new CancellationTokenSource();
        SetBusy(true, "جارٍ جمع معلومات النظام وقراءة الملفات المختارة وتنقيحها...");
        try
        {
            var result = await _service.CreateAsync(
                dialog.FileName,
                _selectedLogs.ToArray(),
                _notes.Text,
                _operation.Token);

            var output = new StringBuilder();
            output.AppendLine("SUPPORT BUNDLE: CREATED LOCALLY");
            output.AppendLine("ARCHIVE: " + result.ArchivePath);
            output.AppendLine("FILES INCLUDED: " + result.IncludedFileCount);
            output.AppendLine("REDACTED BYTES: " + result.IncludedBytes);
            output.AppendLine("UPLOAD: NOT PERFORMED");
            output.AppendLine();
            foreach (var file in result.Files)
            {
                output.AppendLine(file.Name);
                output.AppendLine("Original bytes: " + file.OriginalSizeBytes);
                output.AppendLine("Redacted bytes: " + file.RedactedSizeBytes);
                output.AppendLine("SHA-256: " + file.Sha256);
                output.AppendLine();
            }
            foreach (var warning in result.Warnings)
                output.AppendLine("WARNING: " + warning);
            _result.Text = output.ToString();
            _status.Text = "اكتملت حزمة الدعم محليًا. راجع محتواها قبل مشاركتها.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "تم إلغاء إنشاء الحزمة.";
        }
        catch (Exception ex)
        {
            _status.Text = "فشل إنشاء الحزمة: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Support Bundle",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            SetBusy(false, _status.Text);
        }
    }

    private void SetBusy(bool busy, string message)
    {
        _status.Text = message;
        _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _createButton.IsEnabled = !busy;
        _addButton.IsEnabled = !busy;
        _removeButton.IsEnabled = !busy;
    }

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(11, 7, 11, 7),
            Margin = new Thickness(0, 0, 7, 0),
            MinWidth = 104,
            Background = Brush(background),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096"),
            FontWeight = FontWeights.SemiBold
        };
        button.Click += handler;
        return button;
    }

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));
}
