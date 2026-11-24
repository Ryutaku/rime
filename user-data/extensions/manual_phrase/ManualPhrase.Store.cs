using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

internal sealed class ManualPhraseEntry
{
    internal string Text;
    internal string Code;
    internal string Weight;
}

// File operations are independent of the UI and never access Rime's active DB.
internal static class ManualPhraseStore
{
    // The app and the Chinese/English state the last request came from. A
    // redeployment rebuilds every input session and terminal apps come back in
    // English, so this is handed to the restore request the Lua side reads.
    internal static string OriginApp;
    internal static bool OriginAscii;

    // One request per deployment; consumed and removed by number_input.lua.
    internal static void WriteStateRestore(string root)
    {
        string app = OriginApp;
        OriginApp = null;
        if (String.IsNullOrEmpty(app)) return;
        string directory = Path.Combine(root, "extensions", "manual_phrase");
        string ready = Path.Combine(directory, "restore-ascii.ready");
        string pending = Path.Combine(directory, "restore-ascii.pending");
        long stamp = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        string body = "RIME_RESTORE_V1\t" + app + "\t" + (OriginAscii ? "1" : "0") + "\t" + stamp;
        Directory.CreateDirectory(directory);
        File.WriteAllText(pending, body, new UTF8Encoding(false));
        if (File.Exists(ready)) File.Delete(ready);
        File.Move(pending, ready);
    }

    internal static ManualPhraseEntry Parse(string line)
    {
        if (String.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) return null;
        string[] fields = line.Split('\t');
        if (fields.Length < 2 || fields[0].Length == 0 || fields[1].Length == 0) return null;
        return new ManualPhraseEntry { Text = fields[0], Code = fields[1], Weight = fields.Length > 2 ? fields[2] : "100" };
    }

    internal static List<ManualPhraseEntry> Read(string root)
    {
        var result = new List<ManualPhraseEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string file = Path.Combine(root, "custom_phrase.txt");
        if (!File.Exists(file)) return result;
        foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
        {
            ManualPhraseEntry entry = Parse(line);
            if (entry != null && seen.Add(entry.Text + "\t" + entry.Code)) result.Add(entry);
        }
        return result;
    }

    internal static bool Delete(string root, ManualPhraseEntry selected)
    {
        string file = Path.Combine(root, "custom_phrase.txt");
        if (!File.Exists(file)) return false;
        var remaining = new List<string>();
        bool removed = false;
        // Re-read at the moment of deletion, preserving words added since listing.
        foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
        {
            ManualPhraseEntry entry = Parse(line);
            if (entry != null && entry.Text == selected.Text && entry.Code == selected.Code) removed = true;
            else remaining.Add(line);
        }
        if (removed) Write(root, remaining);
        return removed;
    }

    internal static bool Restore(string root, ManualPhraseEntry selected)
    {
        string file = Path.Combine(root, "custom_phrase.txt");
        var lines = File.Exists(file) ? new List<string>(File.ReadAllLines(file, Encoding.UTF8)) :
            new List<string>(new string[] { "# Rime table", "# coding: utf-8" });
        foreach (string line in lines)
        {
            ManualPhraseEntry entry = Parse(line);
            if (entry != null && entry.Text == selected.Text && entry.Code == selected.Code) return false;
        }
        lines.Add(selected.Text + "\t" + selected.Code + "\t" + selected.Weight);
        Write(root, lines);
        return true;
    }

    private static void Write(string root, List<string> lines)
    {
        string file = Path.Combine(root, "custom_phrase.txt");
        string temporary = Path.Combine(root, ".manual_phrase-" + Guid.NewGuid().ToString("N") + ".tmp");
        string backups = Path.Combine(root, "backups", "manual_phrase");
        Directory.CreateDirectory(backups);
        string backup = Path.Combine(backups, "custom_phrase-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllLines(temporary, lines.ToArray(), new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(temporary, file, backup);
            else File.Move(temporary, file);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
