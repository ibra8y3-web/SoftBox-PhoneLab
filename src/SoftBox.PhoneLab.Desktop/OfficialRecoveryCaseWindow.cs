using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoftBox.PhoneLab.Core.Recovery;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Builds a local evidence checklist and opens official recovery channels.
/// It does not perform or automate a lock bypass or a device state change.
/// </summary>
public sealed class OfficialRecoveryCaseWindow : Window
{
    private readonly TextBox _manufacturer;
    private readonly TextBox _model;
    private readonly ComboBox _workflowSelector;
    private readonly CheckBox _ownerAuthorization;
    private readonly CheckBox _proofOfOwnership;
    private readonly CheckBox _organizationAdmin;
    private readonly CheckBox _carrierAccountHolder;
    private readonly TextBlock _summary;
    private readonly StackPanel _resources;

    public OfficialRecoveryCaseWindow(string? manufacturer, string? model)
    {
        Title = "SoftBox | Official Recovery Case Builder";
        Width = 760;
        Height = 780;
        MinWidth = 620;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _manufacturer = CreateInput(manufacturer, "الشركة المصنّعة");
        _model = CreateInput(model, "الموديل");
        _workflowSelector = new ComboBox
        {
            ItemsSource = OfficialRecoveryWorkflowCatalog.All,
            DisplayMemberPath = nameof(OfficialRecoveryWorkflow.TitleAr),
            SelectedIndex = 0,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 5, 0, 10),
            Background = Brush("#0F1E32"),
            Foreground = Brush("#EAF3FF")
        };
        _ownerAuthorization = CreateCheckBox("أؤكد أنني مالك الجهاز أو لدي تفويض صريح من المالك.");
        _proofOfOwnership = CreateCheckBox("إثبات الشراء/الملكية متاح لتقديمه مباشرة إلى الجهة الرسمية (اختياري لمسار الحساب).");
        _organizationAdmin = CreateCheckBox("أنا مسؤول المؤسسة المخوّل أو أعمل بتفويض موثق منه.");
        _carrierAccountHolder = CreateCheckBox("أنا صاحب حساب شركة الاتصالات أو مفوض عنه.");

        _summary = MakeText(string.Empty, "#C5D6E8");
        _resources = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "إعداد طلب استرداد أو فك قيد عبر الجهة المالكة للحماية",
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(MakeText(
            "اختر الحالة، راجع الخطوات الرسمية، ثم صدّر ملف Markdown محليًا. هذا المعالج لا يزيل القفل ولا يرسل طلبًا تلقائيًا.",
            "#B7C9DE"));
        root.Children.Add(Field("نوع الحالة", _workflowSelector));
        root.Children.Add(Field("الشركة المصنّعة", _manufacturer));
        root.Children.Add(Field("الموديل", _model));
        root.Children.Add(Card("تأكيد الصلاحية", _ownerAuthorization));
        root.Children.Add(Card("مستندات الملكية", _proofOfOwnership));
        root.Children.Add(Card("تفويض المؤسسة — مطلوب لحالة MDM", _organizationAdmin));
        root.Children.Add(Card("تفويض صاحب الحساب — مطلوب لفتح الشبكة", _carrierAccountHolder));
        root.Children.Add(Card("المسار والخطوات", _summary));
        root.Children.Add(new TextBlock
        {
            Text = "القنوات الرسمية",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#7DD3FC"),
            Margin = new Thickness(2, 8, 2, 5)
        });
        root.Children.Add(_resources);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var exportButton = new Button
        {
            Content = "إنشاء وحفظ ملف الطلب",
            Padding = new Thickness(14, 8, 14, 8),
            Background = Brush("#14517A"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 0)
        };
        exportButton.Click += SaveCase_Click;
        actions.Children.Add(exportButton);
        var closeButton = new Button
        {
            Content = "إغلاق",
            Padding = new Thickness(14, 8, 14, 8),
            Background = Brush("#26364A"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#52677E")
        };
        closeButton.Click += (_, _) => Close();
        actions.Children.Add(closeButton);
        root.Children.Add(actions);

        Content = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        _workflowSelector.SelectionChanged += (_, _) => RefreshWorkflow();
        RefreshWorkflow();
    }

