using System.IO.Compression;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace CamtranslatorXpraim.Setup;
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var install = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CamtranslatorXpraim");
        if (args.Contains("--verify-payload"))
        {
            using var zip = new ZipArchive(Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")!);
            var ok = zip.Entries.Any(e => e.FullName == "CamtranslatorXpraim.exe") && zip.Entries.Any(e => e.FullName == "web/receiver.js");
            Environment.Exit(ok ? 0 : 1);return;
        }
        var form = new Form { Text = "Установка CamtranslatorXpraim", Width = 600, Height = 310, StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false };
        var label = new Label { Text = "CamtranslatorXpraim · бесплатно\n\nУстановка для текущего пользователя:\n" + install + "\n\nOBS Studio и WebView2 Runtime устанавливаются отдельно с официальных сайтов, если их ещё нет.", Left = 24, Top = 22, Width = 540, Height = 140 };
        var button = new Button { Text = "Установить", Left = 24, Top = 180, Width = 150, Height = 38 };
        button.Click += (_, _) =>
        {
            try
            {
                if (System.Diagnostics.Process.GetProcessesByName("CamtranslatorXpraim").Length > 0) throw new IOException("Закройте CamtranslatorXpraim перед установкой.");
                Directory.CreateDirectory(install);
                using var zip = new ZipArchive(Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")!);
                var files = new List<string>();
                foreach (var entry in zip.Entries)
                {
                    var path = Path.GetFullPath(Path.Combine(install, entry.FullName));
                    if (!path.StartsWith(install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Неверный путь в архиве");
                    if (entry.Name.Length == 0) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);entry.ExtractToFile(path, true);files.Add(entry.FullName);
                }
                File.WriteAllLines(Path.Combine(install, "installed-files.txt"), files);
                var exe = Path.Combine(install, "CamtranslatorXpraim.exe");
                var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);Directory.CreateDirectory(programs);
                var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows Script Host недоступен для создания ярлыка.");
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(Path.Combine(programs, "CamtranslatorXpraim.lnk"));
                shortcut.TargetPath = exe;shortcut.WorkingDirectory = install;shortcut.Description = "CamtranslatorXpraim — iPhone как веб-камера";shortcut.Save();
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\CamtranslatorXpraim");
                key.SetValue("DisplayName", "CamtranslatorXpraim");key.SetValue("DisplayVersion", "0.1.0");key.SetValue("InstallLocation", install);key.SetValue("UninstallString", $"powershell.exe -NoProfile -File \"{Path.Combine(install, "Uninstall.ps1")}\"");key.SetValue("NoModify", 1);key.SetValue("NoRepair", 1);
                label.Text = "Установка завершена.\n\nПриложение доступно в меню «Пуск».\n\nИнструкция: " + Path.Combine(install, "docs", "QUICKSTART.md");button.Enabled = false;
            }
            catch (Exception e) { MessageBox.Show(e.Message, "Ошибка установки", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        form.Controls.Add(label);form.Controls.Add(button);Application.Run(form);
    }
}
