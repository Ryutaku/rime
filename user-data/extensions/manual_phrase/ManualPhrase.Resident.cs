using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Microsoft.Win32;

// This process is started at sign-in, never from the IME key callback.
internal static class ManualPhraseProgram
{
    [STAThread]
    private static void Main()
    {
        bool created;
        using (var mutex = new Mutex(true, "Local\\RimeManualPhrase_Resident_Administrator", out created))
        {
            if (!created) return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            { Log(e.Exception); };
            try
            {
                string root = Path.GetFullPath(Path.Combine(Application.StartupPath, "..", ".."));
                using (var context = new PhraseApplicationContext(root)) Application.Run(context);
            }
            catch (Exception ex) { Log(ex); }
        }
    }

    internal static void Log(Exception ex)
    {
        try
        {
            string path = Path.Combine(Application.StartupPath, "resident-error.log");
            if (File.Exists(path) && new FileInfo(path).Length > 65536) File.Delete(path);
            File.AppendAllText(path, DateTime.Now.ToString("s") + " " + ex.ToString() + Environment.NewLine);
        }
        catch { }
    }
}

internal static class PhraseNative
{
    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]
    internal static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr window);

    internal static bool ShortcutHeld()
    {
        return (GetAsyncKeyState(0x11) & 0x8000) != 0 ||
               (GetAsyncKeyState(0x10) & 0x8000) != 0 ||
               (GetAsyncKeyState(0xbb) & 0x8000) != 0;
    }
}

internal sealed class PhraseApplicationContext : ApplicationContext
{
    private readonly ManualPhraseForm form;
    private readonly FileSystemWatcher watcher;
    private readonly System.Windows.Forms.Timer timer;
    private readonly string request;
    private int requestAvailable = 1;
    private string pending;
    private DateTime releaseDeadline;
    private DateTime nextScan = DateTime.MinValue;

    internal PhraseApplicationContext(string root)
    {
        request = Path.Combine(Application.StartupPath, "request.ready");
        watcher = new FileSystemWatcher(Application.StartupPath, "request.ready");
        watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
        watcher.Created += delegate { Interlocked.Exchange(ref requestAvailable, 1); };
        watcher.Changed += delegate { Interlocked.Exchange(ref requestAvailable, 1); };
        watcher.Renamed += delegate { Interlocked.Exchange(ref requestAvailable, 1); };
        watcher.Error += delegate { Interlocked.Exchange(ref requestAvailable, 1); };
        watcher.EnableRaisingEvents = true;
        // Load the code table and create controls once, before any shortcut request.
        form = new ManualPhraseForm(root);
        IntPtr warmHandle = form.Handle;
        timer = new System.Windows.Forms.Timer();
        timer.Interval = 30;
        timer.Tick += OnTick;
        timer.Start();
    }

