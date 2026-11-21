using SoftBox.PhoneLab.Core.Fastboot;
using SoftBox.PhoneLab.Core.Firmware;
using SoftBox.PhoneLab.Desktop.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Executes a pre-inspected, serial-pinned Xiaomi Fastboot ROM plan as individual Fastboot commands.
/// Never executes the ROM's BAT/SH script. Any unsupported or ambiguous command blocks the whole plan.
/// </summary>
public sealed class XiaomiFastbootRomFlashWindow : Window
{
    private readonly FastbootService _fastboot = new();
    private readonly string _serial;
    private readonly string _manufacturer;
    private readonly string _model;
    private readonly TextBox _scriptPath = new();
    private readonly TextBox _region = new();
    private readonly TextBox _confirmation = new();
    private readonly TextBox _details = new();
    private readonly TextBlock _status = new();
    private readonly CheckBox _confirmOfficialSource = new()
    {
        Content = "أؤكد أن الحزمة كاملة من مصدر Xiaomi موثوق ومخصصة لنفس Product"
    };
    private readonly CheckBox _confirmRegion = new()
    {
        Content = "أؤكد أن منطقة الروم (Global/EEA/CN/غيرها) مطابقة للجهاز"
    };
    private readonly CheckBox _confirmBackup = new()
    {
        Content = "أؤكد وجود نسخة احتياطية خارج الهاتف وإمكانية استعادتها"
    };
    private readonly CheckBox _confirmAntiRollbackUnknown = new()
    {
        Content = "لا تتوفر قيمة Anti-Rollback قابلة للمقارنة؛ تحققت منها يدويًا من مصدر OEM"
    };
    private readonly CheckBox _confirmWipe = new()
    {
        Content = "أسمح صراحة بمسح بيانات المستخدم لأن سكربت flash_all يتطلب ذلك"
    };
    private readonly Button _inspectButton;
    private readonly Button _flashButton;
    private readonly Button _browseButton;

    private XiaomiFastbootRomFlashPlan? _plan;
    private FastbootStateReport? _lastDeviceState;
    private bool _running;

    public XiaomiFastbootRomFlashWindow(
        string serial,
        string manufacturer,
        string model)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف Fastboot/ADB غير صالح.", nameof(serial));
        if (!XiaomiFastbootDiagnosticsPolicy.IsXiaomiManufacturer(manufacturer))
            throw new UnauthorizedAccessException("تم رفض فتح التفليش الكامل لأن هوية الجهاز ليست Xiaomi/Redmi/POCO.");

        _serial = serial;
        _manufacturer = manufacturer;
        _model = model ?? "";

        Title = "SoftBox | Xiaomi Full Fastboot ROM Service";
        Width = 1120;
        Height = 860;
        MinWidth = 900;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _browseButton = ButtonControl("اختيار flash_all*.bat من مجلد ROM المفكوك", "#243B56", BrowseScript_Click);
        _inspectButton = ButtonControl("فحص الخطة والصور وAnti-Rollback", "#14517A", Inspect_Click);
        _flashButton = ButtonControl("بدء تفليش ROM بعد التأكيد النهائي", "#713B43", Flash_Click);
        _flashButton.IsEnabled = false;
        Content = BuildContent();
        Closing += Window_Closing;
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "Xiaomi / Redmi / POCO — Full Fastboot ROM",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(TextBlock(
            $"الجهاز المحدد: {_manufacturer} {_model} | Serial: {Mask(_serial)}",
            12, "#B7C9DE"));
        header.Children.Add(TextBlock(
            "يتطلب Bootloader Unlocked وحزمة Fastboot ROM مفكوكة محليًا. SoftBox يحلل flash_all.bat كبيانات ويحوّل أوامر flash المحددة إلى أوامر Fastboot منفصلة؛ لا يشغّل BAT أو EXE أو سكربت OEM. أي أمر غير معروف أو قسم حساس يوقف الخطة.",
            12, "#F0C9A5"));
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(_status);
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(_browseButton);
        actions.Children.Add(_inspectButton);
        actions.Children.Add(_flashButton);
        footer.Children.Add(actions);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var content = new StackPanel();

