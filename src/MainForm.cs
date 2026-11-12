// MainForm.cs —— 主界面
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Collections.Generic;

namespace FipArc
{
    public sealed class MainForm : Form
    {
        TreeView tree;
        ListView list;
        SplitContainer split;
        // 文件夹路径 -> 该文件夹下的直接子项
        Dictionary<string, List<EntryInfo>> index;
        // 文件夹路径 -> 树节点
        Dictionary<string, TreeNode> folderNode;
        string currentFolder = "";
        bool draggingOut = false;   // 正在把自己解出来的东西拖出去
        EntryInfo selectedEntry;
        TextBox log;
        StatusStrip statusBar;
        ToolStripStatusLabel statusText;
        string currentArchive;
        string currentPassword = "";

        public MainForm(string[] initialFiles)
        {
            Text = "FipArc 压缩工具";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(940, 620);
            MinimumSize = new Size(700, 460);
            Font = new Font("Microsoft YaHei UI", 9f);
            Icon = LoadIcon();

            // ---------------- 菜单 ----------------
            MenuStrip menu = new MenuStrip();

            ToolStripMenuItem mFile = new ToolStripMenuItem(Lang.S("menu.file"));
            mFile.DropDownItems.Add("打开压缩包…", null, delegate { OpenArchiveDialog(); });
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add("退出", null, delegate { Close(); });
            menu.Items.Add(mFile);

            ToolStripMenuItem mComp = new ToolStripMenuItem(Lang.S("menu.pack"));
            AddItem(mComp, "压缩为 FIP（本软件专有）…", delegate { PackSelected(".fip"); });
            AddItem(mComp, "压缩为 ZIP…", delegate { PackSelected(".zip"); });
            AddItem(mComp, "压缩为 TAR…", delegate { PackSelected(".tar"); });
            AddItem(mComp, "压缩为 GZ（单文件）…", delegate { PackSelected(".gz"); });
            AddItem(mComp, "压缩为 TAR.GZ…", delegate { PackSelected(".tar.gz"); });
            mComp.DropDownItems.Add(new ToolStripSeparator());
            AddItem(mComp, "添加文件到当前压缩包…", delegate { AddToArchive(); });
            menu.Items.Add(mComp);

            ToolStripMenuItem mExt = new ToolStripMenuItem(Lang.S("menu.extract"));
            AddItem(mExt, "解压到…", delegate { ExtractTo(false); });
            AddItem(mExt, "解压到当前文件夹", delegate { ExtractTo(true); });
            AddItem(mExt, "查看压缩包内容", delegate { OpenArchiveDialog(); });
            menu.Items.Add(mExt);

#if !SE
            ToolStripMenuItem mConv = new ToolStripMenuItem(Lang.S("menu.convert"));
            AddItem(mConv, "转换为 FIP 格式（mp3→fmp / png→fpg / jpg→fjp / mp4→fp4 / mov→fov / txt→fxt）", delegate { ConvertToFip(); });
            AddItem(mConv, "从 FIP 格式还原为普通格式", delegate { ConvertFromFip(); });
            menu.Items.Add(mConv);
#endif

            ToolStripMenuItem mTool = new ToolStripMenuItem(Lang.S("menu.tools"));
            AddItem(mTool, "注册 / 修复右键菜单", delegate { RepairShell(); });
            AddItem(mTool, "刷新文件图标", delegate { Shell.RefreshShell(); Log("已请求刷新图标缓存"); });
            AddItem(mTool, "设为默认应用…", delegate
            {
                Shell.OpenDefaultAppsSettings();
                Log("已打开「默认应用」设置页：找到 FipArc，点进去可以把 播放器 / 看图 / 文本 设成本软件。");
            });
            AddItem(mTool, "清除缓存…", delegate { ClearCache(); });
            mTool.DropDownItems.Add(new ToolStripSeparator());

            // ---- 语言：界面上永远显示各语言自己的名字 ----
            ToolStripMenuItem mLang = new ToolStripMenuItem(Lang.S("menu.language"));
            for (int li = 0; li < Lang.Codes.Length; li++)
            {
                int idx = li;
                ToolStripMenuItem one = new ToolStripMenuItem(Lang.Names[idx]);
                if (idx == Lang.Current) one.Checked = true;
                one.Click += delegate
                {
                    if (idx == Lang.Current) return;
                    Lang.Current = idx;
                    Application.Restart();      // 重启一下让所有界面都换成新语言
                };
                mLang.DropDownItems.Add(one);
            }
            AddItem(mTool, Lang.S("menu.language"), delegate { });
            mTool.DropDownItems.RemoveAt(mTool.DropDownItems.Count - 1);
            mTool.DropDownItems.Add(mLang);

            mTool.DropDownItems.Add(new ToolStripSeparator());
            AddItem(mTool, "卸载本软件…", delegate { LaunchUninstall(); });
            menu.Items.Add(mTool);

            ToolStripMenuItem mHelp = new ToolStripMenuItem(Lang.S("menu.help"));
            AddItem(mHelp, "关于", delegate { ShowAbout(); });
            menu.Items.Add(mHelp);

            MainMenuStrip = menu;
            // 注意：Dock 是按添加的逆序生效的。
            // 想让菜单栏在最上面，必须让工具栏先加入、菜单栏后加入。

            // ---------------- 工具栏 ----------------
            ToolStrip ts = new ToolStrip();
            ts.GripStyle = ToolStripGripStyle.Hidden;
            ts.RenderMode = ToolStripRenderMode.System;
            AddTool(ts, "打开压缩包", delegate { OpenArchiveDialog(); });
            ts.Items.Add(new ToolStripSeparator());
            AddTool(ts, "压缩为 FIP", delegate { PackSelected(".fip"); });
            AddTool(ts, "解压到…", delegate { ExtractTo(false); });
#if !SE
            ts.Items.Add(new ToolStripSeparator());
            AddTool(ts, "转为 FIP", delegate { ConvertToFip(); });
            AddTool(ts, "还原格式", delegate { ConvertFromFip(); });
#endif
            // 播放器 / 图片查看器 / 文本查看器 不放在工具栏上：
            // 双击 .fmp/.fpg/.fxt 等文件会自动用对应的查看器打开。
            Controls.Add(ts);
            Controls.Add(menu);          // 菜单最后加入 => 停靠到最顶端

            // ---------------- 中间：左树 + 右列表（资源管理器布局） ----------------
            // 左树只放【文件夹】，右边用带列的列表显示当前文件夹里的内容。
            // 之前把所有文件都塞进树里，文件一多就变成一长条平铺列表、没有列也没法排序。
            split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Vertical;
            split.SplitterWidth = 5;

            tree = new TreeView();
            tree.Dock = DockStyle.Fill;
            tree.HideSelection = false;
            tree.ShowLines = true;
            tree.ShowRootLines = true;
            tree.ShowPlusMinus = true;
            tree.FullRowSelect = true;
            tree.ItemHeight = 22;
            tree.BorderStyle = BorderStyle.None;
            tree.ImageList = BuildTreeIcons();
            tree.ItemDrag += OnItemDrag;
            tree.AfterSelect += delegate { OnFolderSelected(); };
            split.Panel1.Controls.Add(tree);

            list = new ListView();
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.MultiSelect = false;
            list.GridLines = false;
            list.Columns.Add("名称", 260, HorizontalAlignment.Left);
            list.Columns.Add("原始大小", 100, HorizontalAlignment.Right);
            list.Columns.Add("压缩后", 100, HorizontalAlignment.Right);
            list.Columns.Add("类型", 130, HorizontalAlignment.Left);
            list.SmallImageList = tree.ImageList;
            list.DoubleClick += delegate { OpenSelectedItem(); };
            list.ItemDrag += OnItemDrag;
            list.SelectedIndexChanged += delegate { ShowSelectedInfo(); };

            // 右键菜单：拖拽不好使时的可靠兜底
            ContextMenuStrip lvMenu = new ContextMenuStrip();
            lvMenu.Items.Add("解压选中项到桌面", null, delegate { ExtractSelectedTo(true); });
            lvMenu.Items.Add("解压选中项到…", null, delegate { ExtractSelectedTo(false); });
            lvMenu.Items.Add(new ToolStripSeparator());
            lvMenu.Items.Add("用内置查看器打开", null, delegate { OpenSelectedItem(); });
            list.ContextMenuStrip = lvMenu;
            list.MouseDown += delegate(object s, MouseEventArgs me)
            {
                if (me.Button == MouseButtons.Right)
                {
                    ListViewItem hit = list.GetItemAt(me.X, me.Y);
                    if (hit != null) { hit.Selected = true; list.FocusedItem = hit; }
                }
            };
            list.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { OpenSelectedItem(); e.Handled = true; }
                else if (e.KeyCode == Keys.Back) { GoUp(); e.Handled = true; }
            };
            split.Panel2.Controls.Add(list);

