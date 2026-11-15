// PasswordForm.cs —— 密码输入对话框
using System;
using System.Drawing;
using System.Windows.Forms;

namespace FipArc
{
    public sealed class PasswordForm : Form
    {
        TextBox box;
        CheckBox show;
        CheckBox encBox;         // 「加密这个压缩包」（只有设置新密码时才出现）
        CheckBox extraBox;       // 7z 的「加密文件名」/ zip 的「AES-256」
        public string Password;

        /// <summary>是否要加密（只在设置新密码时有意义）</summary>
        public bool DoEncrypt { get { return encBox == null || encBox.Checked; } }

        /// <summary>可选勾选框是否被勾上</summary>
        public bool ExtraChecked { get { return extraBox != null && extraBox.Checked; } }

        PasswordForm(string title, string hint, string okText, bool allowEmpty, string extraText, bool offerEncryptSwitch)
        {
            Text = title;
            // 用 CenterScreen 而不是 CenterParent：从右键菜单 / 命令行过来时
            // 没有 owner，CenterParent 会把窗口放到不可预期的位置，
            // 甚至弹在资源管理器窗口后面，用户就以为"没弹密码框"。
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;              // 一定要盖在最前面
            bool hasExtra = !string.IsNullOrEmpty(extraText);
            ClientSize = new Size(440, (hasExtra ? 212 : 186) + (offerEncryptSwitch ? 28 : 0));
            Font = new Font("Microsoft YaHei UI", 9f);

            int y = 14;

            // 「加密这个压缩包」总开关：不勾就完全不加密，密码框和 AES 勾选框都变灰
            if (offerEncryptSwitch)
            {
                encBox = new CheckBox();
                encBox.Text = Lang.S("pw.encrypt");
                encBox.AutoSize = true;
                encBox.Checked = true;
                encBox.Location = new Point(18, y);
                Controls.Add(encBox);
                y += 28;
            }

            Label l1 = new Label();
            l1.Text = hint;
            l1.AutoSize = false;
            l1.Size = new Size(400, 48);
            l1.Location = new Point(18, y);
            Controls.Add(l1);
            y += 54;

            Label l2 = new Label();
            l2.Text = Lang.S("pw.label");
            l2.AutoSize = true;
            l2.Location = new Point(18, y + 3);
            Controls.Add(l2);

            box = new TextBox();
            box.Location = new Point(70, y);
            box.Size = new Size(350, 26);
            box.UseSystemPasswordChar = true;
            Controls.Add(box);
            y += 32;

            show = new CheckBox();
            show.Text = Lang.S("pw.show");
            show.AutoSize = true;
            show.Location = new Point(70, y);
            show.CheckedChanged += delegate { box.UseSystemPasswordChar = !show.Checked; };
            Controls.Add(show);
            y += 27;

            if (hasExtra)
            {
                extraBox = new CheckBox();
                extraBox.Text = extraText;
                extraBox.AutoSize = true;
                extraBox.Checked = true;
                extraBox.Location = new Point(70, y);
                Controls.Add(extraBox);
            }

            // 总开关一关，密码框和两个勾选框全部禁用
            if (encBox != null)
            {
                encBox.CheckedChanged += delegate
                {
                    bool on = encBox.Checked;
                    box.Enabled = on;
                    show.Enabled = on;
                    if (extraBox != null) extraBox.Enabled = on;
                };
            }

            // 按钮永远贴在窗口底部
            int btnY = ClientSize.Height - 44;

            Button ok = new Button();
            ok.Text = okText;
            ok.Location = new Point(256, btnY);
            ok.Size = new Size(80, 30);
            ok.DialogResult = DialogResult.OK;
            ok.Click += delegate
            {
                bool wantEncrypt = (encBox == null) || encBox.Checked;
                if (wantEncrypt && !allowEmpty && box.Text.Length == 0)
                {
                    MessageBox.Show(this, Lang.S("pw.need"), Lang.S("pw.hint"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.None;
                    return;
                }
                Password = wantEncrypt ? box.Text : "";
            };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = Lang.S("cancel");
            cancel.Location = new Point(344, btnY);
            cancel.Size = new Size(80, 30);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        /// <summary>设置新密码（不带额外勾选框）</summary>
        public static string AskNew(IWin32Window owner, string what)
        {
            return AskNew(owner, what, null, null);
        }

        /// <summary>设置新密码；返回 null 表示取消，空字符串表示不设密码</summary>
        public static string AskNew(IWin32Window owner, string what, string extraText, bool[] outVal)
        {
            using (PasswordForm f = new PasswordForm(Lang.S("pw.tset"),
                string.Format(Lang.S("pw.tipset"), what), Lang.S("ok"), true, extraText, true))
            {
                f.ShowInTaskbar = true;
                f.Activate();
                f.BringToFront();
                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                if (outVal != null && outVal.Length > 0) outVal[0] = f.ExtraChecked;
                return f.Password ?? "";
            }
        }

        /// <summary>输入已有密码；返回 null 表示取消</summary>
        public static string AskOpen(IWin32Window owner, string what, string extra)
        {
            using (PasswordForm f = new PasswordForm(Lang.S("pw.tneedtitle"),
                Lang.S("pw.tneed") + Environment.NewLine +
                Lang.S("pw.tneed2") +
                (string.IsNullOrEmpty(extra) ? "" : Environment.NewLine + extra), Lang.S("pw.open"), false, null, false))
            {
                f.ShowInTaskbar = true;
                f.Activate();
                f.BringToFront();
                if (f.ShowDialog(owner) == DialogResult.OK) return f.Password;
                return null;
            }
        }
    }
}