        var scriptRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(_browseButton, Dock.Left);
        _scriptPath.IsReadOnly = true;
        _scriptPath.Background = Brush("#071625");
        _scriptPath.Foreground = Brush("#DCEBFA");
        _scriptPath.BorderBrush = Brush("#263D58");
        _scriptPath.Padding = new Thickness(8);
        scriptRow.Children.Add(_scriptPath);
        content.Children.Add(Panel(scriptRow));

        _region.ToolTip = "اكتب المنطقة الموجودة على الحزمة مثل Global أو EEA أو CN أو IN";
        _region.Background = Brush("#071625");
        _region.Foreground = Brush("#DCEBFA");
        _region.BorderBrush = Brush("#263D58");
        _region.Padding = new Thickness(8);
        _region.MinWidth = 220;
        content.Children.Add(Panel(new StackPanel
        {
            Children =
            {
                TextBlock("منطقة/إصدار الحزمة — إدخال الفني", 13, "#7DD3FC"),
                _region
            }
        }));

        foreach (var check in new[]
        {
            _confirmOfficialSource, _confirmRegion, _confirmBackup,
            _confirmAntiRollbackUnknown, _confirmWipe
        })
        {
            check.Margin = new Thickness(4);
            check.Foreground = Brush("#F0C9A5");
            content.Children.Add(check);
        }

        _confirmation.Background = Brush("#071625");
        _confirmation.Foreground = Brush("#DCEBFA");
        _confirmation.BorderBrush = Brush("#263D58");
        _confirmation.Padding = new Thickness(8);
        _confirmation.MinWidth = 300;
        _confirmation.ToolTip = "اكتب FLASH ROM وآخر أربعة أحرف من Serial؛ أضف WIPE عندما تتطلب الحزمة مسح البيانات.";
        content.Add(Panel(new StackPanel
        {
            Children =
            {
                TextBlock("تأكيد كتابي مستقل قبل التفليش", 13, "#7DD3FC"),
                _confirmation
            }
        }));

        _details.IsReadOnly = true;
        _details.AcceptsReturn = true;
        _details.TextWrapping = TextWrapping.Wrap;
        _details.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _details.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _details.Background = Brush("#071625");
        _details.Foreground = Brush("#DCEBFA");
        _details.BorderBrush = Brush("#263D58");
        _details.Padding = new Thickness(10);
        _details.FontFamily = new FontFamily("Consolas");
        _details.FontSize = 10;
        _details.MinHeight = 260;
        _details.Text = "1) انقل الهاتف إلى Fastboot باستخدام زر Fastboot في النافذة السابقة.\n" +
                        "2) فك حزمة ROM محليًا دون تشغيل أي ملف منها.\n" +
                        "3) اختر flash_all.bat أو flash_all_except_storage.bat ثم افحصها هنا.\n" +
                        "4) راجع كل خطوة والصورة وبصمتها وقرار Anti-Rollback قبل التأكيد.";
        content.Add(Panel(_details));

        root.Children.Add(new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        _status.Text = "في انتظار فحص الجهاز والحزمة. لن يتم تنفيذ أي كتابة قبل اكتمال جميع بوابات الأمان.";
        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
        return root;
    }

