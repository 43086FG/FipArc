// SettingsForm.cs —— 设置窗口
//
// 目前只有一项：界面语言。
// 设计取舍：
//   · 语言列表显示各语言自己的名字（English、日本語…），看不懂当前界面也能找到自己的语言
//   · 改完立刻写注册表，然后重启主程序 —— 比逐个控件刷文字可靠得多
using System;
using System.Drawing;
using System.Windows.Forms;

namespace FipArc
{
    public sealed class SettingsForm : Form
    {
        ComboBox langBox;
        bool changed;

        public SettingsForm()
        {
            Text = Lang.S("set.title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(420, 150);
            BackColor = Color.White;

            Label lbl = new Label();
            lbl.Text = Lang.S("set.language");
            lbl.AutoSize = true;
            lbl.Location = new Point(24, 30);
            Controls.Add(lbl);

            langBox = new ComboBox();
            langBox.DropDownStyle = ComboBoxStyle.DropDownList;
            langBox.Items.AddRange(Lang.Names);
            langBox.SelectedIndex = Lang.Current;
            langBox.Location = new Point(150, 27);
            langBox.Size = new Size(230, 24);
            Controls.Add(langBox);

            Label hint = new Label();
            hint.Text = Lang.S("set.restart");
            hint.AutoSize = false;
            hint.Size = new Size(370, 40);
            hint.Location = new Point(24, 60);
            hint.ForeColor = Color.FromArgb(110, 110, 110);
            Controls.Add(hint);

            Button ok = new Button();
            ok.Text = Lang.S("ok");
            ok.Location = new Point(220, 108);
            ok.Size = new Size(80, 28);
            ok.Click += delegate
            {
                if (langBox.SelectedIndex != Lang.Current)
                {
                    Lang.Current = langBox.SelectedIndex;
                    changed = true;
                }
                Close();
            };
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);
            AcceptButton = ok;

            Button cancel = new Button();
            cancel.Text = Lang.S("cancel");
            cancel.Location = new Point(306, 108);
            cancel.Size = new Size(80, 28);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);
            CancelButton = cancel;
        }

        /// <summary>语言是否被改过（改过就要重启）</summary>
        public bool LanguageChanged { get { return changed; } }
    }
}
