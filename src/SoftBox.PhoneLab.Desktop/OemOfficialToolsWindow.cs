using SoftBox.PhoneLab.Core.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

public sealed class OemOfficialToolsWindow : Window
{
    private readonly OemServiceVendor _vendor;

    public OemOfficialToolsWindow(OemServiceVendor vendor)
    {
        _vendor = vendor;
        Title = "SoftBox | Official OEM Service Tools";
        Width = 900;
        Height = 700;
        MinWidth = 700;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "Official OEM Tools — " + _vendor,
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "روابط من الشركة أو المستودع الأصلي فقط. فتح الرابط لا يعني أن الأداة تدعم موديل الهاتف تلقائيًا.",
            Foreground = Brush("#F0C9A5"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var items = new StackPanel();
        foreach (var resource in OfficialOemToolCatalog.ForVendor(_vendor))
        {
            var card = new Border
            {
                Background = Brush("#0D1C2E"),
                BorderBrush = Brush("#263D58"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = resource.DisplayName, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brush("#7DD3FC") });
            panel.Children.Add(new TextBlock { Text = resource.Purpose, Foreground = Brush("#DCEBFA"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            panel.Children.Add(new TextBlock { Text = "Availability: " + resource.Availability, Foreground = Brush("#9FC7E8"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            panel.Children.Add(new TextBlock { Text = "Safety: " + resource.SafetyNote, Foreground = Brush("#F0C9A5"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) });
            var open = new Button
            {
                Content = "فتح المصدر الرسمي",
                Padding = new Thickness(10, 6, 10, 6),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = Brush("#14517A"),
                Foreground = Brushes.White,
                BorderBrush = Brush("#3E7096")
            };
            var url = resource.OfficialUrl;
            open.Click += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            panel.Children.Add(open);
            card.Child = panel;
            items.Children.Add(card);
        }
        root.Children.Add(new ScrollViewer { Content = items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return root;
    }

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));
}
