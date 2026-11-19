using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Desktop;

public partial class PhoneTestWindow : Window
{
    private readonly Func<string, Task> _sendCommand;
    private readonly Func<Task<AgentSnapshot?>> _readSnapshot;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _refreshing;
    public ObservableCollection<string> ResultLines { get; } = [];

    public PhoneTestWindow(string deviceName, Func<string, Task> sendCommand, Func<Task<AgentSnapshot?>> readSnapshot)
    {
        InitializeComponent();
        _sendCommand = sendCommand;
        _readSnapshot = readSnapshot;
        DeviceTitle.Text = deviceName;
        ResultsList.ItemsSource = ResultLines;
        _timer.Tick += async (_, _) => await RefreshResultsAsync();
        Loaded += async (_, _) => { await RefreshResultsAsync(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    private async void RunTest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string command }) return;
        try
        {
            SyncStatus.Text = "جارٍ إرسال أمر الاختبار إلى الهاتف...";
            await _sendCommand(command);
            SyncStatus.Text = "تم قبول الأمر. أكمل تعليمات الاختبار التي تظهر على الهاتف.";
            await RefreshResultsAsync();
        }
        catch (Exception ex)
        {
            SyncStatus.Text = "تعذر بدء الاختبار: " + ex.Message;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshResultsAsync();

    private async Task RefreshResultsAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var snapshot = await _readSnapshot();
            if (snapshot is null)
            {
                SyncStatus.Text = "Agent غير متصل. افتح PhoneLab Agent على الهاتف واتركه في المقدمة.";
                return;
            }

            ResultLines.Clear();
            foreach (var item in snapshot.Tests.OrderBy(x => x.Key))
            {
                var status = item.Value.Status switch
                {
                    "passed" => "ناجح",
                    "failed" => "فشل",
                    "review" => "مراجعة",
                    "data_read" => "بيانات مقروءة",
                    "not_tested" => "لم يُختبر",
                    _ => item.Value.Status
                };
                ResultLines.Add($"{status}  |  {item.Key}\n{item.Value.Evidence}");
            }

            SyncStatus.Text = $"Agent {snapshot.AgentVersion} • {snapshot.Tests.Count} نتيجة • آخر مزامنة {DateTime.Now:T}";
        }
        catch (Exception ex) { SyncStatus.Text = "تعذرت المزامنة: " + ex.Message; }
        finally { _refreshing = false; }
    }
}
