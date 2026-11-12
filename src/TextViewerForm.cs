// TextViewerForm.cs —— 文本查看 / 编辑器（支持普通文本与 .fxt）
//
// 两个坑（都踩过）：
//   1) RichTextBox 用 Dock=Fill、ToolStrip 用 Dock=Top，如果 Fill 先停靠，
//      工具栏会盖住正文的第一行 —— 看着像"第一行丢了"。
//      解决：box.BringToFront()，让它最后停靠。
//   2) 只"看"不能"改"不实用。现在可以直接编辑，保存时写回原文件：
//      .fxt 会重新打包成 FIP 容器（保持原来的文件名和加密）。 
using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace FipArc
{
    public sealed class TextViewerForm : Form
    {
        RichTextBox box;
        Label status;
        string currentFile;
        bool dirty = false;
        bool suppressDirty = false;

        public TextViewerForm()
        {
            Text = "FIP 文本编辑器";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(820, 600);
            BackColor = Color.FromArgb(40, 40, 44);
            KeyPreview = true;

            ToolStrip ts = new ToolStrip();
            ts.GripStyle = ToolStripGripStyle.Hidden;
            ts.RenderMode = ToolStripRenderMode.System;
            Controls.Add(ts);

            ToolStripButton bOpen = new ToolStripButton("打开…");
            bOpen.Click += delegate { OpenDialog(); };
            ts.Items.Add(bOpen);

            ToolStripButton bSave = new ToolStripButton("保存");
            bSave.Click += delegate { SaveCurrent(); };
            ts.Items.Add(bSave);

            ToolStripButton bSaveAs = new ToolStripButton("另存为…");
            bSaveAs.Click += delegate { SaveAs(); };
            ts.Items.Add(bSaveAs);

            ts.Items.Add(new ToolStripSeparator());

            ToolStripButton bExport = new ToolStripButton("导出为 .txt");
            bExport.Click += delegate { ExportNormal(); };
            ts.Items.Add(bExport);

            ToolStripButton bWrap = new ToolStripButton("自动换行");
            bWrap.CheckOnClick = true;
            bWrap.Checked = true;
            bWrap.Click += delegate(object s, EventArgs e) { box.WordWrap = bWrap.Checked; };
            ts.Items.Add(bWrap);

            box = new RichTextBox();
            box.Dock = DockStyle.Fill;
            box.BackColor = Color.FromArgb(30, 30, 34);
            box.ForeColor = Color.Gainsboro;
            box.Font = new Font("Consolas", 10.5f);
            box.WordWrap = true;
            box.BorderStyle = BorderStyle.None;
            box.ReadOnly = false;                 // 可编辑
            box.AcceptsTab = true;
            box.HideSelection = false;
            box.TextChanged += delegate { if (!suppressDirty) { dirty = true; UpdateTitle(); } };
            Controls.Add(box);

            status = new Label();
            status.Dock = DockStyle.Bottom;
            status.Height = 24;
            status.ForeColor = Color.Gainsboro;
            status.BackColor = Color.FromArgb(48, 48, 52);
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.Padding = new Padding(8, 0, 0, 0);
            Controls.Add(status);

            // 关键：Fill 控件放最后停靠，否则工具栏会盖住第一行
            box.BringToFront();

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.S) { SaveCurrent(); e.Handled = true; }
                else if (e.Control && e.KeyCode == Keys.O) { OpenDialog(); e.Handled = true; }
                else if (e.KeyCode == Keys.Escape) Close();
            };

            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (!dirty) return;
                DialogResult r = MessageBox.Show(this,
                    "有未保存的修改，要保存吗？", "关闭",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                if (r == DialogResult.Yes) { if (!SaveCurrent()) e.Cancel = true; }
            };
        }

        void UpdateTitle()
        {
            Text = "FIP 文本编辑器 — " +
                   (currentFile == null ? "（未打开文件）" : Path.GetFileName(currentFile)) +
                   (dirty ? " *" : "");
        }

        public void OpenDialog()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "文本文件|*.txt;*.log;*.ini;*.cfg;*.json;*.xml;*.csv;*.md;*.fxt|所有文件|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadFile(dlg.FileName);
            }
        }

        public void LoadFile(string path)
        {
            try
            {
                byte[] data;
                if (FipFile.IsFip(path))
                {
                    string pwd = null;
                    if (FipFile.NeedsPassword(path))
                    {
                        pwd = PasswordForm.AskOpen(this, Path.GetFileName(path), "");
                        if (pwd == null) return;
                    }
                    byte kind; string nm;
                    data = FipFile.Read(path, out kind, out nm, pwd);
                }
                else data = File.ReadAllBytes(path);

                string text = Decode(data);

                suppressDirty = true;
                box.Text = text;
                box.SelectionStart = 0;
                box.SelectionLength = 0;
                suppressDirty = false;
                dirty = false;

                currentFile = path;
                UpdateTitle();
                UpdateStatus();
            }
            catch (WrongPasswordException)
            {
                MessageBox.Show(this, "密码不正确。", "无法打开", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void UpdateStatus()
        {
            status.Text = string.Format("{0}    {1} 字符    {2} 行    {3}{4}",
                currentFile == null ? "—" : Path.GetFileName(currentFile),
                box.TextLength, box.Lines.Length,
                FipFile.IsFip(currentFile ?? "") ? "FIP 文本（.fxt）" : "普通文本",
                dirty ? "    未保存" : "");
        }

        static string Decode(byte[] data)
        {
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                return Encoding.UTF8.GetString(data, 3, data.Length - 3);
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
                return Encoding.Unicode.GetString(data, 2, data.Length - 2);
            try
            {
                UTF8Encoding strict = new UTF8Encoding(false, true);
                return strict.GetString(data);
            }
            catch
            {
                try { return Encoding.GetEncoding("GBK").GetString(data); }
                catch { return Encoding.Default.GetString(data); }
            }
        }

        /// <summary>保存写回原文件：.fxt 重新打包成 FIP 容器，普通文本写 UTF-8</summary>
        bool SaveCurrent()
        {
            if (currentFile == null)
            {
                SaveAs();
                return currentFile != null;
            }
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(box.Text);

                if (FipFile.IsFip(currentFile))
                {
                    // 读出容器头，保持原来的种类和原始文件名；不设密码
                    FipHeader h = FipFile.ReadHeader(currentFile);
                    byte[] packed = FipFile.Write(h.Kind, h.Name, bytes);
                    File.WriteAllBytes(currentFile, packed);
                }
                else
                {
                    // 原来有 BOM 就带上 BOM
                    byte[] old = File.ReadAllBytes(currentFile);
                    bool hadBom = old.Length >= 3 && old[0] == 0xEF && old[1] == 0xBB && old[2] == 0xBF;
                    File.WriteAllBytes(currentFile, hadBom
                        ? new UTF8Encoding(true).GetBytes(box.Text)
                        : bytes);
                }

                dirty = false;
                UpdateTitle();
                UpdateStatus();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        void SaveAs()
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "另存为";
                dlg.Filter = "FIP 文本|*.fxt|文本文件|*.txt|所有文件|*.*";
                string stem = currentFile == null ? "未命名" : Path.GetFileNameWithoutExtension(currentFile);
                dlg.FileName = stem + (FipFile.IsFip(currentFile ?? "") ? ".fxt" : ".txt");
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(box.Text);
                    string target = dlg.FileName;
                    if (target.ToLower().EndsWith(".fxt"))
                    {
                        string name = Path.GetFileNameWithoutExtension(target) + ".txt";
                        File.WriteAllBytes(target, FipFile.Write(Kind.TextTxt, name, bytes));
                    }
                    else File.WriteAllBytes(target, bytes);

                    currentFile = target;
                    dirty = false;
                    UpdateTitle();
                    UpdateStatus();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        void ExportNormal()
        {
            if (currentFile == null) return;
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "文本文件|*.txt";
                dlg.FileName = Path.GetFileNameWithoutExtension(currentFile) + ".txt";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(dlg.FileName, box.Text, new UTF8Encoding(true));
                MessageBox.Show(this, "已导出：" + dlg.FileName, "完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
