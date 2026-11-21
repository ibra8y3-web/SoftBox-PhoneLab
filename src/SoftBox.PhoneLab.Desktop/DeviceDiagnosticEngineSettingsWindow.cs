using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoftBox.PhoneLab.Core.Compatibility;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Desktop;

public sealed class DeviceDiagnosticEngineSettingsWindow : Window
{
    private readonly DeviceDiagnosticEngineSettingsStore _store;
    private readonly TextBox _adbTimeout = Field();
    private readonly TextBox _agentTimeout = Field();
    private readonly TextBox _maxOutput = Field();
    private readonly TextBox _reviewTemperature = Field();
    private readonly TextBox _criticalTemperature = Field();
    private readonly TextBlock _status = new();
    private readonly Button _save;
    private bool _loading;

    public DeviceDiagnosticEngineSettingsWindow(DeviceDiagnosticEngineSettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Title = "SoftBox | إعدادات محرك التشخيص";
        Width = 680;
        Height = 650;
        MinWidth = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _save = ButtonControl("حفظ الإعدادات", "#176044", Save_Click);
        Content = BuildContent();
        Loaded += async (_, _) => await LoadSettingsAsync();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock
        {
            Text = "إعدادات محرك التشخيص",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "DIAGNOSTIC ENGINE POLICY • LOCAL USER PROFILE",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 12)
        });
        header.Children.Add(Notice(
            "تنبيه مهم: درجات الحرارة هنا حدود مراجعة وتنبيه تشغيلية يحددها مركز الصيانة. ليست حدود أمان معتمدة لكل بطارية أو موديل. حدود الخلايا ومسارات الشحن ودوائر PMIC يجب أن تأتي من ورقة بيانات القطعة أو وثائق اللوحة المطابقة."));
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var fields = new Grid();
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        for (var i = 0; i < 5; i++)
            fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddField(fields, 0, "مهلة أمر ADB (ثانية) — من 5 إلى 90", _adbTimeout);
        AddField(fields, 1, "مهلة طلب أمر Agent (ثانية) — من 10 إلى 180", _agentTimeout);
        AddField(fields, 2, "الحد الأقصى الإجمالي لمخرجات أوامر ADB المحفوظة في التقرير (حرف) — من 16,384 إلى 1,048,576", _maxOutput);
        AddField(fields, 3, "حد تنبيه مراجعة حرارة البطارية (°C)", _reviewTemperature);
        AddField(fields, 4, "حد التنبيه الحراري المرتفع (°C)", _criticalTemperature);
        root.Children.Add(fields);

        var footer = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        var actions = new WrapPanel();
        actions.Children.Add(_save);
        actions.Children.Add(ButtonControl("استعادة الافتراضي", "#28435D", Reset_Click));
        actions.Children.Add(ButtonControl("إغلاق", "#51333A", (_, _) => Close()));
        footer.Children.Add(actions);
        _status.Text = "يتم تحميل الإعدادات المحلية.";
        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 12, 0, 0);
        footer.Children.Add(_status);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        return root;
    }

    private async Task LoadSettingsAsync()
    {
        _loading = true;
        _save.IsEnabled = false;
        try
        {
            var loaded = await _store.LoadAsync();
            SetFields(loaded.Settings);
            _status.Text = loaded.Warnings.Count == 0
                ? "تم تحميل إعدادات محرك التشخيص المحلية."
                : string.Join(Environment.NewLine, loaded.Warnings);
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تحميل الإعدادات: " + ex.Message;
        }
        finally
        {
            _loading = false;
            _save.IsEnabled = true;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (!int.TryParse(_adbTimeout.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var adb) ||
            !int.TryParse(_agentTimeout.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var agent) ||
            !int.TryParse(_maxOutput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxOutput) ||
            !double.TryParse(_reviewTemperature.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var review) ||
            !double.TryParse(_criticalTemperature.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var critical))
        {
            _status.Text = "أدخل أرقامًا صحيحة باستخدام النقطة العشرية عند الحاجة.";
            return;
        }

        var settings = new DiagnosticEngineSettings
        {
            AdbCommandTimeoutSeconds = adb,
            AgentCommandTimeoutSeconds = agent,
            MaximumReportOutputCharacters = maxOutput,
            BatteryReviewTemperatureCelsius = review,
            BatteryCriticalTemperatureCelsius = critical
        };
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            _status.Text = string.Join(Environment.NewLine, errors);
            return;
        }

        _save.IsEnabled = false;
        try
        {
            await _store.SaveAsync(settings);
            _status.Text = "تم حفظ الإعدادات. سيتم تطبيقها على الفحص الشامل التالي.";
        }
        catch (Exception ex)
        {
            _status.Text = "فشل حفظ الإعدادات: " + ex.Message;
        }
        finally
        {
            _save.IsEnabled = true;
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        SetFields(new DiagnosticEngineSettings());
        _status.Text = "تم تحميل القيم الافتراضية في الحقول. اضغط حفظ لتثبيتها.";
    }

    private void SetFields(DiagnosticEngineSettings settings)
    {
        _adbTimeout.Text = settings.AdbCommandTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        _agentTimeout.Text = settings.AgentCommandTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        _maxOutput.Text = settings.MaximumReportOutputCharacters.ToString(CultureInfo.InvariantCulture);
        _reviewTemperature.Text = settings.BatteryReviewTemperatureCelsius.ToString("0.0", CultureInfo.InvariantCulture);
        _criticalTemperature.Text = settings.BatteryCriticalTemperatureCelsius.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static void AddField(Grid grid, int row, string label, TextBox field)
    {
        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = Brush("#C4D5E8"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 10, 8),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(labelBlock, row);
        Grid.SetColumn(labelBlock, 0);
        grid.Children.Add(labelBlock);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        field.Margin = new Thickness(4, 7, 0, 7);
        grid.Children.Add(field);
    }

    private static TextBlock Notice(string text)
        => new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#F0C9A5"),
            Background = Brush("#1D2938"),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 14)
        };

    private static TextBox Field()
        => new()
        {
            Padding = new Thickness(8),
            Background = Brush("#071625"),
            Foreground = Brush("#DCEBFA"),
            BorderBrush = Brush("#38536F"),
            VerticalContentAlignment = VerticalAlignment.Center
        };

    private static Button ButtonControl(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 110,
            Background = Brush(color),
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
