using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

// Used by the Windows startup entry and the Start Menu service shortcut.
// This launcher runs outside all input-method callbacks.
internal static class WeaselWithPhrase
{
    [STAThread]
    private static void Main()
    {
        string install = AppDomain.CurrentDomain.BaseDirectory;
        // Failure of the optional phrase editor must not prevent Weasel startup.
        try
        {
            string root = null;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Rime\Weasel"))
            {
                if (key != null) root = key.GetValue("RimeUserDir") as string;
            }
            if (String.IsNullOrEmpty(root))
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rime");
            Start(Path.Combine(root, "extensions", "manual_phrase", "ManualPhrase.Manager.exe"));
        }
        catch (Exception ex) { Log(ex); }
        try { if (!WeaselRunning()) Start(Path.Combine(install, "WeaselServer.exe")); }
        catch (Exception ex) { Log(ex); }
    }

    private static bool WeaselRunning()
    {
        int session;
        using (Process current = Process.GetCurrentProcess()) session = current.SessionId;
        bool running = false;
        foreach (Process process in Process.GetProcessesByName("WeaselServer"))
        {
            using (process)
            {
                try { if (process.SessionId == session) running = true; }
                catch { }
            }
        }
        return running;
    }
    private static void Start(string executable)
    {
        if (!File.Exists(executable)) return;
        var info = new ProcessStartInfo(executable);
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.WindowStyle = ProcessWindowStyle.Hidden;
        info.WorkingDirectory = Path.GetDirectoryName(executable);
        using (Process process = Process.Start(info)) { }
        // Never wait for either long-running process.
    }

    private static void Log(Exception error)
    {
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rime");
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "linked-startup-error.log");
            if (File.Exists(file) && new FileInfo(file).Length > 65536) File.Delete(file);
            File.AppendAllText(file, DateTime.Now.ToString("s") + " " + error.ToString() + Environment.NewLine);
        }
        catch { }
    }
}

