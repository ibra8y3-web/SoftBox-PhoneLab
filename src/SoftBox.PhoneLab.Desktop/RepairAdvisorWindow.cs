using SoftBox.PhoneLab.Core.Diagnostics;
using SoftBox.PhoneLab.Core.Models;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Displays deterministic, local repair advice linked to actual device/Agent/component evidence.
/// </summary>
public sealed class RepairAdvisorWindow : Window
{
    private readonly RepairAdviceReport _report;
    private readonly ObservableCollection<AdviceListItem> _items = [];
    private readonly ListBox _list = new();
    private readonly TextBox _details = new();

    public RepairAdvisorWindow(
        DeviceSnapshot? device,
        AgentSnapshot? agent,
        IReadOnlyList<ComponentDiagnostic>? components,
        bool adbAuthorized)
    {
        _report = DeviceRepairAdvisor.Analyze(device, agent, components, adbAuthorized);
        Title = "SoftBox | مساعد الإصلاح المبني على الأدلة";
        Width = 1040;
        Height = 760;
        MinWidth = 800;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        Content = BuildContent();
        _list.SelectionChanged += (_, _) => ShowSelected();
        Loaded += (_, _) =>
        {
            foreach (var advice in _report.Advice)
                _items.Add(new AdviceListItem(advice));
            _list.SelectedIndex = 0;
        };
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "مساعد الإصلاح المبني على الأدلة",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "LOCAL RULE-BASED REASONING • EVIDENCE-LINKED • NON-DESTRUCTIVE GUIDANCE",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "الجهاز: " + _report.DeviceName + " | عدد الأدلة المقروءة: " + _report.EvidenceCount,
            FontSize = 11,
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 8, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "هذا المحرك محلي قائم على قواعد معلنة، وليس نموذج ذكاء اصطناعي. لا ينفذ إصلاحًا تلقائيًا، ولا يستنتج الأعطال من غياب البيانات.",
            FontSize = 11,
            Foreground = Brush("#F0C9A5"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        var knowledgeButton = new Button
        {
            Content = "فتح قاعدة المعرفة المحلية للموديلات والأعطال",
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(0, 8, 0, 0),
            Background = Brush("#14517A"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096"),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        knowledgeButton.Click += (_, _) => new RepairKnowledgeWindow { Owner = this }.ShowDialog();
        header.Children.Add(knowledgeButton);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.9, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });

        _list.ItemsSource = _items;
        _list.DisplayMemberPath = nameof(AdviceListItem.DisplayText);
        _list.Background = Brush("#071625");
        _list.Foreground = Brush("#DCEBFA");
        _list.BorderBrush = Brush("#263D58");
        _list.Padding = new Thickness(8);
        _list.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(_list, 0);
        body.Children.Add(_list);

        _details.IsReadOnly = true;
        _details.AcceptsReturn = true;
        _details.TextWrapping = TextWrapping.Wrap;
        _details.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _details.Background = Brush("#071625");
        _details.Foreground = Brush("#DCEBFA");
        _details.BorderBrush = Brush("#263D58");
        _details.Padding = new Thickness(12);
        _details.FontFamily = new FontFamily("Consolas");
        _details.FontSize = 10;
        _details.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_details, 1);
        body.Children.Add(_details);
        root.Children.Add(body);

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        footer.Children.Add(new TextBlock
        {
            Text = "Limitations",
            Foreground = Brush("#9FC7E8"),
            FontWeight = FontWeights.SemiBold
        });
        foreach (var limitation in _report.Limitations)
            footer.Children.Add(new TextBlock
            {
                Text = "• " + limitation,
                Foreground = Brush("#AFC2D7"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 10
            });
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        return root;
    }

    private void ShowSelected()
    {
        if (_list.SelectedItem is not AdviceListItem item)
            return;
        var advice = item.Advice;
        var text = new StringBuilder();
        text.AppendLine("TITLE: " + advice.Title);
        text.AppendLine("SEVERITY: " + advice.Severity);
        text.AppendLine("CONFIDENCE: " + advice.Confidence);
        text.AppendLine();
        text.AppendLine("ASSESSMENT:");
        text.AppendLine(advice.Assessment);
        text.AppendLine();
        text.AppendLine("EVIDENCE:");
        foreach (var evidence in advice.Evidence)
            text.AppendLine("- " + evidence);
        text.AppendLine();
        text.AppendLine("SAFE NEXT STEPS:");
        foreach (var step in advice.SafeNextSteps)
            text.AppendLine("- " + step);
        text.AppendLine();
        text.AppendLine("LIMITATION:");
        text.AppendLine(advice.Limitation);
        _details.Text = text.ToString();
    }

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));

    private sealed record AdviceListItem(RepairAdvice Advice)
    {
        public string DisplayText => $"[{Advice.Severity}] {Advice.Title}";
    }
}
