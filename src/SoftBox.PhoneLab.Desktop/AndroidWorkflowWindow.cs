using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Workflow;
using SoftBox.PhoneLab.Desktop.Services;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Builds, saves, loads and runs sequential Android workflows from the read-only preset catalog.
/// No arbitrary shell commands or destructive steps can be inserted.
/// </summary>
public sealed class AndroidWorkflowWindow : Window
{
    private readonly AndroidWorkflowService _workflows;
    private readonly string _serial;
    private readonly TextBox _name = new();
    private readonly ComboBox _stepPicker = new();
    private readonly ListBox _steps = new();
    private readonly TextBox _output = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new();
    private readonly ObservableCollection<WorkflowStepItem> _stepItems = [];
    private readonly Button _addButton;
    private readonly Button _removeButton;
    private readonly Button _saveButton;
    private readonly Button _loadButton;
    private readonly Button _runButton;
    private readonly Button _cancelButton;
    private CancellationTokenSource? _operation;

    public AndroidWorkflowWindow(AdbClient adb, string serial)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));
        _serial = serial;
        _workflows = new AndroidWorkflowService(new AndroidDeviceToolsService(adb));

        Title = $"SoftBox | أتمتة سير العمل — {_serial}";
        Width = 980;
        Height = 760;
        MinWidth = 780;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _addButton = ButtonControl("إضافة خطوة", "#14517A", AddStep_Click);
        _removeButton = ButtonControl("حذف الخطوة المحددة", "#51333A", RemoveStep_Click);
        _saveButton = ButtonControl("حفظ Workflow", "#243B56", Save_Click);
        _loadButton = ButtonControl("تحميل Workflow", "#243B56", Load_Click);
        _runButton = ButtonControl("تشغيل على الهاتف", "#176044", Run_Click);
        _cancelButton = ButtonControl("إلغاء التشغيل", "#51333A", Cancel_Click);
        _cancelButton.IsEnabled = false;

        Content = BuildContent();
        Closed += (_, _) => _operation?.Cancel();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "أتمتة سير العمل",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "READ-ONLY ANDROID WORKFLOWS • JSON TEMPLATES • STEP-BY-STEP EXECUTION",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "الجهاز المستهدف: " + _serial + ". يتكون Workflow من خطوات قراءة آمنة محددة مسبقًا، وتحفظ كملف JSON محلي. تتوقف السلسلة عند فشل خطوة، ويمكنك إلغاؤها.",
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
            Margin = new Thickness(0, 0, 0, 12),
            Child = new TextBlock
            {
                Text = "مهم: لا يسمح المحرك بأوامر Shell حرة أو حذف أو تفليش أو إزالة حماية أو تعديل ملفات النظام. القائمة الحالية للفحص والقراءة فقط؛ أتمتة العمليات الحساسة لا تُفعّل تلقائيًا.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#F0C9A5"),
                FontSize = 11
            }
        };
        DockPanel.SetDock(warning, Dock.Top);
        root.Children.Add(warning);

        var top = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _name.Text = "فحص الجهاز قبل الإصلاح";
        _name.Height = 36;
        _name.Padding = new Thickness(9, 6, 9, 6);
        _name.Background = Brush("#0A1B2D");
        _name.Foreground = Brush("#EAF3FF");
        _name.BorderBrush = Brush("#35516F");
        Grid.SetColumn(_name, 0);
        top.Children.Add(_name);

        _stepPicker.ItemsSource = ReadOnlyWorkflowPolicy.AvailableSteps
            .Select(pair => new WorkflowStepChoice(pair.Key, pair.Value)).ToArray();
        _stepPicker.DisplayMemberPath = nameof(WorkflowStepChoice.Label);
        _stepPicker.SelectedIndex = 0;
        _stepPicker.Width = 240;
        _stepPicker.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_stepPicker, 1);
        top.Children.Add(_stepPicker);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(_addButton);
        actions.Children.Add(_removeButton);
        actions.Children.Add(_saveButton);
        actions.Children.Add(_loadButton);
        actions.Children.Add(_runButton);
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(190) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _steps.ItemsSource = _stepItems;
        _steps.Background = Brush("#071625");
        _steps.Foreground = Brush("#DCEBFA");
        _steps.BorderBrush = Brush("#263D58");
        _steps.Padding = new Thickness(8);
        Grid.SetRow(_steps, 0);
        body.Children.Add(_steps);

        _output.IsReadOnly = true;
        _output.AcceptsReturn = true;
        _output.TextWrapping = TextWrapping.NoWrap;
        _output.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _output.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _output.Background = Brush("#071625");
        _output.Foreground = Brush("#DCEBFA");
        _output.BorderBrush = Brush("#263D58");
        _output.Padding = new Thickness(10);
        _output.FontFamily = new FontFamily("Consolas");
        _output.FontSize = 10;
        _output.Text = "ستظهر نتيجة كل خطوة ومخرجاتها هنا بعد التشغيل.";
        Grid.SetRow(_output, 1);
        body.Children.Add(_output);

        DockPanel.SetDock(body, Dock.Top);
        root.Children.Add(body);

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _status.Text = "أضف خطوة واحدة على الأقل ثم احفظ Workflow أو شغّله.";
        _status.Foreground = Brush("#9FC7E8");
        _status.FontSize = 11;
        footer.Children.Add(_status);
        _progress.Height = 4;
        _progress.IsIndeterminate = true;
        _progress.Visibility = Visibility.Collapsed;
        _progress.Foreground = Brush("#38BDF8");
        footer.Children.Add(_progress);
        footer.Children.Add(_cancelButton);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        return root;
    }

    private void AddStep_Click(object sender, RoutedEventArgs e)
    {
        if (_stepPicker.SelectedItem is not WorkflowStepChoice choice)
            return;
        if (_stepItems.Count >= ReadOnlyWorkflowPolicy.MaximumSteps)
        {
            MessageBox.Show(this, $"الحد الأقصى هو {ReadOnlyWorkflowPolicy.MaximumSteps} خطوة.",
                "Workflow", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _stepItems.Add(new WorkflowStepItem(choice.Id, choice.Label));
    }

    private void RemoveStep_Click(object sender, RoutedEventArgs e)
    {
        if (_steps.SelectedItem is WorkflowStepItem selected)
            _stepItems.Remove(selected);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var definition = BuildDefinition();
            var dialog = new SaveFileDialog
            {
                Title = "حفظ قالب Workflow",
                Filter = "SoftBox Workflow (*.json)|*.json",
                FileName = "SoftBox-Workflow.json",
                AddExtension = true,
                DefaultExt = ".json"
            };
            if (dialog.ShowDialog(this) != true)
                return;

            await _workflows.SaveAsync(dialog.FileName, definition);
            _status.Text = "تم حفظ قالب Workflow محليًا: " + dialog.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "تعذر حفظ Workflow",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Load_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "تحميل قالب SoftBox Workflow",
            Filter = "SoftBox Workflow (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var workflow = await _workflows.LoadAsync(dialog.FileName);
            _name.Text = workflow.Name;
            _stepItems.Clear();
            foreach (var step in workflow.Steps)
                _stepItems.Add(new WorkflowStepItem(step.PresetId, ReadOnlyWorkflowPolicy.GetStepLabel(step.PresetId)));
            _status.Text = $"تم تحميل Workflow: {workflow.Name} — {workflow.Steps.Count} خطوة.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "ملف Workflow غير صالح",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var definition = BuildDefinition();
            var confirm = MessageBox.Show(this,
                $"سيتم تشغيل {definition.Steps.Count} خطوة قراءة فقط على الجهاز {_serial} بالتتابع. هل تتابع؟",
                "تأكيد تشغيل Workflow", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;

            _operation = new CancellationTokenSource();
            SetBusy(true, "بدء Workflow على الجهاز المحدد...");
            var progress = new Progress<string>(message => _status.Text = message);
            try
            {
                var report = await _workflows.RunAsync(_serial, definition, progress, _operation.Token);
                _output.Text = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                _status.Text = report.Success
                    ? $"اكتمل Workflow بنجاح: {report.Steps.Count} خطوة."
                    : $"توقف Workflow بعد فشل خطوة. راجع التقرير؛ المنفذ: {report.Steps.Count} خطوة.";
            }
            catch (OperationCanceledException)
            {
                _status.Text = "تم إلغاء Workflow بطلب المستخدم.";
            }
            finally
            {
                _operation.Dispose();
                _operation = null;
                SetBusy(false, _status.Text);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "تعذر تشغيل Workflow",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private ReadOnlyWorkflowDefinition BuildDefinition()
        => ReadOnlyWorkflowPolicy.Validate(new ReadOnlyWorkflowDefinition(
            ReadOnlyWorkflowPolicy.CurrentSchemaVersion,
            _name.Text,
            _stepItems.Select(step => new ReadOnlyWorkflowStep(step.Id)).ToArray()));

    private void SetBusy(bool busy, string message)
    {
        _status.Text = message;
        _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _addButton.IsEnabled = !busy;
        _removeButton.IsEnabled = !busy;
        _saveButton.IsEnabled = !busy;
        _loadButton.IsEnabled = !busy;
        _runButton.IsEnabled = !busy;
        _cancelButton.IsEnabled = busy;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(11, 7, 11, 7),
            Margin = new Thickness(0, 0, 7, 0),
            MinWidth = 90,
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

    private sealed record WorkflowStepChoice(string Id, string Label);
    private sealed record WorkflowStepItem(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}