    private void OnTick(object sender, EventArgs e)
    {
        try
        {
            DateTime now = DateTime.UtcNow;
            bool signaled = Interlocked.Exchange(ref requestAvailable, 0) != 0;
            // The infrequent fallback covers a missed filesystem notification.
            if (signaled || now >= nextScan)
            {
                nextScan = now.AddSeconds(2);
                if (File.Exists(request))
                {
                    DateTime written = File.GetLastWriteTimeUtc(request);
                    string history = File.ReadAllText(request, Encoding.UTF8);
                    File.Delete(request);
                    if (now - written < TimeSpan.FromMinutes(1))
                    {
                        pending = history;
                        releaseDeadline = now.AddMilliseconds(750);
                    }
                }
            }
            if (pending != null && (!PhraseNative.ShortcutHeld() || now >= releaseDeadline))
            {
                string history = pending;
                pending = null;
                form.OpenRequest(history);
            }
        }
        catch (IOException) { Interlocked.Exchange(ref requestAvailable, 1); }
        catch (Exception ex) { ManualPhraseProgram.Log(ex); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            watcher.Dispose();
            form.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class ManualPhraseForm : Form
{
    private readonly string root;
    private readonly TextBox phrase = new TextBox();
    private readonly TextBox code = new TextBox();
    private readonly Label status = new Label();
    private readonly Button save = new Button();
    private readonly Button cancel = new Button();
    private readonly Button suggest = new Button();
    private readonly Dictionary<string, string> stems = new Dictionary<string, string>();
    private readonly Dictionary<string, string> fullCodes = new Dictionary<string, string>();
    private bool changingCode;
    private bool codeEdited;
    private bool saving;
    private readonly List<string> recentCharacters = new List<string>();
    private int recentCount;
    private bool selectingHistory;
    private bool historyMode;
    private readonly System.Windows.Forms.Timer frontTimer = new System.Windows.Forms.Timer();

    public ManualPhraseForm(string userRoot)
    {
        root = userRoot;
        Text = "小狼毫手动造词";
        Font = new Font("Microsoft YaHei UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(540, 244);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        KeyPreview = true;
        KeyDown += delegate(object sender, KeyEventArgs e)
        {
            if (HandleHistoryKey(e.KeyData))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        AddLabel("词语", 20, 21, 56, 26);
        phrase.SetBounds(80, 17, 440, 28);
        phrase.MaxLength = 256;
        phrase.ImeMode = ImeMode.On;
        phrase.TabIndex = 0;
        Controls.Add(phrase);

        AddLabel("编码", 20, 67, 56, 26);
        code.SetBounds(80, 63, 318, 28);
        code.MaxLength = 32;
        code.ImeMode = ImeMode.Disable;
        code.CharacterCasing = CharacterCasing.Lower;
        code.TabIndex = 1;
        Controls.Add(code);

        suggest.Text = "五笔取码";
        suggest.SetBounds(410, 61, 110, 30);
        suggest.TabIndex = 2;
        suggest.Click += delegate { codeEdited = false; SuggestCode(); };
        Controls.Add(suggest);
        AddLabel("↑ 多取一个字　↓ 少取一个字　Enter 保存　Esc 取消", 20, 104, 500, 24);

        status.SetBounds(20, 137, 500, 40);
        status.ForeColor = Color.DimGray;
        Controls.Add(status);
        save.Text = "保存";
        save.SetBounds(310, 193, 100, 32);
        save.TabIndex = 3;
        save.Click += SaveClicked;
        Controls.Add(save);
        cancel.Text = "取消";
        cancel.SetBounds(420, 193, 100, 32);
        cancel.TabIndex = 4;
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);
        AcceptButton = save;
        CancelButton = cancel;
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if (saving) e.Cancel = true; };
        phrase.TextChanged += delegate
        {
            if (!selectingHistory) historyMode = false;
            if (!codeEdited) SuggestCode();
        };
        code.TextChanged += delegate { if (!changingCode) codeEdited = true; };

        LoadCodes();
        // Closing a modeless window hides it; keep its controls and dictionary warm.
        cancel.Click += delegate { if (!saving) Hide(); };
        FormClosing += delegate(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                if (!saving) Hide();
            }
        };
        Deactivate += delegate { TopMost = false; };
        frontTimer.Interval = 750;
        frontTimer.Tick += delegate { frontTimer.Stop(); TopMost = false; };
        Disposed += delegate { frontTimer.Dispose(); };
    }

    internal void OpenRequest(string history)
    {
        if (!saving)
        {
            recentCharacters.Clear();
            recentCount = 0;
            historyMode = false;
            selectingHistory = true;
            changingCode = true;
            codeEdited = false;
            phrase.Text = "";
            code.Text = "";
            changingCode = false;
            selectingHistory = false;
            LoadRecentCharacters(history);
            if (recentCharacters.Count > 0)
            {
                recentCount = Math.Min(2, recentCharacters.Count);
                SelectRecentPhrase();
            }
            else status.Text = "当前会话没有可用的最近中文，请填写词语。";
            save.Enabled = cancel.Enabled = phrase.Enabled = code.Enabled = suggest.Enabled = true;
            DialogResult = DialogResult.None;
        }
        // Restore explicitly; request comes after the IME callback and key release.
        // Do not attach input queues, simulate keys, or synchronously message the client.
        WindowState = FormWindowState.Normal;
        TopMost = true;
        if (!Visible) Show();
        PhraseNative.ShowWindowAsync(Handle, 9); // SW_RESTORE
        BringToFront();
        PhraseNative.SetForegroundWindow(Handle);
        Activate();
        if (!saving)
        {
            if (phrase.Text.Length > 0 && code.Text.Length > 0) save.Focus();
            else { phrase.Focus(); phrase.SelectAll(); }
        }
        frontTimer.Stop();
        frontTimer.Start();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Escape) { if (!saving) Hide(); return true; }
        if (HandleHistoryKey(keyData)) return true;
        return base.ProcessCmdKey(ref message, keyData);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (HandleHistoryKey(keyData)) return true;
        return base.ProcessDialogKey(keyData);
    }

    private bool HandleHistoryKey(Keys keyData)
    {
        if (saving || !historyMode || recentCharacters.Count == 0) return false;
        if (keyData == Keys.Up)
        {
            recentCount = Math.Min(recentCount + 1, recentCharacters.Count);
            SelectRecentPhrase();
            return true;
        }
        if (keyData == Keys.Down)
        {
            recentCount = Math.Max(1, recentCount - 1);
            SelectRecentPhrase();
            return true;
        }
        return false;
    }

    private static bool IsChineseElement(string element)
    {
        int value = Char.ConvertToUtf32(element, 0);
        return (value >= 0x3400 && value <= 0x4dbf) ||
               (value >= 0x4e00 && value <= 0x9fff) ||
               (value >= 0xf900 && value <= 0xfaff) ||
               (value >= 0x20000 && value <= 0x323af);
    }

    private static bool IsTrailingBoundary(string element)
    {
        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(element, 0);
        return category == UnicodeCategory.SpaceSeparator ||
               category == UnicodeCategory.LineSeparator ||
               category == UnicodeCategory.ParagraphSeparator ||
               category == UnicodeCategory.Control ||
               category == UnicodeCategory.ConnectorPunctuation ||
               category == UnicodeCategory.DashPunctuation ||
               category == UnicodeCategory.OpenPunctuation ||
               category == UnicodeCategory.ClosePunctuation ||
               category == UnicodeCategory.InitialQuotePunctuation ||
               category == UnicodeCategory.FinalQuotePunctuation ||
               category == UnicodeCategory.OtherPunctuation;
    }

    private void LoadRecentCharacters(string history)
    {
        var elements = new List<string>();
        TextElementEnumerator iterator = StringInfo.GetTextElementEnumerator(history);
        while (iterator.MoveNext()) elements.Add((string)iterator.Current);
        int end = elements.Count - 1;
        while (end >= 0 && IsTrailingBoundary(elements[end])) end--;
        int start = end;
        while (start >= 0 && IsChineseElement(elements[start]) && end - start < 128) start--;
        for (int i = start + 1; i <= end; i++) recentCharacters.Add(elements[i]);
    }

    private void SelectRecentPhrase()
    {
        var text = new StringBuilder();
        int start = Math.Max(0, recentCharacters.Count - recentCount);
        for (int i = start; i < recentCharacters.Count; i++) text.Append(recentCharacters[i]);
        selectingHistory = true;
        changingCode = true;
        codeEdited = false;
        try { phrase.Text = text.ToString(); }
        finally { selectingHistory = false; changingCode = false; }
        historyMode = true;
        SuggestCode();
        if (code.Text.Length > 0)
            status.Text = "最近 " + recentCount + " 个字，按回车即可保存；↑ 向前扩展，↓ 缩短。";
    }

    private void AddLabel(string text, int x, int y, int width, int height)
    {
        var label = new Label();
        label.Text = text;
        label.SetBounds(x, y, width, height);
        Controls.Add(label);
    }

    private static bool IsWubiCode(string value)
    {
        return Regex.IsMatch(value, "^[a-y]{1,4}$");
    }

    private void LoadCodes()
    {
        string dictionary = Path.Combine(root, "wubi86.dict.yaml");
        string cache = Path.Combine(Application.StartupPath, "wubi-single-char.tsv");
        if (File.Exists(cache) && (!File.Exists(dictionary) ||
            File.GetLastWriteTimeUtc(cache) >= File.GetLastWriteTimeUtc(dictionary)))
        {
            foreach (string line in File.ReadLines(cache, Encoding.UTF8))
            {
                string[] fields = line.Split('\t');
                if (fields.Length != 3) continue;
                fullCodes[fields[0]] = fields[1];
                if (fields[2].Length > 0) stems[fields[0]] = fields[2];
            }
            return;
        }
        if (!File.Exists(dictionary)) return;
        bool data = false;
        foreach (string line in File.ReadLines(dictionary, Encoding.UTF8))
        {
            if (!data) { if (line.Trim() == "...") data = true; continue; }
            if (line.Length == 0 || line[0] == '#') continue;
            string[] fields = line.Split('\t');
            if (fields.Length < 2 || fields[0].Length != 1) continue;
            string character = fields[0];
            string entryCode = fields[1].Trim();
            if (!IsWubiCode(entryCode)) continue;
            string previous;
            if (!fullCodes.TryGetValue(character, out previous) || entryCode.Length > previous.Length)
                fullCodes[character] = entryCode;
            if (fields.Length > 3 && IsWubiCode(fields[3].Trim()))
                stems[character] = fields[3].Trim();
        }
    }

    private string GetStem(string character)
    {
        string value;
        if (stems.TryGetValue(character, out value)) return value;
        if (fullCodes.TryGetValue(character, out value)) return value;
        return null;
    }

    private static string First(string value, int count)
    {
        if (value == null || value.Length < count) return null;
        return value.Substring(0, count);
    }

    private string GetSuggestedCode(string text)
    {
        var chars = new List<string>();
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext()) chars.Add((string)elements.Current);
        if (chars.Count == 0) return "";
        string value;
        if (chars.Count == 1)
            return fullCodes.TryGetValue(chars[0], out value) ? value : null;
        string a = First(GetStem(chars[0]), chars.Count == 2 ? 2 : 1);
        string b = First(GetStem(chars[1]), chars.Count == 2 ? 2 : 1);
        if (a == null || b == null) return null;
        if (chars.Count == 2) return a + b;
        string c = First(GetStem(chars[2]), chars.Count == 3 ? 2 : 1);
        if (c == null) return null;
        if (chars.Count == 3) return a + b + c;
        string z = First(GetStem(chars[chars.Count - 1]), 1);
        return z == null ? null : a + b + c + z;
    }

