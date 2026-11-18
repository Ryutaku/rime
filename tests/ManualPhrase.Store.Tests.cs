using System;
using System.IO;
using System.Text;

internal static class ManualPhraseStoreTests
{
    private static int checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }

    private static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "rime-delete-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "custom_phrase.txt");
        string original = "# Rime table\r\n# 保留注释\r\n词语\tabcd\t100\r\n同码\tabcd\t80\r\n词语\twxyz\t60\r\n词语\tabcd\t100\r\n𠀀词\tqrst\t40\r\n";
        File.WriteAllText(file, original, new UTF8Encoding(true));
        byte[] originalBytes = File.ReadAllBytes(file);
        Check(ManualPhraseStore.Read(root).Count == 4, "duplicate pairs should be listed once");
        var selected = new ManualPhraseEntry { Text = "词语", Code = "abcd", Weight = "100" };
        // A new word arrives after the selection list was read.
        File.AppendAllText(file, "后加词\tzzzz\t90\r\n", new UTF8Encoding(false));
        byte[] beforeDelete = File.ReadAllBytes(file);
        Check(ManualPhraseStore.Delete(root, selected), "selected exact pair should be deleted");
        string content = File.ReadAllText(file, Encoding.UTF8);
        Check(!content.Contains("词语\tabcd\t"), "all duplicate selected pairs should be removed");
        Check(content.Contains("同码\tabcd\t80"), "a different word with the same code must remain");
        Check(content.Contains("词语\twxyz\t60"), "the same word with a different code must remain");
        Check(content.Contains("后加词\tzzzz\t90"), "freshly added unrelated words must remain");
        Check(content.Contains("# 保留注释") && content.Contains("𠀀词"), "comments and Unicode must remain");
        string[] backups = Directory.GetFiles(Path.Combine(root, "backups", "manual_phrase"));
        Check(backups.Length == 1 && Convert.ToBase64String(File.ReadAllBytes(backups[0])) == Convert.ToBase64String(beforeDelete), "backup must preserve original bytes");
        byte[] afterDelete = File.ReadAllBytes(file);
        Check(!ManualPhraseStore.Delete(root, selected), "stale selections must not rewrite the file");
        Check(Convert.ToBase64String(afterDelete) == Convert.ToBase64String(File.ReadAllBytes(file)), "missing selection must leave bytes unchanged");
        Check(ManualPhraseStore.Restore(root, selected), "undo should restore the deleted pair");
        Check(!ManualPhraseStore.Restore(root, selected), "undo must be idempotent");
        Check(File.ReadAllText(file, Encoding.UTF8).Contains("后加词\tzzzz\t90"), "undo must preserve subsequent additions");
        Console.WriteLine("Passed " + checks + " store checks using an isolated temporary directory. No IME, UI or user dictionary was accessed.");
    }
}