    private void BrowseScript_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "اختيار سكربت ROM المفكوك — لا يتم تشغيله",
            Filter = "Xiaomi supported scripts (flash_all*.bat)|flash_all*.bat",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        _scriptPath.Text = Path.GetFullPath(dialog.FileName);
        _plan = null;
        _lastDeviceState = null;
        _flashButton.IsEnabled = false;
        _status.Text = "تغير ملف السكربت. أعد الفحص قبل أي محاولة كتابة.";
    }

    private async void Inspect_Click(object sender, RoutedEventArgs e)
    {
        _inspectButton.IsEnabled = false;
        _flashButton.IsEnabled = false;
        try
        {
            if (!_fastboot.IsAvailable)
                throw new FileNotFoundException("fastboot.exe غير متاح. ثبت Android SDK Platform-Tools أو أضفه إلى Tools/platform-tools.");
            if (string.IsNullOrWhiteSpace(_scriptPath.Text) || !File.Exists(_scriptPath.Text))
                throw new FileNotFoundException("اختر flash_all.bat أو flash_all_except_storage.bat من مجلد ROM المفكوك.");

            var state = await ReadSelectedDeviceStateAsync();
            var plan = await XiaomiFastbootRomFlashPlanBuilder.BuildAsync(_scriptPath.Text, state.Product);
            _lastDeviceState = state;
            _plan = plan;

            var lines = new List<string>
            {
                "XIAOMI FULL FASTBOOT ROM PREFLIGHT",
                "MODEL: " + _model,
                "SERIAL: " + Mask(_serial),
                "FASTBOOT PRODUCT: " + (state.Product ?? "UNKNOWN"),
                "BOOTLOADER: " + state.LockState,
                "SCRIPT: " + plan.ScriptName,
                "PACKAGE PRODUCT REQUIREMENTS: " + string.Join(", ", plan.RequiredProducts),
                "PRODUCT MATCH: " + plan.ProductMatch,
                "FIRMWARE ANTI-ROLLBACK: " + (plan.FirmwareAntiRollbackIndex?.ToString() ?? "UNKNOWN"),
                "DEVICE ANTI-ROLLBACK: " + (ReadDeviceAntiRollback(state)?.ToString() ?? "UNKNOWN"),
                "FLASH IMAGES: " + plan.FlashImageCount,
                "TOTAL IMAGE BYTES: " + plan.TotalImageBytes,
                "USER DATA WIPE REQUIRED: " + plan.RequiresDataWipe,
                "",
                "ORDERED OPERATIONS:"
            };
            lines.AddRange(plan.Steps.Select(step => step.Kind switch
            {
                XiaomiFastbootRomStepKind.FlashPartition =>
                    $"{step.Index}. FLASH {step.Partition} | {Path.GetFileName(step.ImagePath)} | {step.ImageBytes:N0} bytes | SHA256 {step.Sha256}",
                XiaomiFastbootRomStepKind.WipeUserData =>
                    $"{step.Index}. WIPE USERDATA (DESTRUCTIVE; requires separate consent)",
                XiaomiFastbootRomStepKind.RebootFastbootd =>
                    $"{step.Index}. REBOOT FASTBOOTD",
                _ => $"{step.Index}. UNSUPPORTED"
            }));
            lines.Add("");
            lines.AddRange(plan.Findings.Select(item => "- " + item));
            lines.Add("");
            lines.Add("NO BATCH FILE OR OEM EXECUTABLE WAS RUN.");

            _details.Text = string.Join(Environment.NewLine, lines);
            var arb = EvaluateAntiRollback(plan, state);
            _flashButton.IsEnabled = plan.CanExecute &&
                state.LockState == BootloaderLockState.Unlocked &&
                !arb.Blocked;
            _status.Text = !_flashButton.IsEnabled
                ? "الخطة غير مؤهلة للتنفيذ: تحقق من Product وBootloader وAnti-Rollback والأخطاء المعروضة."
                : "اكتمل الفحص. أكمل تأكيد المصدر والمنطقة والنسخة الاحتياطية وAnti-Rollback قبل Start.";
        }
        catch (Exception ex)
        {
            _plan = null;
            _lastDeviceState = null;
            _flashButton.IsEnabled = false;
            _details.AppendText(Environment.NewLine + Environment.NewLine + "PREFLIGHT FAILED: " + ex);
            _status.Text = "فشل فحص الحزمة أو الجهاز: " + ex.Message;
        }
        finally
        {
            _inspectButton.IsEnabled = true;
        }
    }

    private async void Flash_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
            return;

        try
        {
            var plan = _plan ?? throw new InvalidOperationException("افحص ROM أولًا.");
            var inspectedState = _lastDeviceState ?? throw new InvalidOperationException("لم يتم حفظ حالة Fastboot الحالية.");

            if (!plan.CanExecute)
                throw new InvalidOperationException("Product الخاص بالهاتف لا يطابق الحزمة أو لا توجد صور flash مدعومة.");
            if (inspectedState.LockState != BootloaderLockState.Unlocked)
                throw new UnauthorizedAccessException("Bootloader يجب أن يكون Unlocked بشكل صريح.");
            if (!_confirmOfficialSource.IsChecked.GetValueOrDefault() ||
                !_confirmRegion.IsChecked.GetValueOrDefault() ||
                !_confirmBackup.IsChecked.GetValueOrDefault() ||
                string.IsNullOrWhiteSpace(_region.Text.Trim()))
                throw new InvalidOperationException("يلزم تأكيد المصدر والمنطقة والنسخة الاحتياطية وكتابة اسم المنطقة.");
            if (plan.RequiresDataWipe && !_confirmWipe.IsChecked.GetValueOrDefault())
                throw new InvalidOperationException("السكربت يتضمن مسح userdata. وافق على المسح صراحة أو اختر سكربت حفظ البيانات.");
            if (EvaluateAntiRollback(plan, inspectedState).Blocked)
                throw new InvalidOperationException("تعذر إثبات سلامة Anti-Rollback أو أن مستوى الروم أقل من مستوى الهاتف.");
            if (EvaluateAntiRollback(plan, inspectedState).RequiresManualAcknowledgement &&
                !_confirmAntiRollbackUnknown.IsChecked.GetValueOrDefault())
                throw new InvalidOperationException("يلزم تحقق Anti-Rollback يدويًا عند غياب قيمة قابلة للمقارنة.");

            var suffix = _serial.Length > 4 ? _serial[^4..] : _serial;
            var expectedConfirmation = plan.RequiresDataWipe
                ? "FLASH ROM WIPE " + suffix
                : "FLASH ROM " + suffix;
            if (!string.Equals(_confirmation.Text.Trim(), expectedConfirmation, StringComparison.Ordinal))
                throw new InvalidOperationException("عبارة التأكيد غير صحيحة. أدخل النص المطلوب كما هو.");

            var freshState = await ReadSelectedDeviceStateAsync();
            var freshPlan = await XiaomiFastbootRomFlashPlanBuilder.BuildAsync(_scriptPath.Text, freshState.Product);
            if (!PlansMatch(plan, freshPlan))
                throw new InvalidDataException("تغير سكربت ROM أو صور Firmware بعد الفحص. أعد الفحص قبل التفليش.");
            if (!freshPlan.CanExecute || freshState.LockState != BootloaderLockState.Unlocked)
                throw new UnauthorizedAccessException("لم تعد هوية الهاتف أو حالة Bootloader تسمح بالكتابة.");
            if (EvaluateAntiRollback(freshPlan, freshState).Blocked)
                throw new UnauthorizedAccessException("فشل تحقق Anti-Rollback قبل الكتابة.");

            var summary = string.Join(Environment.NewLine, plan.Steps.Select(step =>
                step.Kind == XiaomiFastbootRomStepKind.FlashPartition
                    ? $"{step.Index}. {step.Partition}: {Path.GetFileName(step.ImagePath)}"
                    : step.Kind == XiaomiFastbootRomStepKind.WipeUserData
                        ? $"{step.Index}. مسح بيانات المستخدم"
                        : $"{step.Index}. الانتقال إلى Fastbootd"));
            var warning = plan.RequiresDataWipe
                ? "\n\nتحذير مهم: هذه الحزمة ستنفذ مسح بيانات المستخدم بشكل لا يمكن التراجع عنه."
                : "\n\nهذه العملية ستكتب عدة أقسام وقد تجعل الجهاز غير قابل للإقلاع إذا كانت الحزمة غير صحيحة.";
            var answer = MessageBox.Show(this,
                $"سيبدأ SoftBox تنفيذ خطة ROM على الهاتف المحدد فقط:\n{_manufacturer} {_model}\nSerial: {Mask(_serial)}\nProduct: {freshState.Product}\nRegion asserted: {_region.Text.Trim()}\n\n{summary}{warning}\n\nلن يتم تشغيل ملف BAT. سيتم تنفيذ الأوامر المسموحة خطوة بخطوة وإيقاف العملية عند أول خطأ. متابعة؟",
                "تأكيد نهائي لتفليش Xiaomi Full ROM", MessageBoxButton.YesNo, MessageBoxImage.Stop);
            if (answer != MessageBoxResult.Yes)
                return;

            _running = true;
            _browseButton.IsEnabled = false;
            _inspectButton.IsEnabled = false;
            _flashButton.IsEnabled = false;
            _details.AppendText(Environment.NewLine + Environment.NewLine +
                "EXECUTION STARTED " + DateTimeOffset.Now.ToString("O") +
                Environment.NewLine + "No OEM script is being launched.");

            try
            {
                foreach (var step in freshPlan.Steps)
                {
                    // Revalidate identity and unlocked state before each write or state transition.
                    var currentState = await ReadSelectedDeviceStateAsync();
                    if (!string.Equals(currentState.Product, freshState.Product, StringComparison.Ordinal))
                        throw new UnauthorizedAccessException("تغير Product أثناء التفليش؛ تم إيقاف باقي الخطوات.");
                    if (currentState.LockState != BootloaderLockState.Unlocked)
                        throw new UnauthorizedAccessException("لم يعد Fastboot يؤكد Bootloader Unlocked؛ تم إيقاف باقي الخطوات.");
                    var currentArb = EvaluateAntiRollback(freshPlan, currentState);
                    if (currentArb.Blocked)
                        throw new UnauthorizedAccessException("فشل تحقق Anti-Rollback أثناء التفليش.");

                    _details.AppendText(Environment.NewLine + $"[{step.Index}/{freshPlan.Steps.Count}] {step.Kind} ...");
                    _status.Text = $"تنفيذ الخطوة {step.Index} من {freshPlan.Steps.Count}: {step.Kind}";
                    switch (step.Kind)
                    {
                        case XiaomiFastbootRomStepKind.FlashPartition:
                        {
                            if (step.ImagePath is null || step.Partition is null || step.Sha256 is null)
                                throw new InvalidDataException("بيانات خطوة Flash غير مكتملة.");
                            await using var imageStream = new FileStream(
                                step.ImagePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                            var hash = Convert.ToHexString(await SHA256.HashDataAsync(imageStream))
                                .ToLowerInvariant();
                            if (!CryptographicOperations.FixedTimeEquals(
                                Convert.FromHexString(hash), Convert.FromHexString(step.Sha256)))
                                throw new InvalidDataException("تغيرت بايتات صورة ROM قبل إرسالها إلى Fastboot: " + Path.GetFileName(step.ImagePath));

                            var output = await _fastboot.FlashRomImageAsync(
                                _serial, step.Partition, step.ImagePath);
                            _details.AppendText(Environment.NewLine + output);
                            break;
                        }
                        case XiaomiFastbootRomStepKind.WipeUserData:
                            if (!_confirmWipe.IsChecked.GetValueOrDefault())
                                throw new UnauthorizedAccessException("تم منع مسح بيانات المستخدم دون موافقة مستقلة.");
                            var wipeOutput = await _fastboot.WipeUserDataAsync(_serial);
                            _details.AppendText(Environment.NewLine + wipeOutput);
                            break;
                        case XiaomiFastbootRomStepKind.RebootFastbootd:
                            var rebootOutput = await _fastboot.RebootFastbootdAsync(_serial);
                            _details.AppendText(Environment.NewLine + rebootOutput);
                            await WaitForSelectedFastbootDeviceAsync(TimeSpan.FromSeconds(60));
                            break;
                        default:
                            throw new InvalidDataException("خطة ROM تحتوي خطوة غير مدعومة.");
                    }

                    _details.AppendText(Environment.NewLine + "RESULT: COMMAND EXITED SUCCESSFULLY");
                }

                var finalState = await ReadSelectedDeviceStateAsync();
                if (!string.Equals(finalState.Product, freshState.Product, StringComparison.Ordinal) ||
                    finalState.LockState != BootloaderLockState.Unlocked)
                    throw new UnauthorizedAccessException("فشل التحقق النهائي من هوية الجهاز أو حالة Bootloader. لم يتم إرسال reboot system.");

                var finalOutput = await _fastboot.RebootSystemAsync(_serial);
                _details.AppendText(Environment.NewLine + "FINAL REBOOT:" + Environment.NewLine + finalOutput);
                _details.AppendText(Environment.NewLine + "ALL PLANNED FASTBOOT COMMANDS COMPLETED.");
                _status.Text = "انتهت جميع أوامر الخطة دون إرجاع خطأ. انتظر أول إقلاع وتحقق من النظام يدويًا؛ لم يتم إجراء اختبار إقلاع فعلي.";
            }
            catch (Exception ex)
            {
                _details.AppendText(Environment.NewLine + Environment.NewLine +
                    "FATAL / PARTIAL FLASH STOP" + Environment.NewLine + ex);
                _status.Text = "توقفت العملية بعد فشل خطوة. قد يكون الهاتف في حالة تفليش جزئي؛ لا تفصل الكابل أو تغلق البرنامج. راجع آخر أمر واحتفظ بالهاتف في وضع Fastboot.";
                MessageBox.Show(this,
                    "توقفت العملية عند أول خطأ ولم تُرسل الخطوات التالية أو إعادة التشغيل النهائي.\n\n" +
                    ex.Message + "\n\nاحتفظ بالهاتف موصلًا وفي وضع Fastboot، ولا تعِد التشغيل عشوائيًا.",
                    "Xiaomi ROM — توقف مع احتمال تفليش جزئي", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _status.Text = "تم منع تنفيذ ROM: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Xiaomi Full ROM Preflight", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _running = false;
            _browseButton.IsEnabled = true;
            _inspectButton.IsEnabled = true;
            _flashButton.IsEnabled = _plan?.CanExecute == true &&
                _lastDeviceState?.LockState == BootloaderLockState.Unlocked;
        }
    }

    private async Task<FastbootStateReport> ReadSelectedDeviceStateAsync()
    {
        var devices = await _fastboot.DiscoverAsync();
        var selected = devices.SingleOrDefault(item =>
            item.Serial.Equals(_serial, StringComparison.Ordinal));
        if (selected is null)
            throw new UnauthorizedAccessException("الهاتف المحدد لم يظهر في fastboot devices؛ تم منع أي عملية على جهاز آخر.");

        var raw = await _fastboot.ReadAllVariablesAsync(_serial);
        var state = FastbootVariableParser.Parse(raw);
        if (string.IsNullOrWhiteSpace(state.Product))
            throw new InvalidDataException("Fastboot لم يرجع Product صريحًا؛ لا يمكن التحقق من توافق ROM.");
        if (state.LockState == BootloaderLockState.Conflicting)
            throw new InvalidDataException("قراءات حالة Bootloader متعارضة.");
        return state;
    }

    private async Task WaitForSelectedFastbootDeviceAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var devices = await _fastboot.DiscoverAsync();
                if (devices.Any(item => item.Serial.Equals(_serial, StringComparison.Ordinal)))
                    return;
            }
            catch (Exception ex) { lastError = ex; }
            await Task.Delay(1000);
        }
        throw new TimeoutException("لم يظهر نفس Serial مرة أخرى بعد الانتقال إلى Fastbootd." +
            (lastError is null ? "" : " آخر خطأ: " + lastError.Message));
    }

    private static (bool Blocked, bool RequiresManualAcknowledgement, string Message) EvaluateAntiRollback(
        XiaomiFastbootRomFlashPlan plan,
        FastbootStateReport state)
    {
        var deviceIndex = ReadDeviceAntiRollback(state);
        var romIndex = plan.FirmwareAntiRollbackIndex;
        if (deviceIndex.HasValue && romIndex.HasValue)
        {
            if (romIndex.Value < deviceIndex.Value)
                return (true, false, $"ROM anti_version {romIndex.Value} أقل من قيمة الجهاز {deviceIndex.Value}.");
            return (false, false, $"ROM anti_version {romIndex.Value} ليس أقل من قيمة الجهاز {deviceIndex.Value}.");
        }
        return (false, true, "لم تتوفر قيمتا Anti-Rollback قابلتان للمقارنة.");
    }

    private static int? ReadDeviceAntiRollback(FastbootStateReport state)
    {
        var candidates = state.RollbackVariables
            .Where(pair => pair.Key.Equals("anti", StringComparison.OrdinalIgnoreCase) ||
                           pair.Key.Equals("anti-rollback", StringComparison.OrdinalIgnoreCase) ||
                           pair.Key.Equals("antirollback", StringComparison.OrdinalIgnoreCase) ||
                           pair.Key.Equals("anti-version", StringComparison.OrdinalIgnoreCase))
            .Select(pair => ParseIndex(pair.Value))
            .ToArray();

        if (candidates.Length == 0 || candidates.Any(value => !value.HasValue))
            return null;
        var distinct = candidates.Select(value => value!.Value).Distinct().ToArray();
        return distinct.Length == 1 ? distinct[0] : null;
    }

    private static int? ParseIndex(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var hex) ? hex : null;
        return int.TryParse(text, out var number) && number >= 0 ? number : null;
    }

    private static bool PlansMatch(XiaomiFastbootRomFlashPlan first, XiaomiFastbootRomFlashPlan second)
        => first.ScriptName.Equals(second.ScriptName, StringComparison.OrdinalIgnoreCase) &&
           first.FirmwareAntiRollbackIndex == second.FirmwareAntiRollbackIndex &&
           first.ProductMatch == second.ProductMatch &&
           string.Equals(first.SelectedDeviceProduct, second.SelectedDeviceProduct, StringComparison.Ordinal) &&
           first.RequiredProducts.SequenceEqual(second.RequiredProducts, StringComparer.OrdinalIgnoreCase) &&
           first.Steps.SequenceEqual(second.Steps);

    private static void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (sender is XiaomiFastbootRomFlashWindow window && window._running)
        {
            e.Cancel = true;
            MessageBox.Show(window,
                "لا يمكن إغلاق هذه النافذة أثناء تنفيذ كتابة Fastboot. انتظر نتيجة الأمر الحالي حتى لا تترك العملية في حالة غير واضحة.",
                "Xiaomi Fastboot Flash", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string FormatStep(XiaomiFastbootRomFlashStep step)
        => step.Kind == XiaomiFastbootRomStepKind.FlashPartition
            ? $"FLASH {step.Partition} — {Path.GetFileName(step.ImagePath)} — SHA-256 {step.Sha256}"
            : step.Kind == XiaomiFastbootRomStepKind.WipeUserData
                ? "WIPE USERDATA"
                : "REBOOT FASTBOOTD";

    private static string Mask(string serial) => serial.Length > 4 ? "••••" + serial[^4..] : "••••";

    private static Border Panel(UIElement child)
        => new()
        {
            Child = child,
            Background = Brush("#0D1C2E"),
            BorderBrush = Brush("#263D58"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8)
        };

    private static TextBlock TextBlock(string text, double size, string color)
        => new() { Text = text, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap };

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 7, 0),
            MinWidth = 135,
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
}
