using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Displays live application/runtime details and the bundled third-party notice file, when available.
/// </summary>
public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = "SoftBox | حول البرنامج";
        Width = 920;
        Height = 720;
        MinWidth = 720;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "SoftBox",
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "Universal Smart Device Service & Repair Platform",
            FontSize = 12,
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 4, 0, 8)
        });
        header.Children.Add(new TextBlock
        {
            Text = "منصة موحدة لتشخيص وإدارة وصيانة الأجهزة الذكية، مع أدوات محلية تعمل عبر Windows وADB عندما تكون متاحة ومصرحًا بها.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#AFC2D7"),
            FontSize = 11
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var tabs = new TabControl
        {
            Background = Brush("#0D1C2E"),
            BorderBrush = Brush("#263D58")
        };

        var info = new StringBuilder();
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        info.AppendLine("PRODUCT: SoftBox");
        info.AppendLine("VERSION: " + (assembly.GetName().Version?.ToString() ?? "Unknown"));
        info.AppendLine("ASSEMBLY: " + assembly.GetName().Name);
        info.AppendLine("FRAMEWORK: " + RuntimeInformation.FrameworkDescription);
        info.AppendLine("OS: " + Environment.OSVersion);
        info.AppendLine("OS ARCHITECTURE: " + RuntimeInformation.OSArchitecture);
        info.AppendLine("PROCESS ARCHITECTURE: " + RuntimeInformation.ProcessArchitecture);
        info.AppendLine("64-BIT PROCESS: " + Environment.Is64BitProcess);
        info.AppendLine("BASE DIRECTORY: " + AppContext.BaseDirectory);
        info.AppendLine("USER DATA: " + Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftBox", "PhoneLab"));
        info.AppendLine();
        info.AppendLine("LOCAL MODULES: 36 section definitions are maintained in SoftBoxModuleCatalog.");
        info.AppendLine("DEVICE SUPPORT: Depends on USB mode, ADB authorization, vendor protocols and the connected handset.");
        info.AppendLine("HARDWARE CERTIFICATION: Not implied by application build or unit tests.");

        tabs.Items.Add(new TabItem
        {
            Header = "معلومات الإصدار",
            Content = CreateTextBox(info.ToString())
        });

        var noticesPath = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
        var noticeText = File.Exists(noticesPath)
            ? File.ReadAllText(noticesPath, Encoding.UTF8)
            : "THIRD-PARTY-NOTICES.md غير موجود في مجلد التثبيت الحالي. راجع إشعارات الطرف الثالث في مستودع SoftBox قبل إعادة توزيع التطبيق.";
        var noticesHeader = noticesPath;
        tabs.Items.Add(new TabItem
        {
            Header = "إشعارات الطرف الثالث",
            Content = new DockPanel
            {
                Margin = new Thickness(8),
                Children =
                {
                    new TextBlock
                    {
                        Text = File.Exists(noticesPath) ? "مصدر الإشعارات: " + noticesHeader : noticesHeader,
                        Foreground = Brush("#9FC7E8"),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 8)
                    },
                    CreateTextBox(noticeText)
                }
            }
        });

        root.Children.Add(tabs);
        return root;
    }

    private static TextBox CreateTextBox(string content)
        => new()
        {
            Text = content,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brush("#071625"),
            Foreground = Brush("#DCEBFA"),
            BorderBrush = Brush("#263D58"),
            Padding = new Thickness(12),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10
        };

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));
}
