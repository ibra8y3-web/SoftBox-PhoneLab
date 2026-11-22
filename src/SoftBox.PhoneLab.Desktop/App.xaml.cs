using SoftBox.PhoneLab.Core.Security;
using SoftBox.PhoneLab.Desktop.Services;
using System.Windows;

namespace SoftBox.PhoneLab.Desktop;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        SoftBoxLocalization.Install();
        SoftBoxDesktopTelemetry.Initialize(this);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Any(argument => argument.Equals("--run-android-backups", StringComparison.OrdinalIgnoreCase)))
        {
            var workerExitCode = await AndroidBackupBackgroundWorker.RunAsync();
            Shutdown(workerExitCode);
            return;
        }

        try
        {
            var accounts = new SoftBoxAccountService();
            var initialSetup = !await accounts.HasAccountsAsync();
            var authentication = new SoftBoxAuthenticationWindow(accounts, initialSetup);
            if (authentication.ShowDialog() != true || authentication.AuthenticatedUser is null)
            {
                Shutdown();
                return;
            }

            var mainWindow = new MainWindow(accounts, authentication.AuthenticatedUser);
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "تعذر فتح قاعدة حسابات SoftBox الآمنة. لم يتم تجاوز المصادقة أو إعادة تعيين الحسابات.\n\n" + ex.Message,
                "SoftBox • خطأ في المصادقة",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