    private void RefreshWorkflow()
    {
        if (_workflowSelector.SelectedItem is not OfficialRecoveryWorkflow workflow)
            return;

        _summary.Text = workflow.SummaryAr + Environment.NewLine + Environment.NewLine +
                        string.Join(Environment.NewLine, workflow.Steps.Select((step, index) => $"{index + 1}. {step}"));
        _organizationAdmin.Visibility = workflow.Kind == OfficialRecoveryCaseKind.MdmRelease
            ? Visibility.Visible : Visibility.Collapsed;
        _carrierAccountHolder.Visibility = workflow.Kind == OfficialRecoveryCaseKind.CarrierNetworkUnlock
            ? Visibility.Visible : Visibility.Collapsed;

        _resources.Children.Clear();
        if (workflow.Resources.Count == 0)
        {
            _resources.Children.Add(MakeText(
                "لا يوجد رابط موحد لكل شركات الاتصالات. استخدم تطبيق أو موقع أو فرع المشغّل الأصلي من قناته الرسمية.",
                "#B7C9DE"));
            return;
        }

        foreach (var resource in workflow.Resources)
        {
            var button = new Button
            {
                Content = "فتح: " + resource.Title,
                Tag = resource.Url,
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 0, 5),
                Background = Brush("#18334F"),
                Foreground = Brushes.White,
                BorderBrush = Brush("#3E7096")
            };
            button.Click += OpenResource_Click;
            _resources.Children.Add(button);
        }
    }

    private void SaveCase_Click(object sender, RoutedEventArgs e)
    {
        if (_workflowSelector.SelectedItem is not OfficialRecoveryWorkflow workflow)
            return;

        var result = OfficialRecoveryCaseService.Build(
            workflow.Kind,
            new OfficialRecoveryCaseInput(
                _manufacturer.Text,
                _model.Text,
                _ownerAuthorization.IsChecked == true,
                _proofOfOwnership.IsChecked == true,
                _organizationAdmin.IsChecked == true,
                _carrierAccountHolder.IsChecked == true));

        if (!result.ReadyForOfficialContact)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, result.ValidationErrors),
                "بيانات أو تفويض مطلوب", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "حفظ ملف طلب الاسترداد الرسمي",
            Filter = "Markdown file (*.md)|*.md|Text file (*.txt)|*.txt",
            DefaultExt = ".md",
            AddExtension = true,
            FileName = "SoftBox-Recovery-" + workflow.Id + ".md"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            File.WriteAllText(dialog.FileName, result.Markdown, new UTF8Encoding(false));
            MessageBox.Show(this,
                "تم إنشاء ملف الطلب محليًا." + Environment.NewLine +
                "المرجع: " + result.CaseReference + Environment.NewLine +
                "الملف لا يثبت الملكية ولا يفتح القفل؛ قدّم الطلب للجهة الرسمية بنفسك.",
                "SoftBox", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "تعذر حفظ الملف: " + ex.Message,
                "SoftBox", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void OpenResource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url } ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("تعذر فتح المصدر الرسمي: " + ex.Message,
                "SoftBox", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static TextBox CreateInput(string? value, string placeholder)
        => new()
        {
            Text = value ?? string.Empty,
            ToolTip = placeholder,
            MinWidth = 240,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 5, 0, 10),
            Background = Brush("#0F1E32"),
            Foreground = Brush("#EAF3FF"),
            BorderBrush = Brush("#36516D")
        };

    private static CheckBox CreateCheckBox(string text)
        => new()
        {
            Content = text,
            Foreground = Brush("#EAF3FF"),
            Margin = new Thickness(0, 3, 0, 3)
        };

    private static Border Field(string title, UIElement control)
    {
        var panel = new StackPanel();
        panel.Children.Add(MakeText(title, "#7DD3FC"));
        panel.Children.Add(control);
        return Card(null, panel);
    }

    private static Border Card(string? title, UIElement content)
    {
        var panel = new StackPanel();
        if (!string.IsNullOrWhiteSpace(title))
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#7DD3FC"),
                Margin = new Thickness(0, 0, 0, 6)
            });
        panel.Children.Add(content);
        return new Border
        {
            Background = Brush("#0F1E32"),
            BorderBrush = Brush("#263D58"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(11),
            Margin = new Thickness(0, 0, 0, 8),
            Child = panel
        };
    }

    private static TextBlock MakeText(string value, string color)
        => new()
        {
            Text = value,
            Foreground = Brush(color),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4)
        };

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color)!);
}
