using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>Authenticode signature inspection for OEM driver catalog/binary files; performs no installation.</summary>
public sealed class UsbDriverSignatureVerifierWindow : Window
{
    private readonly TextBox _path = new();
    private readonly TextBox _report = new();
    private readonly TextBlock _status = new();
    private readonly WindowsDriverSignatureVerificationService _service = new();

    public UsbDriverSignatureVerifierWindow()
    {
        Title = "SoftBox | USB Driver Signature Verification";
        Width = 850;
        Height = 620;
        MinWidth = 680;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "التحقق من توقيع تعريفات USB",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        root.Children.Add(Text(
            "يفحص توقيع Authenticode لدى Windows للملف المحدد فقط. اختر ملف CAT الخاص بحزمة الشركة للتحقق من توقيع الناشر، أو SYS/DLL/EXE للتحقق من توقيع مضمّن. لا يثبت SoftBox أي تعريف ولا يغير Driver Store.",
            "#B7C9DE"));
        root.Children.Add(Text("ملف التعريف أو Catalog", "#7DD3FC"));

        var row = new DockPanel { Margin = new Thickness(0, 5, 0, 10) };
        _path.IsReadOnly = true;
        _path.Padding = new Thickness(8);
        _path.Background = Brush("#0F1E32");
        _path.Foreground = Brush("#EAF3FF");
        _path.BorderBrush = Brush("#36516D");
        row.Children.Add(_path);
        var browse = Button("اختيار ملف", (_, _) => Browse());
        DockPanel.SetDock(browse, Dock.Left);
        row.Children.Add(browse);
        root.Children.Add(row);

        var verify = Button("التحقق من توقيع Windows", (_, _) => Verify());
        verify.HorizontalAlignment = HorizontalAlignment.Left;
        verify.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(verify);

        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
        root.Children.Add(_status);

        _report.IsReadOnly = true;
        _report.AcceptsReturn = true;
        _report.TextWrapping = TextWrapping.Wrap;
        _report.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _report.MinHeight = 280;
        _report.Margin = new Thickness(0, 10, 0, 0);
        _report.Padding = new Thickness(10);
        _report.Background = Brush("#071625");
        _report.Foreground = Brush("#DCEBFA");
        _report.BorderBrush = Brush("#263D58");
        root.Children.Add(_report);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void Browse()
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختيار ملف تعريف أو كتالوج توقيع OEM",
            Filter = "Driver signature files (*.cat;*.sys;*.dll;*.exe)|*.cat;*.sys;*.dll;*.exe|Catalog files (*.cat)|*.cat|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            _path.Text = dialog.FileName;
            _report.Clear();
            _status.Text = "تم اختيار الملف. لم يبدأ التحقق بعد.";
        }
    }

    private void Verify()
    {
        try
        {
            var result = _service.Verify(_path.Text);
            _status.Text = result.IsTrusted
                ? "نجح تحقق توقيع Windows للملف المحدد."
                : "لم يقبل Windows توقيع الملف. لا تستخدمه كتعريف موثوق.";
            _status.Foreground = Brush(result.IsTrusted ? "#7DDBAA" : "#FFB4A6");
            _report.Text =
                "File: " + result.FilePath + Environment.NewLine +
                "Signature type: " + result.SignatureType + Environment.NewLine +
                "Trusted by Windows: " + result.IsTrusted + Environment.NewLine +
                "Trust status: 0x" + unchecked((uint)result.TrustStatus).ToString("X8") + Environment.NewLine +
                "Status: " + result.TrustStatusText + Environment.NewLine +
                "Signer: " + (string.IsNullOrWhiteSpace(result.SignerSubject) ? "غير متاح" : result.SignerSubject) + Environment.NewLine +
                "Thumbprint: " + (string.IsNullOrWhiteSpace(result.SignerThumbprint) ? "غير متاح" : result.SignerThumbprint) + Environment.NewLine +
                "Certificate valid from: " + (result.CertificateNotBefore?.ToString("u") ?? "غير متاح") + Environment.NewLine +
                "Certificate valid until: " + (result.CertificateNotAfter?.ToString("u") ?? "غير متاح") + Environment.NewLine +
                Environment.NewLine + "حدود الفحص: " + result.Limitation;
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر التحقق: " + ex.Message;
            _status.Foreground = Brush("#FFB4A6");
            _report.Clear();
        }
    }

    private static Button Button(string label, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 7, 12, 7),
            Background = Brush("#14517A"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096"),
            Margin = new Thickness(0, 0, 8, 0)
        };
        button.Click += handler;
        return button;
    }

    private static TextBlock Text(string value, string color)
        => new()
        {
            Text = value,
            Foreground = Brush(color),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 5)
        };

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color)!);
}
