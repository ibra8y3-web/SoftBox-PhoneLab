using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Hosts section pages and tools inside the main workspace instead of opening a separate window for each one.
/// The original Window object is kept (invisible and non-activating) so its code-behind, Owner relationships,
/// Close() and dialogs keep working; only its visual content is shown inside <see cref="EmbeddedBody"/>.
/// </summary>
public partial class MainWindow
{
    private sealed class EmbeddedPage
    {
        public required Window Window { get; init; }
        public required UIElement Content { get; init; }
        public required string Title { get; init; }
        public Brush? Background { get; init; }
        public Brush? Foreground { get; init; }
        public FontFamily? FontFamily { get; init; }
        public ResourceDictionary? Resources { get; init; }
    }

    private readonly List<EmbeddedPage> _embeddedPages = [];
    private bool _installingAdb;

    public Visibility AdbInstallVisibility => _adb.IsAvailable ? Visibility.Collapsed : Visibility.Visible;

    // ───────────────────────── Embedded navigation ─────────────────────────

    internal bool ShowEmbedded(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.Content is not UIElement content)
        {
            window.Show(); // nothing to embed; fall back to the normal window
            return true;
        }

        var page = new EmbeddedPage
        {
            Window = window,
            Content = content,
            Title = string.IsNullOrWhiteSpace(window.Title) ? "SoftBox" : window.Title,
            Background = window.Background,
            Foreground = window.Foreground,
            FontFamily = window.FontFamily,
            Resources = CloneResources(window.Resources)
        };

        // Keep bindings that relied on the Window's DataContext working after the content moves.
        if (content is FrameworkElement element &&
            window.DataContext is not null &&
            element.ReadLocalValue(FrameworkElement.DataContextProperty) == DependencyProperty.UnsetValue)
        {
            element.DataContext = window.DataContext;
        }

        // Small dialogs keep their original width (centred); big tool windows fill the workspace.
        if (content is FrameworkElement sized && double.IsNaN(sized.Width) &&
            !double.IsNaN(window.Width) && window.Width < 900)
        {
            sized.Width = window.Width;
            sized.HorizontalAlignment = HorizontalAlignment.Center;
        }

        window.Content = null;
        PrepareInvisibleOwnerWindow(window);
        window.Closed += (_, _) => OnEmbeddedWindowClosed(page);

        _embeddedPages.Add(page);
        DisplayEmbeddedPage(page);
        SoftBoxLocalization.ApplyTo(content);