            Controls.Add(split);

            log = new TextBox();
            log.Dock = DockStyle.Bottom;
            log.Height = 120;
            log.Multiline = true;
            log.ScrollBars = ScrollBars.Vertical;
            log.ReadOnly = true;
            log.BackColor = Color.FromArgb(250, 250, 250);
            log.Font = new Font("Consolas", 9f);
            Controls.Add(log);

            // ---------------- 状态栏 ----------------
            statusBar = new StatusStrip();
            statusText = new ToolStripStatusLabel("就绪");
            statusBar.Items.Add(statusText);
            Controls.Add(statusBar);

            // 关键：Dock 是按控件集合的【倒序】依次计算的。
            // Fill 控件必须放在 index 0（最后被处理），才能拿到"扣掉四周之后"的真正剩余空间。
            split.BringToFront();   // 必须让 split 最后停靠，否则顶部被工具栏盖住

            // ---------------- 拖放 ----------------
            AllowDrop = true;
            DragEnter += delegate(object s, DragEventArgs e)
            {
                if (draggingOut) { e.Effect = DragDropEffects.None; return; }
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            DragDrop += delegate(object s, DragEventArgs e)
            {
                // 自己拖出去的东西又落回自己窗口 —— 忽略，绝不能当成"拖文件进来压缩"
                if (draggingOut) return;

                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files == null || files.Length == 0) return;

                // 再兜一层：来自本软件拖拽临时目录的，也忽略
                string dragRoot = Path.Combine(Path.GetTempPath(), "FipArcDrag");
                if (files[0].StartsWith(dragRoot, StringComparison.OrdinalIgnoreCase)) return;

                if (files.Length == 1 && !Directory.Exists(files[0]) && ArchiveKind.Detect(files[0]) != ArchiveKind.Other)
                    LoadArchive(files[0]);
                else
                    PackPaths(files, ".fip");
            };

            Log("FipArc 已启动。");
            Log("提示：把文件或文件夹拖进窗口即可压缩；把压缩包拖进来即可查看内容。");
            Log("");

            if (initialFiles != null && initialFiles.Length > 0)
            {
                // 不能在构造函数里 BeginInvoke（句柄还没创建），改挂 Shown 事件
                string first = initialFiles[0];
                Shown += delegate { LoadArchive(first); };
            }
        }

