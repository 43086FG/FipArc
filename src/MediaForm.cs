// MediaForm.cs —— 自带媒体播放器
//
// 三级引擎，逐级降级：
//   1) WPF MediaElement —— 走 Windows Media Foundation，格式面最广
//      （H.264/AAC 的 MP4、WMV、AVI、MP3、WAV、WMA、MPEG ...）
//   2) DirectShow（Player.cs）—— 前一个打不开时再试一次
//   3) 系统默认播放器 —— 都不行就交给系统，并告诉用户缺了什么解码器
//
// 重要：Windows 10/11 上 H.264 / AAC 解码器是「Windows Media Player」
// 这个可选功能的一部分，被禁用的话 mfh264dec.dll / mfheaacdec.dll 都不在，
// 任何程序都放不了这类 MP4。这时我们会给出明确提示 + 一键修复按钮。
using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace FipArc
{
    public sealed class MediaForm : Form
    {
        Panel screen;              // DirectShow 的宿主面板
        ElementHost host;          // WPF MediaElement 的宿主
        System.Windows.Controls.MediaElement me;
        System.Windows.Controls.Grid overlay;          // 中间的播放/暂停图标
        System.Windows.Shapes.Path playIcon, pauseIcon;
        int overlayHoldMs;                             // 播放中图标再显示多久（毫秒）
        Panel bottom;
        Label nameLabel, timeLabel, lblSpeed, lblVol, lblEngine;
        Button btnPlay, btnStop, btnOpen, btnFull, btnClose;
        ComboBox speedBox;
        TrackBar seek, vol;
        Timer timer;

        DsPlayer ds;               // 备用引擎
        bool useDs;                // 当前是不是在用 DirectShow
        string openedPath, tempFile;
        string currentPath;      // 当前实际交给引擎的文件路径（临时文件或原路径）
        bool seekDragging, playing;
        bool failedOnce;
        bool dsPlaybackStalled;    // DirectShow 是否已确认启动不了
        int dsZeroTicks;           // 进度为 0 持续了多久

        public MediaForm()
        {
            Text = "FIP 媒体播放器";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(960, 620);
            MinimumSize = new Size(600, 400);
            BackColor = Color.Black;
            KeyPreview = true;

            // 先加 bottom，后加 screen：Dock 按控件集合倒序计算，
            // Fill 的控件必须在最后一个位置（index 0）才能拿到剩余空间。
            bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 108;
            bottom.BackColor = Color.FromArgb(32, 32, 34);
            Controls.Add(bottom);

            screen = new Panel();
            screen.Dock = DockStyle.Fill;

            // 点击画面 = 暂停 / 继续（DirectShow 引擎走这条路径）
            screen.MouseClick += delegate(object s, MouseEventArgs ev)
            {
                if (ev.Button == MouseButtons.Left) TogglePlay();
            };
            screen.BackColor = Color.Black;
            screen.Visible = false;
            Controls.Add(screen);

            host = new ElementHost();
            host.Dock = DockStyle.Fill;
            host.BackColor = Color.Black;
            Controls.Add(host);
            host.BringToFront();

            try
            {
                me = new System.Windows.Controls.MediaElement();
                me.LoadedBehavior = System.Windows.Controls.MediaState.Manual;
                me.UnloadedBehavior = System.Windows.Controls.MediaState.Manual;
                me.Stretch = System.Windows.Media.Stretch.Uniform;
                me.MediaOpened += delegate
                {
                    playing = true;
                    SyncPlayState();
                    seek.Value = 0;
                    UpdateTime();
                };
                me.MediaEnded += delegate { playing = false; SyncPlayState(); };
                me.MediaFailed += delegate(object s, System.Windows.ExceptionRoutedEventArgs e) { OnEngineFailed(e.ErrorException == null ? "" : e.ErrorException.Message); };

                // 点击画面 = 暂停 / 继续（播放器最常用的操作）
                // MediaElement 是 WPF 控件，要用 WPF 的鼠标事件，不能用 WinForms 的
                me.MouseLeftButtonUp += delegate { TogglePlay(); };

                // 再罩一层透明 Border 专门接点击。
                // 原因：MediaElement 用硬件渲染时，画面那块不一定参与 WPF 命中测试，
                // 只挂 me 上的事件可能点不到；透明 Border 覆盖在上层就一定能收到。
                System.Windows.Controls.Grid root = new System.Windows.Controls.Grid();
                root.Children.Add(me);
                System.Windows.Controls.Border hit = new System.Windows.Controls.Border();
                hit.Background = System.Windows.Media.Brushes.Transparent;
                hit.Cursor = System.Windows.Input.Cursors.Hand;
                hit.MouseLeftButtonUp += delegate { TogglePlay(); };
                root.Children.Add(hit);

                // ---- 中间的播放/暂停图标 ----
                // 必须画在 WPF 层里：ElementHost 是独立窗口，
                // WinForms 控件盖不上去（airspace 问题）。
                // 图标用几何图形画，不依赖字体有没有 ▶ / ⏸ 这两个字符。
                overlay = new System.Windows.Controls.Grid();
                overlay.Width = 110;
                overlay.Height = 110;
                overlay.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                overlay.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                overlay.IsHitTestVisible = false;      // 点击交给下面那层

                System.Windows.Controls.Border disc = new System.Windows.Controls.Border();
                disc.CornerRadius = new System.Windows.CornerRadius(55);
                disc.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(120, 0, 0, 0));
                overlay.Children.Add(disc);

                // 三角形（播放）
                playIcon = new System.Windows.Shapes.Path();
                playIcon.Fill = System.Windows.Media.Brushes.White;
                System.Windows.Media.PathGeometry tri = new System.Windows.Media.PathGeometry();
                System.Windows.Media.PathFigure fg = new System.Windows.Media.PathFigure();
                fg.StartPoint = new System.Windows.Point(0, 0);
                fg.Segments.Add(new System.Windows.Media.LineSegment(new System.Windows.Point(0, 46), true));
                fg.Segments.Add(new System.Windows.Media.LineSegment(new System.Windows.Point(40, 23), true));
                fg.IsClosed = true;
                tri.Figures.Add(fg);
                playIcon.Data = tri;
                playIcon.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                playIcon.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                playIcon.Margin = new System.Windows.Thickness(6, 0, 0, 0);
                playIcon.Visibility = System.Windows.Visibility.Visible;
                overlay.Children.Add(playIcon);

                // 两条竖杠（暂停）
                pauseIcon = new System.Windows.Shapes.Path();
                pauseIcon.Fill = System.Windows.Media.Brushes.White;
                System.Windows.Media.GeometryGroup gg = new System.Windows.Media.GeometryGroup();
                gg.Children.Add(new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(0, 0, 13, 46)));
                gg.Children.Add(new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(22, 0, 13, 46)));
                pauseIcon.Data = gg;
                pauseIcon.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                pauseIcon.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                pauseIcon.Visibility = System.Windows.Visibility.Collapsed;
                overlay.Children.Add(pauseIcon);

                root.Children.Add(overlay);

                host.Child = root;
            }
            catch (Exception ex)
            {
                host.Visible = false;
                MessageBox.Show(this, "无法初始化媒体组件：" + ex.Message, "播放器",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            BuildBar();

            bottom.Resize += delegate { LayoutBar(); };
            LayoutBar();

            timer = new Timer();
            timer.Interval = 300;
            timer.Tick += delegate { Tick(); };
            timer.Start();

            DragEnter += delegate(object s, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            DragDrop += delegate(object s, DragEventArgs e)
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0) LoadFile(files[0]);
            };

            FormClosed += delegate { CloseMedia(); };

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Space) { TogglePlay(); e.Handled = true; }
                else if (e.KeyCode == Keys.Escape) { Close(); }
                else if (e.KeyCode == Keys.Left) SeekBy(-5000);
                else if (e.KeyCode == Keys.Right) SeekBy(5000);
            };
        }

        void BuildBar()
        {
            nameLabel = MakeLabel("");
            nameLabel.AutoEllipsis = true;
            bottom.Controls.Add(nameLabel);

            btnPlay = MakeButton("播放", delegate { TogglePlay(); });
            btnStop = MakeButton("停止", delegate { StopAndRewind(); });
            btnOpen = MakeButton("打开…", delegate { OpenDialog(); });
            btnFull = MakeButton("全屏", delegate { ToggleFullScreen(); });
            btnClose = MakeButton("关闭", delegate { Close(); });
            bottom.Controls.Add(btnPlay);
            bottom.Controls.Add(btnStop);
            bottom.Controls.Add(btnOpen);
            bottom.Controls.Add(btnFull);
            bottom.Controls.Add(btnClose);

            timeLabel = MakeLabel("00:00 / 00:00");
            bottom.Controls.Add(timeLabel);

            lblEngine = MakeLabel("");
            lblEngine.ForeColor = Color.FromArgb(140, 140, 145);
            bottom.Controls.Add(lblEngine);

            seek = new TrackBar();
            seek.TickStyle = TickStyle.None;
            seek.Minimum = 0;
            seek.Maximum = 1000;
            seek.MouseDown += delegate { seekDragging = true; };
            seek.MouseUp += delegate
            {
                seekDragging = false;
                SeekTo(((long)seek.Value) * DurationMs / 1000);
            };
            bottom.Controls.Add(seek);

            lblSpeed = MakeLabel("速度");
            bottom.Controls.Add(lblSpeed);

            speedBox = new ComboBox();
            speedBox.DropDownStyle = ComboBoxStyle.DropDownList;
            speedBox.Items.AddRange(new object[] { "0.5x", "0.75x", "1.0x", "1.25x", "1.5x", "2.0x" });
            speedBox.SelectedIndex = 2;
            speedBox.SelectedIndexChanged += delegate { ApplySpeed(); };
            bottom.Controls.Add(speedBox);

            lblVol = MakeLabel("音量");
            bottom.Controls.Add(lblVol);

            vol = new TrackBar();
            vol.TickStyle = TickStyle.None;
            vol.Minimum = 0;
            vol.Maximum = 100;
            vol.Value = 80;
            vol.ValueChanged += delegate { ApplyVolume(); };
            bottom.Controls.Add(vol);
        }

        static Button MakeButton(string text, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.ForeColor = Color.Black;
            b.BackColor = Color.FromArgb(238, 238, 238);
            b.FlatStyle = FlatStyle.System;
            b.Click += onClick;
            return b;
        }

        static Label MakeLabel(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = Color.Gainsboro;
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        /// <summary>按窗口宽度用公式排布，缩放到任何宽度都不错位</summary>
        void LayoutBar()
        {
            int w = bottom.ClientSize.Width;
            if (w < 200) return;

            nameLabel.SetBounds(14, 6, w - 28, 20);

            int y = 30, h = 30;
            btnPlay.SetBounds(14, y, 76, h);
            btnStop.SetBounds(96, y, 76, h);
            timeLabel.SetBounds(180, y + 4, 108, 22);

            int right = w - 14;
            btnClose.SetBounds(right - 72, y, 72, h); right -= 80;
            btnFull.SetBounds(right - 72, y, 72, h); right -= 80;
            btnOpen.SetBounds(right - 76, y, 76, h); right -= 88;

            int seekX = 296;
            int seekW = right - seekX;
            if (seekW < 60) seekW = 60;
            seek.SetBounds(seekX, y + 1, seekW, 32);

            int y2 = 68;
            lblVol.SetBounds(14, y2 + 6, 36, 20);
            vol.SetBounds(54, y2 + 1, 160, 32);
            lblSpeed.SetBounds(232, y2 + 6, 40, 20);
            speedBox.SetBounds(276, y2 + 3, 80, 26);
            lblEngine.SetBounds(376, y2 + 6, Math.Max(100, w - 396), 20);
        }

        // ==================== 打开与播放 ====================

        public void OpenDialog()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "打开媒体文件";
                dlg.Filter =
                    "音视频文件|*.mp4;*.m4v;*.mov;*.mkv;*.avi;*.wmv;*.flv;*.webm;*.mpg;*.mpeg;*.ts;*.3gp;" +
                    "*.mp3;*.wav;*.flac;*.aac;*.m4a;*.ogg;*.wma;*.opus;" +
                    "*.fmp;*.fp4;*.fov|所有文件|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadFile(dlg.FileName);
            }
        }

        public void LoadFile(string path)
        {
            CloseMedia();
            try
            {
                bool isTemp;
                string actual = Playable.Resolve(path, out isTemp);
                if (isTemp) tempFile = actual;
                currentPath = actual;
                openedPath = path;
                nameLabel.Text = Path.GetFileName(path) +
                                 (string.Equals(actual, path, StringComparison.OrdinalIgnoreCase)
                                     ? "" : "   —   " + actual);

                Text = "FIP 媒体播放器 — " + Path.GetFileName(path);

                // 一律 WPF 优先。
                // 理由：WPF MediaElement 是 WMP 内核（底层也是 DirectShow），
                // 它自带成熟可靠的视频渲染；我们手写的 DirectShow 封装
                // 目前只能读出时长、却启动不了播放（进度条不动、画面黑屏）。
                // 装了 LAV 之后 WPF 也能解 OGG/H.264 等格式，所以顺序改成固定 WPF → DS。
                if (TryWpf(actual)) return;
                if (TryDs(actual)) return;

                ReportFailure(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "打不开这个文件",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        bool TryWpf(string actual)
        {
            if (me == null) return false;
            try
            {
                useDs = false;
                host.Visible = true;
                screen.Visible = false;
                host.BringToFront();
                lblEngine.Text = "引擎：Media Foundation";
                me.Source = new Uri(actual);
                me.Play();
                return true;
            }
            catch { return false; }
        }

        bool TryDs(string actual)
        {
            try
            {
                useDs = true;
                dsPlaybackStalled = false;
                dsZeroTicks = 0;
                if (ds != null) { try { ds.Dispose(); } catch { } ds = null; }
                host.Visible = false;
                screen.Visible = true;
                screen.BringToFront();
                screen.CreateControl();
                ds = new DsPlayer(screen.Handle);
                ds.Open(actual);
                ds.ResizeVideo(screen.ClientSize);
                // 音量暂不设：IBasicAudio.put_Volume 的 COM 封装会触发 AccessViolation
                ds.Play();
                playing = true;
                SyncPlayState();
                lblEngine.Text = "引擎：DirectShow";
                return true;
            }
            catch
            {
                if (ds != null) { try { ds.Dispose(); } catch { } ds = null; }
                useDs = false;
                return false;
            }
        }

        /// <summary>WPF 引擎报错时自动降级到 DirectShow</summary>
        void OnEngineFailed(string msg)
        {
            if (failedOnce) return;
            failedOnce = true;

            // 注意：以前这里只对"临时文件"降级，普通文件 WPF 一失败就直接放弃，
            // 导致装了 DirectShow 解码器（比如 LAV）也永远用不上。现在一律再试一次 DirectShow。
            string fallback = !string.IsNullOrEmpty(currentPath) && File.Exists(currentPath)
                ? currentPath
                : (!string.IsNullOrEmpty(tempFile) && File.Exists(tempFile) ? tempFile : openedPath);
            if (!string.IsNullOrEmpty(fallback) && File.Exists(fallback))
            {
                if (TryDs(fallback)) return;
            }
            ReportFailure(openedPath);
            lblEngine.Text = "引擎：不可用 — " + msg;
        }

        // ==================== 失败诊断 ====================

        /// <summary>列出系统缺哪些关键解码器（缺 H.264/AAC 时 MP4 必然放不了）</summary>
        public static System.Collections.Generic.List<string> MissingCodecs()
        {
            System.Collections.Generic.List<string> miss = new System.Collections.Generic.List<string>();
            string sys = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32");
            if (!File.Exists(Path.Combine(sys, "mfh264dec.dll"))) miss.Add("H.264 视频解码器 (mfh264dec.dll)");
            if (!File.Exists(Path.Combine(sys, "mfheaacdec.dll"))) miss.Add("AAC 音频解码器 (mfheaacdec.dll)");
            if (!File.Exists(Path.Combine(sys, "wmp.dll"))) miss.Add("Windows Media Player 组件 (wmp.dll)");
            return miss;
        }

        void ReportFailure(string path)
        {
            System.Collections.Generic.List<string> miss = MissingCodecs();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("这个文件打不开。");
            sb.AppendLine();

            if (miss.Count > 0)
            {
                sb.AppendLine("系统缺少以下媒体组件：");
                foreach (string m in miss) sb.AppendLine("    · " + m);
                sb.AppendLine();
                sb.AppendLine("原因：Windows 10/11 的 H.264、AAC 解码器属于");
                sb.AppendLine("「Windows Media Player」这个可选功能，它被禁用时");
                sb.AppendLine("mfh264dec.dll / mfheaacdec.dll 不会安装，");
                sb.AppendLine("于是所有 MP4 类视频都放不了（本软件、电影和电视都一样）。");
                sb.AppendLine();
                sb.AppendLine("修复办法（按顺序试）：");
                sb.AppendLine("  1) 点「是」自动启用 Windows 媒体功能（需管理员，装完重启）");
                sb.AppendLine("  2) 管理员命令行执行  sfc /scannow  修复被删的系统组件");
                sb.AppendLine("  3) 若仍缺失，说明这版 Windows 是精简版（媒体组件被物理移除），");
                sb.AppendLine("     需要覆盖安装官方 Windows 镜像才能补回。");
                sb.AppendLine();
                sb.AppendLine("在此之前，可以点「否」后用其它播放器（PotPlayer / VLC 等）打开，");
                sb.AppendLine("它们自带解码器，不依赖系统。");
            }
            else
            {
                sb.AppendLine("系统解码器齐全，但这个文件的编码比较特殊");
                sb.AppendLine("（可能是 HEVC / AV1 或特殊封装），需要额外解码包。");
                sb.AppendLine();
                sb.AppendLine("点「是」用系统默认播放器试试。");
            }

            DialogResult r = MessageBox.Show(this, sb.ToString(), "打不开这个文件",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (r != DialogResult.Yes) return;

            if (miss.Count > 0) RunRepair();
            else OpenWithSystemPlayer(path);
        }

        /// <summary>用管理员权限启用 Windows Media Player 可选功能（装解码器）</summary>
        void RunRepair()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("dism.exe");
                psi.Arguments = "/online /enable-feature /featurename:WindowsMediaPlayer /norestart";
                psi.UseShellExecute = true;
                psi.Verb = "runas";                 // 触发 UAC
                psi.WindowStyle = ProcessWindowStyle.Normal;
                Process.Start(psi);
                MessageBox.Show(this,
                    "已开始安装媒体组件。\r\n\r\n" +
                    "安装完成后请重启一次 Windows，然后就能播放 MP4 等视频了。",
                    "正在修复", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch
            {
                // 用户拒绝了 UAC：退回到打开「可选功能」页面
                try
                {
                    Process.Start(new ProcessStartInfo("ms-settings:optionalfeatures") { UseShellExecute = true });
                }
                catch { }
            }
        }

        void OpenWithSystemPlayer(string path)
        {
            try
            {
                string target = (!string.IsNullOrEmpty(tempFile) && File.Exists(tempFile)) ? tempFile : path;
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "无法打开", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ==================== 控制 ====================

        long DurationMs
        {
            get
            {
                if (useDs) return ds != null ? ds.DurationMs : 0;
                if (me == null) return 0;
                try
                {
                    if (me.NaturalDuration.HasTimeSpan) return (long)me.NaturalDuration.TimeSpan.TotalMilliseconds;
                }
                catch { }
                return 0;
            }
        }

        long PositionMs
        {
            get
            {
                if (useDs) return ds != null ? ds.PositionMs : 0;
                if (me == null) return 0;
                try { return (long)me.Position.TotalMilliseconds; } catch { return 0; }
            }
        }

        void SeekTo(long ms)
        {
            if (ms < 0) ms = 0;
            long len = DurationMs;
            if (len > 0 && ms > len) ms = len;
            if (useDs) { if (ds != null) ds.Seek(ms); }
            else if (me != null) { try { me.Position = TimeSpan.FromMilliseconds(ms); } catch { } }
        }

        void SeekBy(long delta) { SeekTo(PositionMs + delta); }

        /// <summary>把按钮文字和中间图标一起同步到当前状态</summary>
        void SyncPlayState()
        {
            btnPlay.Text = playing ? "暂停" : "播放";
            if (overlay == null) return;
            // 暂停时一直显示三角形（提示点一下继续）；播放中显示两秒竖杠再隐去
            if (playIcon != null)
                playIcon.Visibility = playing ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            if (pauseIcon != null)
                pauseIcon.Visibility = playing ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            overlay.Visibility = System.Windows.Visibility.Visible;
            overlayHoldMs = playing ? 1200 : int.MaxValue;
        }

        void TogglePlay()
        {
            if (playing)
            {
                if (useDs) { if (ds != null) ds.Pause(); }
                else if (me != null) me.Pause();
                playing = false;
                SyncPlayState();
            }
            else
            {
                if (useDs) { if (ds != null) ds.Play(); }
                else if (me != null) me.Play();
                playing = true;
                SyncPlayState();
            }
        }

        void StopAndRewind()
        {
            if (useDs) { if (ds != null) ds.Stop(); }
            else if (me != null) { me.Stop(); try { me.Position = TimeSpan.Zero; } catch { } }
            playing = false;
            SyncPlayState();
            seek.Value = 0;
            UpdateTime();
        }

        void ApplyVolume()
        {
            if (useDs) { /* DirectShow 音量接口暂不使用 */ }
            else if (me != null) { try { me.Volume = vol.Value / 100.0; } catch { } }
        }

        void ApplySpeed()
        {
            double rate = 1.0;
            switch (speedBox.SelectedIndex)
            {
                case 0: rate = 0.5; break;
                case 1: rate = 0.75; break;
                case 2: rate = 1.0; break;
                case 3: rate = 1.25; break;
                case 4: rate = 1.5; break;
                case 5: rate = 2.0; break;
            }
            try
            {
                if (useDs) { if (ds != null) ds.SetRate(rate); }
                else if (me != null) me.SpeedRatio = rate;
            }
            catch { }
        }

        void Tick()
        {
            long len = DurationMs;
            long pos = PositionMs;
            if (len > 0 && !seekDragging)
            {
                int v = (int)Math.Min(1000, Math.Max(0, pos * 1000 / len));
                if (seek.Value != v) seek.Value = v;
            }
            UpdateTime();

            // 看门狗：DirectShow 能读出时长、却启动不了播放时（进度一直是 0），
            // 别让用户盯着一个黑屏卡死的窗口 —— 1.5 秒后自动交给系统默认播放器。
            if (useDs && ds != null && !dsPlaybackStalled)
            {
                if (ds.PositionMs > 0) { dsPlaybackStalled = true; }
                else
                {
                    dsZeroTicks += timer.Interval;
                    if (dsZeroTicks >= 1500)
                    {
                        dsPlaybackStalled = true;
                        HandOffToSystemPlayer();
                    }
                }
            }

            // 图标自动隐藏：播放中只在刚点过时显示一下；暂停时一直显示
            if (overlay != null && overlayHoldMs != int.MaxValue)
            {
                overlayHoldMs -= timer.Interval;
                if (overlayHoldMs <= 0)
                {
                    overlay.Visibility = System.Windows.Visibility.Collapsed;
                    overlayHoldMs = 0;
                }
            }

            if (playing && len > 0 && pos >= len - 400)
            {
                playing = false;
                SyncPlayState();
            }
        }

        void UpdateTime()
        {
            timeLabel.Text = Fmt(PositionMs) + " / " + Fmt(DurationMs);
        }

        static string Fmt(long ms)
        {
            if (ms < 0) ms = 0;
            long s = ms / 1000;
            if (s >= 3600) return string.Format("{0}:{1:00}:{2:00}", s / 3600, (s / 60) % 60, s % 60);
            return string.Format("{0:00}:{1:00}", s / 60, s % 60);
        }

        // ==================== 全屏 ====================

        bool fullScreen;
        Rectangle restoreBounds;
        FormBorderStyle restoreStyle;
        bool restoreTopMost;
        FormWindowState restoreState;

        void ToggleFullScreen()
        {
            if (!fullScreen)
            {
                restoreBounds = Bounds;
                restoreStyle = FormBorderStyle;
                restoreTopMost = TopMost;
                restoreState = WindowState;
                FormBorderStyle = FormBorderStyle.None;
                TopMost = true;
                WindowState = FormWindowState.Maximized;
                bottom.Visible = false;
                fullScreen = true;
            }
            else
            {
                FormBorderStyle = restoreStyle;
                TopMost = restoreTopMost;
                WindowState = restoreState;
                Bounds = restoreBounds;
                bottom.Visible = true;
                fullScreen = false;
            }
            if (useDs && ds != null) ds.ResizeVideo(screen.ClientSize);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (useDs && ds != null) ds.ResizeVideo(screen.ClientSize);
        }

        /// <summary>DirectShow 起不来播放时，交给系统默认播放器（至少能看/能听）</summary>
        void HandOffToSystemPlayer()
        {
            string target = !string.IsNullOrEmpty(currentPath) && File.Exists(currentPath) ? currentPath : openedPath;
            if (string.IsNullOrEmpty(target)) return;
            lblEngine.Text = "引擎：DirectShow 不可用，已转交系统播放器";
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
            catch { }
        }

        void CloseMedia()
        {
            failedOnce = false;
            playing = false;
            try { if (me != null) { me.Stop(); me.Close(); me.Source = null; } } catch { }
            if (ds != null) { try { ds.Dispose(); } catch { } ds = null; }
            if (!string.IsNullOrEmpty(tempFile))
            {
                for (int i = 0; i < 6; i++)
                {
                    try { if (File.Exists(tempFile)) File.Delete(tempFile); break; }
                    catch { System.Threading.Thread.Sleep(150); }
                }
                tempFile = null;
            }
        }
    }
}
