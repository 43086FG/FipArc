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
        public string Password;

        PasswordForm(string title, string hint, string okText, bool allowEmpty)
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
            ClientSize = new Size(420, 190);
            Font = new Font("Microsoft YaHei UI", 9f);

            Label l1 = new Label();
            l1.Text = hint;
            l1.AutoSize = false;
            l1.Size = new Size(380, 40);
            l1.Location = new Point(18, 16);
            Controls.Add(l1);

            Label l2 = new Label();
            l2.Text = Lang.S("pw.label");
            l2.AutoSize = true;
            l2.Location = new Point(18, 64);
            Controls.Add(l2);

            box = new TextBox();
            box.Location = new Point(70, 61);
            box.Size = new Size(330, 26);
            box.UseSystemPasswordChar = true;
            Controls.Add(box);

            show = new CheckBox();
            show.Text = Lang.S("pw.show");
            show.AutoSize = true;
            show.Location = new Point(70, 93);
            show.CheckedChanged += delegate { box.UseSystemPasswordChar = !show.Checked; };
            Controls.Add(show);

            Button ok = new Button();
            ok.Text = okText;
            ok.Location = new Point(236, 140);
            ok.Size = new Size(80, 30);
            ok.DialogResult = DialogResult.OK;
            ok.Click += delegate
            {
                if (!allowEmpty && box.Text.Length == 0)
                {
                    MessageBox.Show(this, Lang.S("pw.need"), Lang.S("pw.hint"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.None;
                    return;
                }
                Password = box.Text;
            };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = Lang.S("cancel");
            cancel.Location = new Point(324, 140);
            cancel.Size = new Size(80, 30);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        /// <summary>设置新密码；返回 null 表示取消，空字符串表示不设密码</summary>
        public static string AskNew(IWin32Window owner, string what)
        {
            using (PasswordForm f = new PasswordForm(Lang.S("pw.tset"),
                string.Format(Lang.S("pw.tipset"), what), Lang.S("ok"), true))
            {
                f.ShowInTaskbar = true;
                f.Activate();
                f.BringToFront();
                if (f.ShowDialog(owner) == DialogResult.OK) return f.Password ?? "";
                return null;
            }
        }

        /// <summary>输入已有密码；返回 null 表示取消</summary>
        public static string AskOpen(IWin32Window owner, string what, string extra)
        {
            using (PasswordForm f = new PasswordForm(Lang.S("pw.tneedtitle"),
                Lang.S("pw.tneed") + Environment.NewLine +
                Lang.S("pw.tneed2") +
                (string.IsNullOrEmpty(extra) ? "" : Environment.NewLine + extra), Lang.S("pw.open"), false))
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