        Icon LoadIcon()
        {
            try
            {
                string p = Path.Combine(Application.StartupPath, "app.ico");
                if (File.Exists(p)) return new Icon(p);
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { return null; }
        }

        void AddItem(ToolStripMenuItem parent, string text, EventHandler h)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.Click += h;
            parent.DropDownItems.Add(it);
        }

        void AddTool(ToolStrip ts, string text, EventHandler h)
        {
            ToolStripButton b = new ToolStripButton(text);
            b.DisplayStyle = ToolStripItemDisplayStyle.Text;
            b.Click += h;
            ts.Items.Add(b);
        }

        void Log(string s)
        {
            log.AppendText(s + Environment.NewLine);
        }

        void SetStatus(string s)
        {
            statusText.Text = s;
        }

        // ==================== 打开压缩包 ====================
        void OpenArchiveDialog()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "打开压缩包";
                dlg.Filter = "压缩包|*.fip;*.zip;*.tar;*.gz;*.tgz;*.tar.gz;*.7z;*.rar;*.iso;*.cab;*.xz;*.bz2;*.lzh;*.jar|所有文件|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadArchive(dlg.FileName);
            }
        }

        public void LoadArchive(string path)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                SetStatus("正在读取 " + Path.GetFileName(path) + " …");
                tree.BeginUpdate();
                tree.Nodes.Clear();

                int kind = ArchiveKind.Detect(path);
                List<EntryInfo> entries = null;
                string usedPwd = "";
                bool okOpen = RunWithPassword(delegate(string pw)
                {
                    IExtractor ex = Archive.Open(path);
                    ex.SetPassword(pw);
                    entries = ex.List();
                    usedPwd = pw;
                }, path);
                if (!okOpen || entries == null)
                {
                    tree.EndUpdate();
                    Log("[取消] 未打开 " + Path.GetFileName(path) + "（密码问题或用户取消）");
                    return;
                }
                currentPassword = usedPwd;

                long total = 0;
                int fileCount = 0;
                foreach (EntryInfo e in entries)
                {
                    if (e.Size > 0) total += e.Size;
                    if (!e.IsDir && !(e.Name ?? "").EndsWith("/")) fileCount++;
                }

                BuildTree(path, entries);
                tree.EndUpdate();

                // 文件夹数量按树里的真实节点统计：ZIP 不一定有显式目录条目
                int dirCount = (folderNode == null ? 0 : folderNode.Count) - 1;   // 去掉根
                if (dirCount < 0) dirCount = 0;

                currentArchive = path;
                Text = "FipArc 压缩工具 — " + Path.GetFileName(path) +
                       "  [" + ArchiveKind.Describe(kind) + "]" +
                       (Archive.NeedsPassword(path) ? "  🔒" : "");
                Log(string.Format("已打开 {0}（{1}），共 {2} 项（{3} 个文件夹 / {4} 个文件），解压后约 {5}",
                    Path.GetFileName(path), ArchiveKind.Describe(kind), entries.Count,
                    dirCount, fileCount, FormatSize(total)));
                SetStatus(string.Format("{0} 项", entries.Count));
            }
            catch (Exception ex2)
            {
                MessageBox.Show(this,
                    "无法打开：" + ex2.Message + Environment.NewLine + Environment.NewLine +
                    (ArchiveKind.Detect(path) == ArchiveKind.SevenZip || ArchiveKind.Detect(path) == ArchiveKind.Rar
                        ? "提示：7z / rar 需要安装 7-Zip 或 WinRAR 才能解压。"
                        : ""),
                    "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Log("[错误] " + ex2.Message);
            }
            finally { Cursor = Cursors.Default; }
        }

        // ==================== 树状目录 ====================
        /// <summary>文件夹 / 文件两个小图标（自己画，不依赖任何资源文件）</summary>
        ImageList BuildTreeIcons()
        {
            ImageList il = new ImageList();
            il.ImageSize = new Size(16, 16);
            il.ColorDepth = ColorDepth.Depth32Bit;

            // 文件夹图标
            using (Bitmap b = new Bitmap(16, 16))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (SolidBrush back = new SolidBrush(Color.FromArgb(255, 200, 80)))
                    using (SolidBrush front = new SolidBrush(Color.FromArgb(255, 226, 130)))
                    using (Pen line = new Pen(Color.FromArgb(190, 140, 30)))
                    {
                        g.FillRectangle(back, 1, 3, 6, 3);          // 文件夹的"标签"
                        g.FillRectangle(front, 1, 5, 14, 9);
                        g.DrawRectangle(line, 1, 5, 13, 8);
                    }
                }
                il.Images.Add((Image)b.Clone());
            }

            // 文件图标（白纸 + 折角）
            using (Bitmap b = new Bitmap(16, 16))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.Clear(Color.Transparent);
                    using (SolidBrush paper = new SolidBrush(Color.White))
                    using (Pen line = new Pen(Color.FromArgb(120, 120, 120)))
                    {
                        g.FillPolygon(paper, new Point[] {
                            new Point(3,1), new Point(10,1), new Point(13,4), new Point(13,14), new Point(3,14) });
                        g.DrawPolygon(line, new Point[] {
                            new Point(3,1), new Point(10,1), new Point(13,4), new Point(13,14), new Point(3,14) });
                        g.DrawLine(line, 10, 1, 10, 4);
                        g.DrawLine(line, 10, 4, 13, 4);
                    }
                }
                il.Images.Add((Image)b.Clone());
            }

            return il;
        }

        /// <summary>把条目列表按路径建成文件夹树</summary>
        /// <summary>把条目按文件夹分组，并只在树里放文件夹</summary>
        void BuildTree(string archivePath, List<EntryInfo> entries)
        {
            index = new Dictionary<string, List<EntryInfo>>(StringComparer.OrdinalIgnoreCase);
            folderNode = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
            index[""] = new List<EntryInfo>();

            TreeNode root = tree.Nodes.Add(Path.GetFileName(archivePath));
            root.ImageIndex = 0;
            root.SelectedImageIndex = 0;
            folderNode[""] = root;

            // 第一遍：把所有文件夹路径建出来（含中间层）
            List<string> folders = new List<string>();
            foreach (EntryInfo e in entries)
            {
                string name = (e.Name ?? "").Replace('\\', '/');
                if (name.Length == 0) continue;
                string[] parts = name.Split('/') ;
                string cur = "";
                int last = e.IsDir || name.EndsWith("/") ? parts.Length : parts.Length - 1;
                for (int i = 0; i < last; i++)
                {
                    if (parts[i].Length == 0) continue;
                    cur = cur.Length == 0 ? parts[i] : cur + "/" + parts[i];
                    if (!folders.Contains(cur)) folders.Add(cur);
                }
            }
            folders.Sort(StringComparer.OrdinalIgnoreCase);

            // 建树：只放文件夹，按层级挂
            foreach (string f in folders)
            {
                int slash = f.LastIndexOf('/');
                string parent = slash < 0 ? "" : f.Substring(0, slash);
                string leaf = slash < 0 ? f : f.Substring(slash + 1);
                TreeNode pn;
                if (!folderNode.TryGetValue(parent, out pn)) pn = root;
                TreeNode n = pn.Nodes.Add(leaf);
                n.ImageIndex = 0;
                n.SelectedImageIndex = 0;
                folderNode[f] = n;
            }

            // 第二遍：把文件挂到所属文件夹的列表里
            foreach (EntryInfo e in entries)
            {
                string name = (e.Name ?? "").Replace('\\', '/');
                if (name.Length == 0) continue;
                if (e.IsDir || name.EndsWith("/")) continue;      // 目录已在树里
                int slash = name.LastIndexOf('/');
                string folder = slash < 0 ? "" : name.Substring(0, slash);
                List<EntryInfo> bucket;
                if (!index.TryGetValue(folder, out bucket))
                {
                    bucket = new List<EntryInfo>();
                    index[folder] = bucket;
                }
                bucket.Add(e);
            }

            root.Expand();
            tree.SelectedNode = root;       // 触发 OnFolderSelected
        }

        /// <summary>树上选了某个文件夹 -> 右侧列出它的内容</summary>
        void OnFolderSelected()
        {
            if (tree.SelectedNode == null || index == null) return;
            string path = "";
            foreach (KeyValuePair<string, TreeNode> kv in folderNode)
                if (kv.Value == tree.SelectedNode) { path = kv.Key; break; }
            currentFolder = path;
            FillList(path);
        }

        void FillList(string folder)
        {
            list.BeginUpdate();
            list.Items.Clear();
            selectedEntry = null;

            // 先列子文件夹
            List<string> subs = new List<string>();
            foreach (string key in index.Keys)
            {
                if (key.Length == 0) continue;
                int slash = key.LastIndexOf('/');
                string parent = slash < 0 ? "" : key.Substring(0, slash);
                if (string.Equals(parent, folder ?? "", StringComparison.OrdinalIgnoreCase))
                    subs.Add(key);
            }
            subs.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string s in subs)
            {
                string leaf = s.Substring(s.LastIndexOf('/') + 1);
                int count = 0;
                List<EntryInfo> b;
                if (index.TryGetValue(s, out b)) count += b.Count;
                ListViewItem it = new ListViewItem(leaf);
                it.ImageIndex = 0;
                it.SubItems.Add("");
                it.SubItems.Add("");
                it.SubItems.Add("文件夹 (" + count + ")");
                it.Tag = s;                    // 文件夹用 string 标记
                list.Items.Add(it);
            }

            // 再列文件
            List<EntryInfo> files;
            if (index.TryGetValue(folder ?? "", out files))
            {
                files.Sort(delegate(EntryInfo a, EntryInfo c)
                { return string.Compare(a.Name, c.Name, StringComparison.OrdinalIgnoreCase); });
                foreach (EntryInfo e in files)
                {
                    string leaf = e.Name;
                    int slash = leaf.LastIndexOf('/');
                    if (slash >= 0) leaf = leaf.Substring(slash + 1);
                    ListViewItem it = new ListViewItem(leaf);
                    it.ImageIndex = 1;
                    it.SubItems.Add(e.Size >= 0 ? FormatSize(e.Size) : "—");
                    it.SubItems.Add(e.PackedSize > 0 ? FormatSize(e.PackedSize) : "—");
                    it.SubItems.Add(e.IsDir ? "文件夹" : (Path.GetExtension(leaf).Length > 0 ? Path.GetExtension(leaf).Substring(1).ToUpperInvariant() + " 文件" : "文件"));
                    it.Tag = e;
                    list.Items.Add(it);
                }
            }

            list.EndUpdate();
            SetStatus(string.Format("[{0}] {1} 项", folder.Length == 0 ? "根目录" : folder, list.Items.Count));
        }

        /// <summary>树节点反查文件夹路径</summary>
        string PathOfNode(TreeNode node)
        {
            if (node == null || folderNode == null) return null;
            foreach (KeyValuePair<string, TreeNode> kv in folderNode)
                if (kv.Value == node) return kv.Key;
            return null;
        }

        void GoUp()
        {
            if (currentFolder == null || currentFolder.Length == 0) return;
            int slash = currentFolder.LastIndexOf('/');
            string parent = slash < 0 ? "" : currentFolder.Substring(0, slash);
            TreeNode n;
            if (folderNode != null && folderNode.TryGetValue(parent, out n)) tree.SelectedNode = n;
        }

        void ShowSelectedInfo()
        {
            if (list.SelectedItems.Count == 0) { selectedEntry = null; return; }
            object tag = list.SelectedItems[0].Tag;
            selectedEntry = tag as EntryInfo;
            if (selectedEntry == null)
            {
                SetStatus("文件夹：" + tag);
                return;
            }
            SetStatus(string.Format("{0}    原始 {1}    压缩后 {2}",
                selectedEntry.Name,
                selectedEntry.Size >= 0 ? FormatSize(selectedEntry.Size) : "—",
                selectedEntry.PackedSize > 0 ? FormatSize(selectedEntry.PackedSize) : "—"));
        }
        void OpenSelectedItem()
        {
            if (currentArchive == null || list.SelectedItems.Count == 0) return;
            object tag = list.SelectedItems[0].Tag;

            // 双击文件夹：跳到那个文件夹
            string folder = tag as string;
            if (folder != null)
            {
                TreeNode n;
                if (folderNode != null && folderNode.TryGetValue(folder, out n))
                {
                    n.EnsureVisible();
                    tree.SelectedNode = n;
                }
                return;
            }

            EntryInfo e = tag as EntryInfo;
            if (e == null) return;
            string name = e.Name;
            try
            {
                string tmpDir = Path.Combine(Path.GetTempPath(), "FipArcPreview");
                Directory.CreateDirectory(tmpDir);
                string dest = Path.Combine(tmpDir, Path.GetFileName(name));
                string tmpAll = Path.Combine(tmpDir, "all_" + Guid.NewGuid().ToString("N"));
                bool ok = RunWithPassword(delegate(string pw)
                {
                    IExtractor ex = Archive.Open(currentArchive);
                    ex.SetPassword(pw);
                    ex.ExtractAll(tmpAll, null);
                }, currentArchive);
                if (!ok) return;

                string src = Path.Combine(tmpAll, name.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(src)) { Log("[提示] 没找到解出来的文件: " + name); return; }
                File.Copy(src, dest, true);
                OpenWithBuiltin(dest);
            }
            catch (Exception ex2) { Log("[错误] " + ex2.Message); }
        }

        // ==================== 从列表里拖出文件 ====================
        /// <summary>
        /// 把选中的文件拖到资源管理器 = 直接解压出来。
        /// 有密码时优先复用打开压缩包时输入的那个，避免拖拽中途弹模态框打断。
        /// </summary>
        void OnItemDrag(object sender, ItemDragEventArgs e)
        {
            if (currentArchive == null) return;
            EntryInfo ent = null;
            string folder = null;

            ListViewItem lvi = e.Item as ListViewItem;
            if (lvi != null)
            {
                ent = lvi.Tag as EntryInfo;
                folder = lvi.Tag as string;
            }
            else
            {
                TreeNode node = e.Item as TreeNode;
                if (node != null) folder = PathOfNode(node);
            }

            Log("[拖出] 开始: " + (ent != null ? ent.Name : folder));

            // 根节点（整个压缩包）不拖
            if (ent == null && (folder == null || folder.Length == 0)) { Log("[拖出] 取消：根节点不拖"); return; }

            string pwd = currentPassword ?? "";
            try
            {
                if (Archive.NeedsPassword(currentArchive) && pwd.Length == 0)
                {
                    string ask = PasswordForm.AskOpen(this, Path.GetFileName(currentArchive),
                        "拖出文件需要先解开压缩包。");
                    if (ask == null) return;
                    pwd = ask;
                    currentPassword = ask;
                }
            }
            catch { }

            string tempDir = Path.Combine(Path.GetTempPath(), "FipArcDrag", Guid.NewGuid().ToString("N"));
            string produced = null;
            Cursor prev = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                Directory.CreateDirectory(tempDir);
                IExtractor ex = Archive.Open(currentArchive);
                ex.SetPassword(pwd);
                produced = ent != null
                    ? ex.ExtractOne(ent.Name, tempDir)
                    : ex.ExtractFolder(folder, tempDir);
            }
            catch (WrongPasswordException)
            {
                MessageBox.Show(this, "密码不正确。", "无法拖出", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            catch (Exception ex2)
            {
                Log("[错误] 拖出失败: " + ex2.Message);
                MessageBox.Show(this, ex2.Message, "无法拖出", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            finally { Cursor = prev; }

            if (produced == null || (!File.Exists(produced) && !Directory.Exists(produced)))
            {
                Log("[提示] 没能解出 " + (ent != null ? ent.Name : folder));
                return;
            }

            Log("拖出: " + (ent != null ? ent.Name : folder));
            DataObject data = new DataObject();
            data.SetData(DataFormats.FileDrop, new string[] { produced });

            // 拖出过程中必须关掉本窗口的拖放接收，
            // 否则鼠标在自己窗口里松手会被当成"拖文件进来"，弹出压缩/密码框。
            bool prevDrop = AllowDrop;
            draggingOut = true;
            AllowDrop = false;
            DragDropEffects res = DragDropEffects.None;
            try { res = DoDragDrop(data, DragDropEffects.Copy); }
            catch (Exception ex3) { Log("[拖出] DoDragDrop 异常: " + ex3.Message); }
            finally { AllowDrop = prevDrop; draggingOut = false; }
            Log("[拖出] 松手结果: " + res + (res == DragDropEffects.None
                ? "（没有落在能接收的地方：请在资源管理器/桌面上松手）" : "（已放到目标位置）"));
        }

        public void OpenWithBuiltin(string path)
        {
            if (Playable.IsImage(path)) { ImageViewerForm f = new ImageViewerForm(); f.Show(this); f.LoadFile(path); }
            else if (Playable.IsText(path)) { TextViewerForm f = new TextViewerForm(); f.Show(this); f.LoadFile(path); }
            // 音视频不再内置播放，交给系统默认播放器
            else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        // ==================== 压缩 ====================
        void PackSelected(string ext)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择要压缩的文件（可多选）";
                dlg.Multiselect = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                PackPaths(dlg.FileNames, ext);
            }
        }

        public void PackPaths(string[] paths, string ext)
        {
            if (paths == null || paths.Length == 0) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                string first = paths[0];
                string baseDir = Directory.Exists(first) ? Path.GetDirectoryName(first.TrimEnd('\\')) : Path.GetDirectoryName(first);
                string nameBase = Directory.Exists(first) ? Path.GetFileName(first.TrimEnd('\\')) : Path.GetFileNameWithoutExtension(first);
                string outPath = Path.Combine(baseDir, nameBase + ext);
                if (paths.Length > 1) outPath = Path.Combine(baseDir, Path.GetFileName(baseDir) + ext);

                SetStatus("正在压缩…");
                Application.DoEvents();

                // .fip / .zip / .7z 问密码；f 格式转换不问
                string pwd = null;
                if (ext == ".fip" || ext == ".zip" || ext == ".7z")
                {
                    pwd = PasswordForm.AskNew(this, nameBase + ext);
                    if (pwd == null) return;      // 用户取消
                }

                byte[] data;
                if (ext == ".fip")
                    data = Archive.CreateFip(paths, baseDir, nameBase, pwd);
                else if (ext == ".zip")
                    data = Archive.CreateZip(paths, baseDir, pwd);
                else if (ext == ".7z")
                    data = Archive.Create7z(paths, baseDir, pwd);
                else if (ext == ".tar")
                    data = Archive.CreateTar(paths, baseDir);
                else if (ext == ".gz")
                {
                    if (paths.Length != 1 || Directory.Exists(paths[0]))
                        throw new InvalidOperationException("GZ 只能压缩单个文件");
                    data = Archive.CreateGz(paths[0]);
                }
                else if (ext == ".tar.gz")
                {
                    byte[] tar = Archive.CreateTar(paths, baseDir);
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (System.IO.Compression.GZipStream gz = new System.IO.Compression.GZipStream(
                            ms, System.IO.Compression.CompressionMode.Compress, true))
                            gz.Write(tar, 0, tar.Length);
                        data = ms.ToArray();
                    }
                }
                else throw new NotSupportedException("不支持的压缩格式: " + ext);

                outPath = Convert2.UniquePath(outPath);
                File.WriteAllBytes(outPath, data);
                Log(string.Format("已创建 {0}  ({1})", Path.GetFileName(outPath), FormatSize(data.Length)));
                SetStatus("完成");
                LoadArchive(outPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "压缩失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Log("[错误] " + ex.Message);
            }
            finally { Cursor = Cursors.Default; }
        }

        void AddToArchive()
        {
            if (currentArchive == null) { Log("[提示] 请先打开一个压缩包"); return; }
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "要添加的文件";
                dlg.Multiselect = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    // 解出原内容 + 追加
                    string tmp = Path.Combine(Path.GetTempPath(), "FipArcAdd_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tmp);
                    Archive.Open(currentArchive).ExtractAll(tmp, null);
                    foreach (string f in dlg.FileNames)
                        File.Copy(f, Path.Combine(tmp, Path.GetFileName(f)), true);

                    string ext = Path.GetExtension(currentArchive).ToLowerInvariant();
                    if (currentArchive.ToLowerInvariant().EndsWith(".tar.gz")) ext = ".tar.gz";
                    byte[] data;
                    if (ext == ".fip") data = Archive.CreateFip(new string[] { tmp }, tmp, Path.GetFileNameWithoutExtension(currentArchive));
                    else if (ext == ".zip") data = Archive.CreateZip(new string[] { tmp }, tmp);
                    else if (ext == ".tar") data = Archive.CreateTar(new string[] { tmp }, tmp);
                    else throw new NotSupportedException("这个格式暂不支持追加");
                    File.WriteAllBytes(currentArchive, data);
                    LoadArchive(currentArchive);
                    Log("已追加 " + dlg.FileNames.Length + " 个文件");
                    try { Directory.Delete(tmp, true); } catch { }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "追加失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        // ==================== 解压 ====================
        /// <summary>把当前选中的条目（文件或文件夹）解出来放到桌面或指定目录</summary>
        void ExtractSelectedTo(bool desktop)
        {
            if (currentArchive == null || list.SelectedItems.Count == 0)
            {
                Log("[提示] 请先在右边列表里选中一个文件或文件夹");
                return;
            }
            object tag = list.SelectedItems[0].Tag;
            EntryInfo ent = tag as EntryInfo;
            string folder = tag as string;
            if (ent == null && string.IsNullOrEmpty(folder)) return;

            string destRoot;
            if (desktop) destRoot = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            else
            {
                using (FolderBrowserDialog fb = new FolderBrowserDialog())
                {
                    fb.Description = "解压到哪个文件夹？";
                    fb.SelectedPath = Path.GetDirectoryName(Application.ExecutablePath);
                    if (fb.ShowDialog(this) != DialogResult.OK) return;
                    destRoot = fb.SelectedPath;
                }
            }

            string pwd = currentPassword ?? "";
            Cursor prev = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                // 需要密码就先问（复用 RunWithPassword 的重试逻辑）
                bool ok = RunWithPassword(delegate(string pw)
                {
                    Archive.ClearErrors();
                    IExtractor ex = Archive.Open(currentArchive);
                    ex.SetPassword(pw);
                    if (ent != null) ex.ExtractOne(ent.Name, destRoot);
                    else ex.ExtractFolder(folder, destRoot);
                }, currentArchive);
                if (!ok) { Log("[取消] 未解出"); return; }

                if (Archive.LastErrors.Count > 0) { ShowExtractErrors(); return; }

                Log("已解出「" + (ent != null ? ent.Name : folder) + "」到 " + destRoot);
                SetStatus("已解出");
                try { Process.Start("explorer.exe", "\"" + destRoot + "\""); } catch { }
            }
            catch (Exception ex2)
            {
                Log("[错误] 解出失败: " + ex2.Message);
                MessageBox.Show(this, ex2.Message, "解出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { Cursor = prev; }
        }

        /// <summary>把解压失败的条目汇总告诉用户（而不是抛一句没上下文的异常）</summary>
        void ShowExtractErrors()
        {
            int n = Archive.LastErrors.Count;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("有 " + n + " 个项目没能解出来：");
            sb.AppendLine();
            int shown = 0;
            foreach (string e in Archive.LastErrors)
            {
                if (shown >= 8) { sb.AppendLine("    …还有 " + (n - 8) + " 项"); break; }
                sb.AppendLine("  · " + e);
                shown++;
            }
            sb.AppendLine();
            sb.AppendLine("常见原因：目标文件只读 / 正被其它程序占用 /");
            sb.AppendLine("被杀毒软件（360、Windows Defender）拦截。");
            sb.AppendLine("把解压目录加入安全软件白名单后重试即可。");
            MessageBox.Show(this, sb.ToString(), "部分文件解压失败",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Log("[警告] " + n + " 个项目解压失败，详见弹窗");
        }

        void ExtractTo(bool sameDir)
        {
            string archive = currentArchive;
            if (archive == null)
            {
                using (OpenFileDialog dlg = new OpenFileDialog())
                {
                    dlg.Filter = "压缩包|*.fip;*.zip;*.tar;*.gz;*.tgz;*.tar.gz;*.7z;*.rar;*.iso;*.cab;*.xz;*.bz2;*.lzh;*.jar|所有文件|*.*";
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    archive = dlg.FileName;
                }
            }
            string dest;
            if (sameDir)
                dest = Path.Combine(Path.GetDirectoryName(archive), Path.GetFileNameWithoutExtension(archive));
            else
            {
                using (FolderBrowserDialog fb = new FolderBrowserDialog())
                {
                    fb.Description = "解压到哪里？";
                    fb.SelectedPath = Path.GetDirectoryName(archive);
                    if (fb.ShowDialog(this) != DialogResult.OK) return;
                    dest = Path.Combine(fb.SelectedPath, Path.GetFileNameWithoutExtension(archive));
                }
            }
            try
            {
                Cursor = Cursors.WaitCursor;
                SetStatus("正在解压…");
                dest = Convert2.UniquePath(dest);
                Directory.CreateDirectory(dest);
                int count = 0;
                string destFinal = dest;
                bool ok = RunWithPassword(delegate(string pw)
                {
                    IExtractor ex = Archive.Open(archive);
                    ex.SetPassword(pw);
                    count = 0;
                    ex.ExtractAll(destFinal, delegate(int n, string name)
                    {
                        count = n;
                        if (n % 8 == 0) { SetStatus("正在解压 " + name); Application.DoEvents(); }
                    });
                }, archive);

                if (!ok) { Log("[取消] 解压未完成（密码问题或用户取消）"); return; }

                Log(string.Format("已解压 {0} 个文件到 {1}", count, dest));
                SetStatus("解压完成");

                // 有文件写不进去时不要整包失败，列出来让用户知道是哪些
                if (Archive.LastErrors.Count > 0) { ShowExtractErrors(); return; }
                if (MessageBox.Show(this, "解压完成，是否打开目录？", "完成",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    Process.Start("explorer.exe", "\"" + dest + "\"");
            }
            catch (Exception ex2)
            {
                MessageBox.Show(this, ex2.Message, "解压失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Log("[错误] " + ex2.Message);
            }
            finally { Cursor = Cursors.Default; }
        }

        // ==================== 转换 ====================
        void ConvertToFip()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择要转换的文件";
                dlg.Multiselect = true;
                dlg.Filter = "可转换文件|*.mp3;*.png;*.jpg;*.jpeg;*.mp4;*.mov;*.txt|所有文件|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                // 不设密码：f 格式用内置密钥加密，只有本软件能解开
                string pwd = "";
                int okCount = 0;
                StringBuilder errs = new StringBuilder();
                foreach (string f in dlg.FileNames)
                {
                    try
                    {
                        string outp = Convert2.ToFip(f, pwd);
                        Log("已转换 " + Path.GetFileName(f) + "  ->  " + Path.GetFileName(outp));
                        okCount++;
                    }
                    catch (Exception ex) { errs.AppendLine(Path.GetFileName(f) + ": " + ex.Message); }
                }
                if (errs.Length > 0)
                    MessageBox.Show(this, errs.ToString(), "部分文件未转换", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("转换完成 " + okCount + " 个");
            }
        }

        void ConvertFromFip()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择要还原的 FIP 文件";
                dlg.Multiselect = true;
                dlg.Filter = "FIP 专有格式|*.fmp;*.fpg;*.fjp;*.fp4;*.fov;*.fxt|所有文件|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                int okCount = 0;
                StringBuilder errs = new StringBuilder();
                foreach (string f in dlg.FileNames)
                {
                    try
                    {
                        string src = f;
                        string outp = null;
                        bool okOne = RunWithPassword(delegate(string pw) { outp = Convert2.FromFip(src, pw); }, src);
                        if (!okOne) { errs.AppendLine(Path.GetFileName(f) + ": 已取消或密码错误"); continue; }
                        Log("已还原 " + Path.GetFileName(f) + "  ->  " + Path.GetFileName(outp));
                        okCount++;
                    }
                    catch (Exception ex) { errs.AppendLine(Path.GetFileName(f) + ": " + ex.Message); }
                }
                if (errs.Length > 0)
                    MessageBox.Show(this, errs.ToString(), "部分文件未还原", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("还原完成 " + okCount + " 个");
            }
        }

        // ==================== 工具 ====================

        void ClearCache()
        {
            CacheInfo info;
            try { info = Cache.Scan(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "清除缓存"); return; }

            string msg = "将清除：" + Environment.NewLine +
                         "    · 界面上的日志文字" + Environment.NewLine +
                         "    · 拖拽 / 预览 / 播放产生的临时文件" + Environment.NewLine +
                         "    · 安装 / 卸载的诊断日志" + Environment.NewLine +
                         Environment.NewLine +
                         string.Format("找到 {0} 个文件 / {1} 个目录，共约 {2}。",
                                       info.FileCount, info.DirCount, Cache.Size(info.Bytes)) +
                         Environment.NewLine + Environment.NewLine +
                         "确定清除吗？";
            if (MessageBox.Show(this, msg, "清除缓存",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            log.Clear();
            int failed = Cache.Clear(delegate(string s) { Log(s); });
            if (failed > 0) Log("（其中 " + failed + " 项正被占用，已尽力清理内部文件）");
            Log("");
            Log(failed == 0
                ? "缓存已清空，释放约 " + Cache.Size(info.Bytes) + "。"
                : "缓存已清理，释放约 " + Cache.Size(info.Bytes) + "，" + failed + " 项未能完全删除。");
            SetStatus("缓存已清除，释放 " + Cache.Size(info.Bytes));
        }

        void RepairShell()
        {
            try
            {
                string icon = Path.Combine(Application.StartupPath, "app.ico");
                if (!File.Exists(icon)) icon = Application.ExecutablePath;
                Shell.RegisterContextMenu(Application.ExecutablePath, icon);
                long kb = 0;
                try
                {
                    foreach (string f in Directory.GetFiles(Application.StartupPath))
                        kb += new FileInfo(f).Length;
                    kb = kb / 1024 + 1;
                }
                catch { kb = 512; }
                Shell.RegisterUninstall(Application.ExecutablePath, Application.StartupPath,
                    Application.ProductVersion, kb);
                Shell.RefreshShell();
                Log("右键菜单与图标关联已重新注册。");
                MessageBox.Show(this, "右键菜单和文件图标已重新注册。", "完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "注册失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void LaunchUninstall()
        {
            string setup = Path.Combine(Application.StartupPath, "Uninstall.exe");
            if (!File.Exists(setup))
            {
                MessageBox.Show(this, "找不到卸载程序 Uninstall.exe", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Process.Start(setup);
            Close();
        }

        void ShowAbout()
        {
            MessageBox.Show(this,
                "FipArc 压缩工具\n\n" +
                "· 右键菜单直接压缩 / 解压\n" +
                "· 支持 ZIP / TAR / GZ / TAR.GZ 的读取，ZIP / TAR / GZ / FIP 的创建\n" +
                "· 专有格式 .fip —— 只有本软件能解开\n" +
                "· 格式互转：mp3↔fmp  png↔fpg  jpg↔fjp  mp4↔fp4  mov↔fov  txt↔fxt\n" +
                "· 内置媒体播放器 / 图片查看器 / 文本查看器\n\n" +
                "版本 " + Application.ProductVersion,
                "关于 FipArc", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ==================== 密码处理 ====================
        /// <summary>
        /// 执行一个需要密码的操作：密码保护的文件会弹框询问，密码错误最多重试 3 次。
        /// 返回 false 表示用户取消或密码一直不对。
        /// </summary>
        bool RunWithPassword(Action<string> action, string path)
        {
            string pwd = "";
            bool needPwd = false;
            try { needPwd = Archive.NeedsPassword(path); }
            catch { }

            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (needPwd)
                {
                    pwd = PasswordForm.AskOpen(this, Path.GetFileName(path),
                        attempt > 0 ? "密码不正确，请重新输入。" : "");
                    if (pwd == null) return false;
                }
                try
                {
                    action(pwd);
                    return true;
                }
                catch (WrongPasswordException)
                {
                    needPwd = true;
                    continue;
                }
                catch (PasswordRequiredException)
                {
                    // 事先没检测出来（比如数据加密但文件名没加密），这里补问一次
                    needPwd = true;
                    continue;
                }
            }
            MessageBox.Show(this, "密码连续 3 次不正确，已取消。", "无法打开",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        // ==================== 工具方法 ====================
        public static string FormatSize(long bytes)
        {
            if (bytes < 0) return "—";
            if (bytes < 1024) return bytes + " B";
            double v = bytes;
            string[] u = { "KB", "MB", "GB", "TB" };
            int i = -1;
            do { v /= 1024.0; i++; } while (v >= 1024 && i < u.Length - 1);
            return string.Format("{0:0.##} {1}", v, u[i]);
        }
    }
}