        try { window.Show(); }
        catch (Exception ex)
        {
            _embeddedPages.Remove(page);
            DisplayTopEmbeddedPageOrHome();
            StatusMessage = "تعذر فتح الأداة داخل الواجهة: " + ex.Message;
            Notify(nameof(StatusMessage));
            return false;
        }
        return true;
    }

    private void PrepareInvisibleOwnerWindow(Window window)
    {
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.AllowsTransparency = true;
        window.Background = Brushes.Transparent;
        window.Opacity = 0;
        window.SizeToContent = SizeToContent.Manual;
        window.MinWidth = 0;
        window.MinHeight = 0;
        window.Width = 1;
        window.Height = 1;
        window.WindowState = WindowState.Normal;
        window.WindowStartupLocation = WindowStartupLocation.Manual;

        // Centre it on the main window so MessageBox/dialogs owned by it appear in the right place.
        var centre = new Point(ActualWidth / 2, ActualHeight / 2);
        try
        {
            var source = PresentationSource.FromVisual(this);
            var screenPoint = PointToScreen(centre);
            if (source?.CompositionTarget is not null)
                screenPoint = source.CompositionTarget.TransformFromDevice.Transform(screenPoint);
            window.Left = screenPoint.X;
            window.Top = screenPoint.Y;
        }
        catch
        {
            window.Left = Left + centre.X;
            window.Top = Top + centre.Y;
        }
    }

    private static ResourceDictionary? CloneResources(ResourceDictionary? source)
    {
        if (source is null || (source.Count == 0 && source.MergedDictionaries.Count == 0)) return null;
        try
        {
            var clone = new ResourceDictionary();
            foreach (var dictionary in source.MergedDictionaries) clone.MergedDictionaries.Add(dictionary);
            foreach (System.Collections.DictionaryEntry entry in source) clone[entry.Key] = entry.Value;
            return clone;
        }
        catch
        {
            return null;
        }
    }

    private void DisplayEmbeddedPage(EmbeddedPage page)
    {
        EmbeddedBody.Child = null;
        EmbeddedBody.Resources = page.Resources ?? new ResourceDictionary();
        EmbeddedBody.Background = page.Background ?? Brushes.Transparent;
        if (page.Foreground is not null)
            System.Windows.Documents.TextElement.SetForeground(EmbeddedBody, page.Foreground);
        if (page.FontFamily is not null)
            System.Windows.Documents.TextElement.SetFontFamily(EmbeddedBody, page.FontFamily);
        EmbeddedBody.Child = page.Content;
        EmbeddedTitle.Text = SoftBoxLocalization.Translate(page.Title);

        // Hide the three workspace columns (this also hides the HwndHost used for phone mirroring,
        // which would otherwise be drawn on top of the embedded page).
        DevicesColumn.Visibility = Visibility.Visible; // Keep connected phones and component tools visible.
        ViewerColumn.Visibility = Visibility.Collapsed;
        DetailsColumn.Visibility = Visibility.Collapsed;
        PhoneScreenColumn.Visibility = Visibility.Visible;
        EmbeddedHostPanel.Visibility = Visibility.Visible;
    }

    private void DisplayTopEmbeddedPageOrHome()
    {
        if (_embeddedPages.Count > 0)
        {
            DisplayEmbeddedPage(_embeddedPages[^1]);
            return;
        }

        EmbeddedBody.Child = null;
        EmbeddedHostPanel.Visibility = Visibility.Collapsed;
        DevicesColumn.Visibility = Visibility.Visible;
        ViewerColumn.Visibility = Visibility.Visible;
        PhoneScreenColumn.Visibility = Visibility.Visible;
        DetailsColumn.Visibility = Visibility.Visible;
    }

    private void OnEmbeddedWindowClosed(EmbeddedPage page)
    {
        var wasTop = _embeddedPages.Count > 0 && ReferenceEquals(_embeddedPages[^1], page);
        _embeddedPages.Remove(page);
        if (wasTop) DisplayTopEmbeddedPageOrHome();
    }

    private void CloseTopEmbeddedPage()
    {
        if (_embeddedPages.Count == 0) return;
        var page = _embeddedPages[^1];
        try
        {
            page.Window.Close(); // Closed handler removes the page and shows the previous one
        }
        catch
        {
            OnEmbeddedWindowClosed(page);
        }
    }

    private void CloseAllEmbeddedPages()
    {
        while (_embeddedPages.Count > 0)
        {
            var before = _embeddedPages.Count;
            CloseTopEmbeddedPage();
            if (_embeddedPages.Count >= before) break; // a page cancelled its Closing event
        }
        if (_embeddedPages.Count == 0) DisplayTopEmbeddedPageOrHome();
    }

    private void EmbeddedBack_Click(object sender, RoutedEventArgs e) => CloseTopEmbeddedPage();

    private void EmbeddedHome_Click(object sender, RoutedEventArgs e) => CloseAllEmbeddedPages();

    // ───────────────────────── ADB installation / diagnosis ─────────────────────────

    private async void InstallAdb_Click(object sender, RoutedEventArgs e) => await EnsureAdbInstalledAsync(askFirst: false);

    public async Task<bool> EnsureAdbInstalledAsync(bool askFirst)
    {
        if (_adb.IsAvailable) return true;
        if (_installingAdb) return false;

        if (askFirst)
        {
            var answer = MessageBox.Show(this,
                "لم يتم العثور على أداة ADB، وبدونها لن يتعرف SoftBox على الهاتف ولن يظهر على الهاتف طلب «السماح بتصحيح أخطاء USB».\n\n" +
                "هل تريد تنزيل Android Platform-Tools الرسمية من Google وتثبيتها الآن؟ (حوالي 10 ميجابايت، تحتاج اتصال إنترنت، ولا تحتاج صلاحيات مدير)",
                "تثبيت ADB", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return false;
        }

        _installingAdb = true;
        try
        {
            var progress = new Progress<string>(message =>
            {
                StatusMessage = message;
                Notify(nameof(StatusMessage));
            });
            await AdbInstallerService.InstallAsync(progress);

            if (!_adb.Refresh())
                throw new InvalidOperationException("تم التنزيل لكن SoftBox لم يتمكن من العثور على adb.exe.");

            Notify(nameof(EngineStatus));
            Notify(nameof(DiscoveryHint));
            Notify(nameof(AdbInstallVisibility));

            try { await _adb.StartServerAsync(); } catch { /* discovery will report problems */ }
            _deviceSignature = "adb-installed";
            StatusMessage = "تم تثبيت ADB بنجاح. افتح قفل الهاتف ووافق على «السماح بتصحيح أخطاء USB».";
            Notify(nameof(StatusMessage));
            await RefreshDevicesAsync();
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = "تعذر تثبيت ADB: " + ex.Message;
            Notify(nameof(StatusMessage));
            MessageBox.Show(this,
                "تعذر تنزيل أو تثبيت ADB تلقائيًا:\n" + ex.Message + "\n\n" +
                "حل يدوي: نزّل SDK Platform-Tools لنظام Windows من developer.android.com/tools/releases/platform-tools ثم فك الضغط " +
                "إلى المجلد: " + System.IO.Path.Combine(AdbInstallerService.InstallRoot, "platform-tools") +
                "\nأو أضف مجلد platform-tools إلى متغير PATH ثم أعد تشغيل SoftBox.",
                "تثبيت ADB", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            _installingAdb = false;
        }
    }

    /// <summary>
    /// Replaces the old generic "choose an ADB-authorized phone first" message with the real reason and,
    /// when ADB itself is missing, offers to install it.
    /// </summary>
    private void ExplainNoAuthorizedDevice(string title)
    {
        if (!_adb.IsAvailable)
        {
            _ = EnsureAdbInstalledAsync(askFirst: true);
            return;
        }

        var waiting = Devices.Any(device => device.State.Equals("unauthorized", StringComparison.OrdinalIgnoreCase));
        var message = waiting
            ? "الهاتف متصل لكنه لم يمنح التصريح بعد.\n\nافتح قفل شاشة الهاتف ووافق على نافذة «السماح بتصحيح أخطاء USB»، " +
              "واختر «السماح دائمًا من هذا الكمبيوتر». إذا لم تظهر النافذة: من خيارات المطور اختر «إلغاء تصاريح تصحيح أخطاء USB» ثم افصل الكابل وأعد توصيله."
            : Devices.Count > 0
                ? "الهاتف ظاهر لكنه غير جاهز للتشخيص (الحالة: " + (Devices[0].State) + "). افصل الكابل وأعد توصيله ثم اختر الهاتف من قائمة الأجهزة."
                : "لم يظهر أي هاتف عبر ADB.\n\n" +
                  "1) فعّل «خيارات المطور» ثم «تصحيح أخطاء USB» في الهاتف.\n" +
                  "2) وصّل الهاتف بكابل بيانات (وليس شحن فقط) واختر وضع «نقل الملفات».\n" +
                  "3) افتح قفل الهاتف ووافق على نافذة التصريح عندما تظهر.\n" +
                  "4) إن لم يتعرف الكمبيوتر عليه فثبّت تعريف USB الخاص بالشركة (مثلًا Samsung USB Driver) ثم أعد التوصيل.";
        MessageBox.Show(this, message, SoftBoxLocalization.Translate(title),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

internal static class EmbeddedWindowExtensions
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, object?[]> Results = new();

    /// <summary>The main window that hosts every page. Set once by MainWindow.</summary>
    public static MainWindow? Host { get; set; }

    /// <summary>Shows <paramref name="window"/> inside the main workspace instead of as a separate window.</summary>
    public static void ShowEmbedded(this Window window, MainWindow host) => host.ShowEmbedded(window);

    public static void ShowEmbedded(this Window window)
    {
        if (Host is { } host) host.ShowEmbedded(window);
        else window.Show();
    }

    /// <summary>Modal-style call inside the workspace: waits until the page closes and returns its result.</summary>
    public static bool? ShowDialogEmbedded(this Window window)
    {
        if (Host is not { } host) return window.ShowDialog();

        var frame = new System.Windows.Threading.DispatcherFrame();
        window.Closed += (_, _) => frame.Continue = false;
        if (!host.ShowEmbedded(window)) return null;
        if (frame.Continue) System.Windows.Threading.Dispatcher.PushFrame(frame);
        return Results.TryGetValue(window, out var box) ? (bool?)box[0] : null;
    }

    /// <summary>Replacement for Window.DialogResult that works for both embedded pages and real dialogs.</summary>
    public static void SetResult(this Window window, bool? result)
    {
        Results.Remove(window);
        Results.Add(window, [result]);
        try { window.DialogResult = result; }
        catch (InvalidOperationException) { window.Close(); } // not shown with ShowDialog → embedded page
    }
}
