using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class ManualPhraseDeleteForm : Form
{
    private readonly string root;
    private readonly TextBox search = new TextBox();
    private readonly ListView entries = new ListView();
    private readonly Button remove = new Button();
    private readonly Button undo = new Button();
    private readonly Button close = new Button();
    private readonly Label status = new Label();
    private bool focusSearch;
    private ManualPhraseEntry deleted;
    private bool busy;
    internal bool IsSaving { get { return busy; } }

    internal ManualPhraseDeleteForm(string userRoot)
    {
        root = userRoot;
        Text = "小狼毫删除自造词";
        Font = new Font("Microsoft YaHei UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(600, 390);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        var label = new Label { Text = "搜索词语或编码", AutoSize = true, Location = new Point(18, 21) };
        Controls.Add(label);
        search.SetBounds(145, 16, 320, 29);
        search.TabIndex = 0;
        search.MaxLength = 256;
        search.TextChanged += delegate { if (!busy) RefreshEntries(); };
        Controls.Add(search);
        var clear = new Button { Text = "显示全部", TabIndex = 1 };
        clear.SetBounds(476, 15, 105, 31);
        clear.Click += delegate { if (!busy) { search.Clear(); RefreshEntries(); search.Focus(); } };
        Controls.Add(clear);
        entries.SetBounds(18, 61, 564, 230);
        entries.View = View.Details;
        entries.FullRowSelect = true;
        entries.HideSelection = false;
        entries.MultiSelect = false;
        entries.TabIndex = 2;
        entries.Columns.Add("词语", 350);
        entries.Columns.Add("编码", 170);
        entries.SelectedIndexChanged += delegate { remove.Enabled = !busy && entries.SelectedItems.Count == 1; };
        Controls.Add(entries);
        status.SetBounds(18, 299, 564, 43);
        status.ForeColor = Color.DimGray;
        Controls.Add(status);
        undo.Text = "撤销上次";
        undo.SetBounds(18, 347, 115, 31);
        undo.TabIndex = 4;
        undo.Enabled = false;
        undo.Click += UndoClicked;
        Controls.Add(undo);
        remove.Text = "删除所选";
        remove.SetBounds(343, 347, 115, 31);
        remove.TabIndex = 3;
        remove.Click += DeleteClicked;
        Controls.Add(remove);
        close.Text = "关闭";
        close.SetBounds(469, 347, 113, 31);
        close.TabIndex = 5;
        close.Click += delegate { if (!busy) Hide(); };
        Controls.Add(close);
        FormClosing += delegate(object sender, FormClosingEventArgs e)
        {
            if (busy || e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                if (!busy) Hide();
            }
        };
        Activated += delegate { FocusSearchWhenActive(); };
    }

    internal void OpenRequest(string seed)
    {
        if (!busy)
        {
            search.Text = seed.Trim();
            RefreshEntries();
        }
        focusSearch = !busy;
        WindowState = FormWindowState.Normal;
        if (!Visible) Show();
        PhraseNative.ShowWindowAsync(Handle, 9);
        BringToFront();
        if (!PhraseNative.SetForegroundWindow(Handle))
            ManualPhraseProgram.Log(new InvalidOperationException("Delete window foreground activation was denied."));
        Activate();
        FocusSearchWhenActive();
    }

    private void FocusSearchWhenActive()
    {
        // Never move focus merely because a non-activated window became visible.
        if (focusSearch && !busy && Visible && PhraseNative.GetForegroundWindow() == Handle)
        {
            focusSearch = false;
            ActiveControl = search;
            search.Focus();
            search.SelectAll();
        }
    }

    private void RefreshEntries()
    {
        int selectedIndex = entries.SelectedIndices.Count > 0 ? entries.SelectedIndices[0] : 0;
        entries.BeginUpdate();
        try
        {
            entries.Items.Clear();
            List<ManualPhraseEntry> all = ManualPhraseStore.Read(root);
            string query = search.Text.Trim();
            foreach (ManualPhraseEntry entry in all)
            {
                if (query.Length > 0 && entry.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    entry.Code.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var item = new ListViewItem(new string[] { entry.Text, entry.Code });
                item.Tag = entry;
                entries.Items.Add(item);
            }
            if (entries.Items.Count > 0)
            {
                int index = Math.Min(selectedIndex, entries.Items.Count - 1);
                entries.Items[index].Selected = entries.Items[index].Focused = true;
                entries.EnsureVisible(index);
            }
            remove.Enabled = !busy && entries.SelectedItems.Count == 1;
            undo.Enabled = !busy && deleted != null;
            status.Text = "手工自造词 " + all.Count + " 条，匹配 " + entries.Items.Count + " 条。↓进入列表，Delete 删词，Esc 关闭。";
            if (all.Count > 0 && entries.Items.Count == 0) status.Text += " 可点“显示全部”。";
        }
        catch (Exception ex) { status.Text = "读取失败：" + ex.Message; remove.Enabled = false; }
        finally { entries.EndUpdate(); }
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Escape) { if (!busy) Hide(); return true; }
        if (search.Focused && (keyData == Keys.Down || keyData == Keys.Enter))
        { entries.Focus(); return true; }
        if (entries.Focused && (keyData == Keys.Delete || keyData == Keys.Enter))
        { DeleteClicked(this, EventArgs.Empty); return true; }
        return base.ProcessCmdKey(ref message, keyData);
    }

    private async void DeleteClicked(object sender, EventArgs e)
    {
        if (busy || entries.SelectedItems.Count != 1) return;
        var selected = (ManualPhraseEntry)entries.SelectedItems[0].Tag;
        try
        {
            if (!ManualPhraseStore.Delete(root, selected))
            { RefreshEntries(); status.Text = "该记录已经不在文件中，已刷新列表。"; return; }
            deleted = selected;
        }
        catch (Exception ex) { status.Text = "删除失败：" + ex.Message; return; }
        await DeployChange("已删除“" + selected.Text + "”");
    }

    private async void UndoClicked(object sender, EventArgs e)
    {
        if (busy || deleted == null) return;
        var selected = deleted;
        try
        {
            ManualPhraseStore.Restore(root, selected);
            deleted = null;
        }
        catch (Exception ex) { status.Text = "恢复失败：" + ex.Message; return; }
        await DeployChange("已恢复“" + selected.Text + "”");
    }

    private async Task DeployChange(string result)
    {
        busy = true;
        remove.Enabled = undo.Enabled = search.Enabled = entries.Enabled = close.Enabled = false;
        status.Text = result + "，正在重新部署……";
        string note;
        try { note = await Task.Run(() => ManualPhraseForm.Deploy()); }
        catch (Exception ex) { note = "请手动重新部署：" + ex.Message; }
        busy = false;
        search.Enabled = entries.Enabled = close.Enabled = true;
        RefreshEntries();
        status.Text = result + (note.Length == 0 ? "，已部署。可继续删词或撤销上次。" : "。" + note);
        entries.Focus();
    }
}
