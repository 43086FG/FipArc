// ImageViewerForm.cs —— 自带图片查看器（支持普通图片与 .fpg/.fjp）
//
// 关键点（之前两个 bug 都出在这里）：
//   1) 不能用 PictureBox.SizeMode = Zoom + Dock = Fill。
//      那样不管位图多大都缩放填满控件，"放大/缩小"按钮看起来完全没反应。
//      正确做法：SizeMode = Normal，放在 AutoScroll 的 Panel 里，
//      由 zoom 直接决定 PictureBox 的尺寸。
//   2) PictureBox 拿不到键盘焦点，所以 pic.MouseWheel 几乎不会触发。
//      滚轮要挂在窗体上（子控件不处理就一直冒泡到窗体）。
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using System.Collections.Generic;

namespace FipArc
{
    public sealed class ImageViewerForm : Form
    {
        Panel viewport;
        PictureBox pic;
        Label status;
        string currentFile;
        List<string> siblings = new List<string>();
        double zoom = 1.0;
        float rotation = 0f;
        Image current;

        const double MinZoom = 0.02, MaxZoom = 40.0, ZoomStep = 1.2;

        bool dragging = false;
        Point dragStart;
        Point scrollStart;

        public ImageViewerForm()
        {
            Text = "FIP 图片查看器";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(900, 640);
            MinimumSize = new Size(420, 320);
            BackColor = Color.FromArgb(32, 32, 34);
            KeyPreview = true;

            // ---- 工具栏 ----
            ToolStrip ts = new ToolStrip();
            ts.GripStyle = ToolStripGripStyle.Hidden;
            ts.BackColor = Color.FromArgb(48, 48, 52);
            ts.ForeColor = Color.Gainsboro;
            ts.RenderMode = ToolStripRenderMode.System;
            Controls.Add(ts);

            AddBtn(ts, "打开…", delegate { OpenDialog(); });
            AddBtn(ts, "上一张", delegate { Nav(-1); });
            AddBtn(ts, "下一张", delegate { Nav(1); });
            ts.Items.Add(new ToolStripSeparator());
            AddBtn(ts, "放大", delegate { ZoomBy(ZoomStep, null); });
            AddBtn(ts, "缩小", delegate { ZoomBy(1.0 / ZoomStep, null); });
            AddBtn(ts, "适应窗口", delegate { FitToWindow(); });
            AddBtn(ts, "1:1", delegate { SetZoom(1.0, null); });
            ts.Items.Add(new ToolStripSeparator());
            AddBtn(ts, "左转", delegate { rotation -= 90; if (rotation < 0) rotation += 360; Render(); });
            AddBtn(ts, "右转", delegate { rotation += 90; if (rotation >= 360) rotation -= 360; Render(); });
            ts.Items.Add(new ToolStripSeparator());
            AddBtn(ts, "转为普通图片", delegate { ExportNormal(); });

            // ---- 可滚动的显示区 ----
            viewport = new Panel();
            viewport.Dock = DockStyle.Fill;
            viewport.AutoScroll = true;
            viewport.BackColor = Color.FromArgb(32, 32, 34);
            Controls.Add(viewport);

            pic = new PictureBox();
            pic.SizeMode = PictureBoxSizeMode.Normal;   // 不自动缩放，尺寸由 zoom 决定
            pic.BackColor = Color.FromArgb(32, 32, 34);
            pic.Location = new Point(0, 0);
            pic.Size = new Size(1, 1);
            pic.DoubleClick += delegate { FitToWindow(); };
            viewport.Controls.Add(pic);

            // 滚轮：窗体 + 显示区 + 图片，三处都挂，确保鼠标在哪都能缩放
            viewport.MouseWheel += OnWheel;
            pic.MouseWheel += OnWheel;
            this.MouseWheel += OnWheel;

            // 拖动平移
            viewport.MouseDown += BeginDrag;
            pic.MouseDown += BeginDrag;
            viewport.MouseMove += DoDrag;
            pic.MouseMove += DoDrag;
            viewport.MouseUp += EndDrag;
            pic.MouseUp += EndDrag;

            // ---- 状态栏 ----
            status = new Label();
            status.Dock = DockStyle.Bottom;
            status.Height = 24;
            status.ForeColor = Color.Gainsboro;
            status.BackColor = Color.FromArgb(48, 48, 52);
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.Padding = new Padding(8, 0, 0, 0);
            Controls.Add(status);

            // 关键：Fill 面板放最后停靠，否则工具栏会盖住它顶部一条
            viewport.BringToFront();

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Left) Nav(-1);
                else if (e.KeyCode == Keys.Right) Nav(1);
                else if (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add) ZoomBy(ZoomStep, null);
                else if (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract) ZoomBy(1.0 / ZoomStep, null);
                else if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0) SetZoom(1.0, null);
                else if (e.KeyCode == Keys.F) FitToWindow();
                else if (e.KeyCode == Keys.Escape) Close();
            };

            viewport.Resize += delegate { CenterIfSmall(); };

            AllowDrop = true;
            DragEnter += delegate(object s, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            DragDrop += delegate(object s, DragEventArgs e)
            {
                string[] f = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (f != null && f.Length > 0) LoadFile(f[0]);
            };
        }

        void AddBtn(ToolStrip ts, string text, EventHandler h)
        {
            ToolStripButton b = new ToolStripButton(text);
            b.ForeColor = Color.Gainsboro;
            b.Click += h;
            ts.Items.Add(b);
        }

        // ---------------- 滚轮 ----------------
        void OnWheel(object sender, MouseEventArgs e)
        {
            if (current == null) return;
            Point anchor = PointToClient(Cursor.Position);
            ZoomBy(e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep, anchor);
            // 交给窗体自己处理完，避免再被父级重复处理
            HandledMouseEventArgs h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;
        }

        // ---------------- 拖动平移 ----------------
        void BeginDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || current == null) return;
            dragging = true;
            dragStart = PointToClient(Cursor.Position);
            scrollStart = new Point(-viewport.AutoScrollPosition.X, -viewport.AutoScrollPosition.Y);
            Cursor = Cursors.SizeAll;
        }

        void DoDrag(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            Point now = PointToClient(Cursor.Position);
            int dx = now.X - dragStart.X;
            int dy = now.Y - dragStart.Y;
            try { viewport.AutoScrollPosition = new Point(scrollStart.X - dx, scrollStart.Y - dy); }
            catch { }
        }

        void EndDrag(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            Cursor = Cursors.Default;
        }

        // ---------------- 加载 ----------------
        public void OpenDialog()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "打开图片";
                dlg.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.fpg;*.fjp|所有文件|*.*";
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
                    byte kind; string nm;
                    string pwd = null;
                    if (FipFile.NeedsPassword(path))
                    {
                        pwd = PasswordForm.AskOpen(this, Path.GetFileName(path), "");
                        if (pwd == null) return;
                    }
                    data = FipFile.Read(path, out kind, out nm, pwd);
                }
                else data = File.ReadAllBytes(path);

                if (current != null) { current.Dispose(); current = null; }
                using (MemoryStream ms = new MemoryStream(data))
                using (Image tmp = Image.FromStream(ms))
                    current = new Bitmap(tmp);

                currentFile = path;
                BuildSiblings(path);
                rotation = 0;
                Text = "FIP 图片查看器 — " + Path.GetFileName(path);
                FitToWindow();
            }
            catch (WrongPasswordException)
            {
                MessageBox.Show(this, "密码不正确。", "无法打开", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开图片：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void BuildSiblings(string path)
        {
            siblings.Clear();
            try
            {
                string dir = Path.GetDirectoryName(path);
                foreach (string f in Directory.GetFiles(dir))
                    if (Playable.IsImage(f)) siblings.Add(f);
                siblings.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
        }

        void Nav(int delta)
        {
            if (siblings.Count == 0 || currentFile == null) return;
            int idx = siblings.FindIndex(delegate(string s) { return string.Equals(s, currentFile, StringComparison.OrdinalIgnoreCase); });
            if (idx < 0) idx = 0;
            int n = (idx + delta + siblings.Count) % siblings.Count;
            LoadFile(siblings[n]);
        }

        // ---------------- 缩放 ----------------
        void ZoomBy(double factor, Point? screenAnchor)
        {
            if (current == null) return;
            SetZoom(zoom * factor, screenAnchor);
        }

        void SetZoom(double z, Point? screenAnchor)
        {
            if (current == null) return;
            if (z < MinZoom) z = MinZoom;
            if (z > MaxZoom) z = MaxZoom;
            if (Math.Abs(z - zoom) < 1e-9) return;

            // 记下锚点在【图像坐标】里的位置，缩放后让它还在鼠标底下
            Point anchor = screenAnchor.HasValue ? anchor = screenAnchor.Value : anchor = PointToClient(Cursor.Position);
            Point inView = viewport.PointToClient(anchor);
            int addX = -viewport.AutoScrollPosition.X;
            int addY = -viewport.AutoScrollPosition.Y;
            double imgX = (addX + inView.X - pic.Left) / zoom;
            double imgY = (addY + inView.Y - pic.Top) / zoom;

            zoom = z;
            Render();

            try
            {
                int newX = (int)(imgX * zoom) - inView.X + pic.Left;
                int newY = (int)(imgY * zoom) - inView.Y + pic.Top;
                viewport.AutoScrollPosition = new Point(Math.Max(0, newX), Math.Max(0, newY));
            }
            catch { }
        }

        void FitToWindow()
        {
            if (current == null) return;
            bool swap = (((int)Math.Round(rotation) / 90) % 2) != 0;
            double sw = Math.Max(1, viewport.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            double sh = Math.Max(1, viewport.ClientSize.Height - SystemInformation.HorizontalScrollBarHeight - 4);
            double iw = swap ? current.Height : current.Width;
            double ih = swap ? current.Width : current.Height;
            zoom = Math.Min(sw / iw, sh / ih);
            if (zoom < MinZoom) zoom = MinZoom;
            if (zoom > MaxZoom) zoom = MaxZoom;
            Render();
        }

        // ---------------- 渲染 ----------------
        void Render()
        {
            if (current == null) { pic.Image = null; return; }

            bool swap = (((int)Math.Round(rotation) / 90) % 2) != 0;
            double iw = swap ? current.Height : current.Width;
            double ih = swap ? current.Width : current.Height;

            int outW = (int)Math.Max(1, Math.Round(iw * zoom));
            int outH = (int)Math.Max(1, Math.Round(ih * zoom));

            Bitmap bmp = new Bitmap(outW, outH);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.TranslateTransform(outW / 2f, outH / 2f);
                if (rotation != 0f) g.RotateTransform(rotation);
                float dw = (float)(current.Width * zoom);
                float dh = (float)(current.Height * zoom);
                g.DrawImage(current, new RectangleF(-dw / 2f, -dh / 2f, dw, dh));
            }

            Image old = pic.Image;
            pic.Image = bmp;
            pic.Size = new Size(outW, outH);
            if (old != null) old.Dispose();

            CenterIfSmall();
            UpdateStatus();
        }

        void CenterIfSmall()
        {
            if (pic.Image == null) return;
            int vw = viewport.ClientSize.Width;
            int vh = viewport.ClientSize.Height;
            int x = pic.Width < vw ? (vw - pic.Width) / 2 : 0;
            int y = pic.Height < vh ? (vh - pic.Height) / 2 : 0;
            if (pic.Left != x || pic.Top != y) pic.Location = new Point(x, y);
        }

        void UpdateStatus()
        {
            if (current == null) { status.Text = ""; return; }
            int idx = 1;
            for (int i = 0; i < siblings.Count; i++)
                if (string.Equals(siblings[i], currentFile, StringComparison.OrdinalIgnoreCase)) { idx = i + 1; break; }
            status.Text = string.Format(
                "{0}    {1}×{2}    缩放 {3:0.#}%    旋转 {4}°    [{5}/{6}]    滚轮缩放 · 拖动平移 · 双击适应窗口",
                Path.GetFileName(currentFile), current.Width, current.Height, zoom * 100,
                (int)rotation, idx, siblings.Count);
        }

        // ---------------- 导出 ----------------
        void ExportNormal()
        {
            if (current == null) return;
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "导出为普通图片";
                dlg.Filter = "PNG 图片|*.png|JPEG 图片|*.jpg";
                string stem = currentFile == null ? "image" : Path.GetFileNameWithoutExtension(currentFile);
                dlg.FileName = stem + ".png";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                ImageFormat fmt = dlg.FileName.ToLower().EndsWith(".jpg") ? ImageFormat.Jpeg : ImageFormat.Png;
                try
                {
                    // 导出带旋转的结果
                    if (rotation != 0f)
                    {
                        bool swap = (((int)Math.Round(rotation) / 90) % 2) != 0;
                        Bitmap b = new Bitmap(swap ? current.Height : current.Width, swap ? current.Width : current.Height);
                        using (Graphics g = Graphics.FromImage(b))
                        {
                            g.TranslateTransform(b.Width / 2f, b.Height / 2f);
                            g.RotateTransform(rotation);
                            g.DrawImage(current, -current.Width / 2f, -current.Height / 2f, current.Width, current.Height);
                        }
                        b.Save(dlg.FileName, fmt);
                        b.Dispose();
                    }
                    else current.Save(dlg.FileName, fmt);

                    MessageBox.Show(this, "已导出：" + dlg.FileName, "完成",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }
    }
}
