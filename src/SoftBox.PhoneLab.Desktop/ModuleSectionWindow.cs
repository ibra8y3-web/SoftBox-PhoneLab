using SoftBox.PhoneLab.Core.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Shared presentation for audited modules. Each module has its own window and only
/// real, wired actions are rendered as actionable buttons.
/// </summary>
public sealed class ModuleSectionWindow : Window
{
    private readonly SoftBoxModule _module;
    private readonly Action<string> _openAction;
    private readonly Func<string, bool> _canExecute;

    public ModuleSectionWindow(SoftBoxModule module, Action<string> openAction, Func<string, bool>? canExecute = null)
    {
        _module = module ?? throw new ArgumentNullException(nameof(module));
        _openAction = openAction ?? throw new ArgumentNullException(nameof(openAction));
        _canExecute = canExecute ?? (_ => true);
        Title = $"SoftBox | {_module.NameAr} — {_module.NameEn}";
        Width = 980;
        Height = 760;
        MinWidth = 760;
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
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock
        {
            Text = _module.NameAr,
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = _module.NameEn + "  •  MODULE " + _module.Order.ToString("00"),
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 8)
        });
        var status = new Border
        {
            Background = StatusBackground(_module.Status),
            BorderBrush = StatusBrush(_module.Status),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = new TextBlock
            {
                Text = SoftBoxModuleCatalog.StatusLabel(_module.Status),
                Foreground = StatusBrush(_module.Status),
                FontWeight = FontWeights.SemiBold
            }
        };
        header.Children.Add(status);
        header.Children.Add(new TextBlock
        {
            Text = _module.SummaryAr,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 10, 0, 4),
            FontSize = 13
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new Border
        {
            Background = Brush("#101F32"),
            BorderBrush = Brush("#263D58"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 12, 0, 0),
            Child = new TextBlock
            {
                Text = "قاعدة SoftBox: لا تظهر أداة كأنها جاهزة إلا إذا كان لها تنفيذ فعلي. عناصر «لم يبدأ» و«غير مدعوم» معلومات حالة فقط، ولا يوجد لها زر تشغيل.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#9CB3CC"),
                FontSize = 11
            }
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var features = new StackPanel();
        features.Children.Add(new TextBlock
        {
            Text = "الأدوات والوظائف داخل القسم",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#DCEBFA"),
            Margin = new Thickness(2, 0, 2, 10)
        });

        foreach (var feature in _module.Features)
        {
            var card = new Border
            {
                Background = Brush("#0F1E32"),
                BorderBrush = Brush("#263D58"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 9)
            };
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock
            {
                Text = feature.NameAr,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#EAF3FF"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 8, 5)
            };
            Grid.SetColumn(title, 0);
            layout.Children.Add(title);

            var state = new TextBlock
            {
                Text = SoftBoxModuleCatalog.StatusLabel(feature.Status),
                Foreground = StatusBrush(feature.Status),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 4, 5)
            };
            Grid.SetColumn(state, 1);
            layout.Children.Add(state);

            var english = new TextBlock
            {
                Text = feature.NameEn,
                Foreground = Brush("#7894B3"),
                FontSize = 9,
                Margin = new Thickness(0, 0, 8, 6)
            };
            Grid.SetRow(english, 1);
            layout.Children.Add(english);

            var description = new TextBlock
            {
                Text = feature.DetailsAr,
                Foreground = Brush("#AFC2D7"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 8, 2)
            };
            Grid.SetRow(description, 2);
            Grid.SetColumnSpan(description, 2);
            layout.Children.Add(description);

            if (!string.IsNullOrWhiteSpace(feature.ActionId))
            {
                var actionId = feature.ActionId;
                var allowed = _canExecute(actionId);
                var open = new Button
                {
                    Content = allowed ? "فتح الأداة الموجودة" : "الصلاحية غير متاحة",
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(4, 0, 4, 0),
                    MinWidth = 120,
                    Background = Brush(allowed ? "#14517A" : "#273449"),
                    Foreground = Brushes.White,
                    BorderBrush = Brush("#2F83B5"),
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsEnabled = allowed,
                    ToolTip = allowed ? null : "هذا الحساب لا يملك صلاحية تشغيل هذه الأداة."
                };
                open.Click += (_, _) => _openAction(actionId);
                Grid.SetColumn(open, 2);
                Grid.SetRowSpan(open, 3);
                layout.Children.Add(open);
            }

            card.Child = layout;
            features.Children.Add(card);
        }

        scroll.Content = features;
        root.Children.Add(scroll);
        return root;
    }

    private static SolidColorBrush StatusBrush(ModuleImplementationStatus status) => Brush(status switch
    {
        ModuleImplementationStatus.Verified => "#22C55E",
        ModuleImplementationStatus.Tested => "#4ADE80",
        ModuleImplementationStatus.Implemented => "#38BDF8",
        ModuleImplementationStatus.InDevelopment => "#F59E0B",
        ModuleImplementationStatus.Planned => "#94A3B8",
        ModuleImplementationStatus.Unsupported => "#F87171",
        _ => "#94A3B8"
    });

    private static SolidColorBrush StatusBackground(ModuleImplementationStatus status) => Brush(status switch
    {
        ModuleImplementationStatus.Verified or ModuleImplementationStatus.Tested => "#123326",
        ModuleImplementationStatus.Implemented => "#102D42",
        ModuleImplementationStatus.InDevelopment => "#382C16",
        ModuleImplementationStatus.Unsupported => "#3A1D26",
        _ => "#1C293A"
    });

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));
}
