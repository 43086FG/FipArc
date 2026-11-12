// UninstallMain.cs —— 专门的卸载程序入口（Uninstall.exe）
//
// 和 FipArcSetup.exe 分开：
//   · 用户卸载时只需要一个干净的 Uninstall.exe，不用去点安装程序
//   · 控制面板「卸载」按钮直接调这个
//   · 代码复用 Setup.cs 里同一套 Installer / UninstallForm，逻辑不会走偏
//
// 用法：
//   Uninstall.exe           卸载向导
//   Uninstall.exe /quiet    静默卸载
using System;
using System.Reflection;
using System.Windows.Forms;

namespace FipArcSetup
{
    static class UninstallMain
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool quiet = false;
            foreach (string a in args)
            {
                string s = a.ToLowerInvariant();
                if (s == "/quiet" || s == "/s" || s == "/silent") quiet = true;
            }

            // 顺序很重要：先读安装路径，再清注册表（清了就查不到了）
            string dir = Installer.FoundDir();
            if (string.IsNullOrEmpty(dir)) dir = Program.DefaultDir;

            if (quiet)
            {
                FipArc.Shell.UnregisterAll(dir);
                int rm, fl; string list;
                string self = Assembly.GetExecutingAssembly().Location;
                Installer.UninstallFiles(dir, true, self, out rm, out fl, out list);
                if (Installer.IsUnder(self, dir)) Installer.ScheduleSelfDelete(dir, self);
                return;
            }

            Application.Run(new UninstallForm());
        }
    }
}