    private void SuggestCode()
    {
        string value = GetSuggestedCode(phrase.Text.Trim());
        changingCode = true;
        try { code.Text = value ?? ""; }
        finally { changingCode = false; }
        status.Text = value == null
            ? "这段文字无法自动取码，请填写自定义字母编码。"
            : "可使用生成的五笔编码，也可改成自己方便记忆的字母。";
    }

    private void WritePhrase(string text, string entryCode)
    {
        string target = Path.Combine(root, "custom_phrase.txt");
        var result = new List<string>();
        bool found = false;
        if (File.Exists(target))
        {
            foreach (string line in File.ReadAllLines(target, Encoding.UTF8))
            {
                string[] fields = line.Split('\t');
                if (!line.StartsWith("#") && fields.Length >= 2 &&
                    fields[0] == text && fields[1] == entryCode)
                {
                    if (!found) result.Add(text + "\t" + entryCode + "\t100");
                    found = true;
                }
                else result.Add(line);
            }
        }
        else
        {
            result.Add("# Rime table");
            result.Add("# coding: utf-8");
        }
        if (!found) result.Add(text + "\t" + entryCode + "\t100");
        string backups = Path.Combine(root, "backups", "manual_phrase");
        Directory.CreateDirectory(backups);
        if (File.Exists(target))
            File.Copy(target, Path.Combine(backups,
                "custom_phrase-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".txt"), false);
        string temp = Path.Combine(root, ".manual_phrase-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllLines(temp, result.ToArray(), new UTF8Encoding(false));
        if (File.Exists(target)) File.Replace(temp, target, null);
        else File.Move(temp, target);
    }

    private static string FindDeployer()
    {
        foreach (RegistryView view in new RegistryView[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
            using (RegistryKey key = machine.OpenSubKey(@"Software\Rime\Weasel"))
            {
                if (key == null) continue;
                string install = key.GetValue("WeaselRoot") as string;
                if (String.IsNullOrEmpty(install)) continue;
                string exe = Path.Combine(install, "WeaselDeployer.exe");
                if (File.Exists(exe)) return exe;
            }
        }
        return null;
    }

    private static string Deploy()
    {
        try
        {
            string exe = FindDeployer();
            if (exe == null) return "未找到部署程序，请右键小狼毫选择“重新部署”。";
            var start = new ProcessStartInfo(exe, "/deploy");
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WindowStyle = ProcessWindowStyle.Hidden;
            using (Process process = Process.Start(start))
            {
                if (process == null) return "请右键小狼毫选择“重新部署”。";
                if (!process.WaitForExit(30000)) return "重新部署仍在进行，请稍候再输入。";
                if (process.ExitCode != 0)
                    return "自动部署未完成。请先关闭其他小狼毫设置窗口，再右键选择“重新部署”。";
            }
            return "";
        }
        catch (Exception ex)
        {
            return "请右键小狼毫选择“重新部署”。\n" + ex.Message;
        }
    }

    private async void SaveClicked(object sender, EventArgs e)
    {
        if (saving) return;
        string text = phrase.Text.Trim();
        string entryCode = code.Text.Trim();
        if (text.Length == 0 || text.IndexOfAny(new char[] { '\t', '\r', '\n' }) >= 0)
        {
            MessageBox.Show(this, "请填写一行词语或短语，不要包含换行和 Tab。", Text);
            phrase.Focus(); return;
        }
        if (!Regex.IsMatch(entryCode, "^[a-z]{1,32}$"))
        {
            MessageBox.Show(this, "编码请使用小写英文字母，不含数字或符号。", Text);
            code.Focus(); return;
        }
        try { WritePhrase(text, entryCode); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存失败：\n" + ex.Message, Text,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        saving = true;
        save.Enabled = false;
        cancel.Enabled = false;
        phrase.Enabled = false;
        code.Enabled = false;
        suggest.Enabled = false;
        status.Text = "词条已保存，正在重新部署……";
        string deploymentMessage;
        try { deploymentMessage = await Task.Run(() => Deploy()); }
        catch (Exception ex) { deploymentMessage = "请手动重新部署。\n" + ex.Message; }
        saving = false;
        if (deploymentMessage.Length > 0)
            MessageBox.Show(this, "词条已保存。\n" + deploymentMessage,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.None;
        Hide();
    }
}
