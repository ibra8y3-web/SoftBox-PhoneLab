using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoftBox.PhoneLab.Core.Activity;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>Displays locally retained, DPAPI-protected component test/read evidence.</summary>
public sealed class DiagnosticEvidenceHistoryWindow : Window
{
    private readonly DiagnosticEvidenceStore _store;
    private readonly string? _serial;
    private readonly StackPanel _entries = new();
    private readonly TextBlock _status = new();

    public DiagnosticEvidenceHistoryWindow(DiagnosticEvidenceStore store, string? serial = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _serial = serial;
        Title = "SoftBox | Diagnostic Evidence History";
        Width = 950;
        Height = 760;
        MinWidth = 720;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");
        Content = BuildContent();
        Loaded += async (_, _) => await ReloadAsync();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = "سجل أدلة اختبارات المكونات",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(Text(
            string.IsNullOrWhiteSpace(_serial)
                ? "يعرض أحدث الأدلة المحلية المحفوظة لجميع الأجهزة."
                : "يعرض الأدلة المحفوظة للجهاز المحدد فقط. يظهر الرقم التسلسلي مقنّعًا.",
            "#B7C9DE", 10));
        var actions = new WrapPanel();
        actions.Children.Add(Button("تحديث السجل", async (_, _) => await ReloadAsync()));
        header.Children.Add(actions);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = Brush("#9FC7E8");
        _status.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(_status, Dock.Top);
        root.Children.Add(_status);

        root.Children.Add(new ScrollViewer
        {
            Content = _entries,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        return root;
    }

    private async Task ReloadAsync()
    {
        _status.Text = "جارٍ قراءة الأدلة المحلية المحمية بمستخدم Windows الحالي...";
        _entries.Children.Clear();
        try
        {
            var records = await _store.GetRecentAsync(_serial, 1000);
            if (records.Count == 0)
                _entries.Children.Add(Text(
                    "لا توجد أدلة محفوظة بعد. وصّل جهازًا مصرحًا به، وشغّل اختبارًا من مركز الاختبارات، ثم ستظهر النتائج هنا.",
                    "#AFC7DF", 11));

            foreach (var item in records)
            {
                var card = new Border
                {
                    Background = Brush("#10243A"),
                    BorderBrush = Brush("#2D4A68"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(11),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                var stack = new StackPanel();
                var statusColor = item.Status switch
                {
                    "Pass" => "#22C55E",
                    "Failed" => "#EF4444",
                    "Review" => "#F59E0B",
                    "DataRead" => "#38BDF8",
                    _ => "#94A3B8"
                };
                stack.Children.Add(Text($"{item.ComponentName} • {item.Status}", statusColor, 12));
                stack.Children.Add(Text(
                    $"{item.Manufacturer} {item.Model} • {item.MaskedSerial} • {item.AtUtc.ToLocalTime():g}",
                    "#8FC7EA", 9));
                stack.Children.Add(Text("المصدر: " + item.Source, "#AFC7DF", 9));
                stack.Children.Add(Text("الدليل: " + item.Evidence, "#DCEBFA", 10));
                card.Child = stack;
                _entries.Children.Add(card);
            }

            _status.Text = $"تم تحميل {records.Count} سجلًا من قاعدة محلية مشفرة. لا تتضمن القاعدة الرقم التسلسلي الكامل.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر قراءة سجل الأدلة: " + ex.Message;
        }
    }

    private static Button Button(string label, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 4, 8, 4),
            Background = Brush("#14517A"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096")
        };
        button.Click += handler;
        return button;
    }

    private static TextBlock Text(string value, string color, double size)
        => new() { Text = value, Foreground = Brush(color), FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) };

    private static SolidColorBrush Brush(string value)
        => new((Color)ColorConverter.ConvertFromString(value)!);
}
