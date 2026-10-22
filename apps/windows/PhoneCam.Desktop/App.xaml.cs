using System.IO;
using System.Windows;

namespace CamtranslatorXpraim.Desktop;
public partial class App : System.Windows.Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, @"Local\CamtranslatorXpraim.Desktop", out var created);
        if (!created) { System.Windows.MessageBox.Show("CamtranslatorXpraim уже запущен. Откройте его через значок в системном трее.", "CamtranslatorXpraim"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => { System.Windows.MessageBox.Show(args.Exception.Message, "CamtranslatorXpraim — ошибка"); args.Handled = true; };
        MainWindow = new MainWindow(); MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e) { if (instance is not null) { try { instance.ReleaseMutex(); } catch (ApplicationException) { } instance.Dispose(); } base.OnExit(e); }
}
