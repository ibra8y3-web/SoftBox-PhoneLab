using SoftBox.PhoneLab.Core.Help;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Searchable offline help for current SoftBox tools and their real security/compatibility boundaries.
/// </summary>
public sealed class HelpCenterWindow : Window
{
    private readonly TextBox _search = new();
    private readonly ListBox _articles = new();
    private readonly TextBox _content = new();
    private readonly ObservableCollection<HelpListItem> _items = [];

    public HelpCenterWindow()
    {
        Title = "SoftBox | مركز المساعدة دون إنترنت";
        Width = 1100;
        Height = 780;
        MinWidth = 820;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        Content = BuildContent();
        _articles.SelectionChanged += (_, _) => ShowArticle();
        _search.TextChanged += (_, _) => ApplySearch();
        Loaded += (_, _) => ApplySearch();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock
        {
            Text = "مركز المساعدة",
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "OFFLINE KNOWLEDGE • ARABIC & ENGLISH KEYWORDS • FEATURE LIMITATIONS",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "ابحث في إرشادات SoftBox المخزنة داخل البرنامج. المقالات تصف الأدوات الفعلية وحدودها ولا تفترض دعمًا غير موجود.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 11
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        _search.Height = 38;
        _search.Margin = new Thickness(0, 0, 0, 12);
        _search.Padding = new Thickness(10, 6, 10, 6);
        _search.Background = Brush("#0A1B2D");
        _search.Foreground = Brush("#EAF3FF");
        _search.BorderBrush = Brush("#35516F");
        _search.ToolTip = "Search: ADB, USB, APK, Backup, Firmware, DNS, Workflow...";
        DockPanel.SetDock(_search, Dock.Top);
        root.Children.Add(_search);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.85, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });

        _articles.ItemsSource = _items;
        _articles.DisplayMemberPath = nameof(HelpListItem.DisplayText);
        _articles.Background = Brush("#071625");
        _articles.Foreground = Brush("#DCEBFA");
        _articles.BorderBrush = Brush("#263D58");
        _articles.Padding = new Thickness(8);
        _articles.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(_articles, 0);
        body.Children.Add(_articles);

        _content.IsReadOnly = true;
        _content.AcceptsReturn = true;
        _content.TextWrapping = TextWrapping.Wrap;
        _content.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _content.Background = Brush("#071625");
        _content.Foreground = Brush("#DCEBFA");
        _content.BorderBrush = Brush("#263D58");
        _content.Padding = new Thickness(14);
        _content.FontSize = 12;
        _content.Text = "اختر موضوعًا من القائمة أو اكتب كلمة للبحث.";
        _content.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_content, 1);
        body.Children.Add(_content);

        root.Children.Add(body);
        return root;
    }

    private void ApplySearch()
    {
        var query = _search.Text;
        var results = OfflineHelpCatalog.Search(query);
        _items.Clear();
        foreach (var article in results)
            _items.Add(new HelpListItem(
                article.Id,
                article.TitleAr + " — " + article.TitleEn,
                article.Category,
                article));

        _articles.SelectedItem = _items.FirstOrDefault();
        if (_items.Count == 0)
            _content.Text = "لا توجد مقالات مطابقة. جرّب كلمة أخرى بالعربية أو الإنجليزية.";
        else
            ShowArticle();
    }

    private void ShowArticle()
    {
        if (_articles.SelectedItem is not HelpListItem item)
            return;

        var article = item.Article;
        _content.Text = string.Join(Environment.NewLine,
        [
            article.TitleAr,
            article.TitleEn,
            "CATEGORY: " + article.Category,
            "RELATED MODULE: " + article.RelatedModuleId,
            "KEYWORDS: " + string.Join(", ", article.Keywords),
            "",
            article.BodyAr
        ]);
    }

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));

    private sealed record HelpListItem(
        string Id,
        string DisplayText,
        string Category,
        OfflineHelpArticle Article);
}
