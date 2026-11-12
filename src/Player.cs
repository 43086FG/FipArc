// Player.cs —— 基于 DirectShow 的媒体播放引擎
//
// 为什么不用 MCI：
//   MCI 的 mpegvideo 只认很老的 MPEG-1 / AVI，遇到 H.264 的 .mp4 直接报
//   "MCI 无法打开该文件 (错误码 277)"。DirectShow 走的是系统解码器
//   （Windows 自带 H.264/AAC/MP3/WMA/WMV，装了扩展还能解 HEVC 等），
//   格式支持面大得多，而且能直接把画面渲染到我们的 Panel 里。
//
// 用法：
//   using (DsPlayer p = new DsPlayer(panel.Handle)) { p.Open(file); p.Play(); }
using System;
using System.IO;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace FipArc
{
    // ---------------- COM 接口声明 ----------------
    // 注意：下面这些接口的方法顺序必须和 C++ 头文件（control.h / strmif.h）完全一致，
    // 差一个方法就会调错虚表项，表现为崩溃或静默无效。

    [ComImport, Guid("00020400-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDispatch
    {
        [PreserveSig] int GetTypeInfoCount();
        [PreserveSig] int GetTypeInfo(int iTInfo, int lcid, out IntPtr ppTInfo);
        [PreserveSig] int GetIDsOfNames(ref Guid riid, IntPtr rgszNames, int cNames, int lcid, IntPtr rgDispId);
        [PreserveSig] int Invoke(int dispIdMember, ref Guid riid, int lcid, ushort wFlags,
                                 IntPtr pDispParams, IntPtr pVarResult, IntPtr pExcepInfo, IntPtr puArgErr);
    }

    [ComImport, Guid("56A868A9-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphBuilder
    {
        // ---- IFilterGraph ----
        [PreserveSig] int AddFilter(IntPtr pFilter, [MarshalAs(UnmanagedType.LPWStr)] string pName);
        [PreserveSig] int RemoveFilter(IntPtr pFilter);
        [PreserveSig] int EnumFilters(out IntPtr ppEnum);
        [PreserveSig] int FindFilterByName([MarshalAs(UnmanagedType.LPWStr)] string pName, out IntPtr ppFilter);
        [PreserveSig] int ConnectDirect(IntPtr pPinOut, IntPtr pPinIn, IntPtr pmt);
        [PreserveSig] int Reconnect(IntPtr pPin);
        [PreserveSig] int Disconnect(IntPtr pPin);
        [PreserveSig] int SetDefaultSyncSource();
        // ---- IGraphBuilder ----
        [PreserveSig] int Connect(IntPtr pPinOut, IntPtr pPinIn);
        [PreserveSig] int Render(IntPtr pPinOut);
        [PreserveSig] int RenderFile([MarshalAs(UnmanagedType.LPWStr)] string lpcwstrFile,
                                     [MarshalAs(UnmanagedType.LPWStr)] string lpcwstrPlayList);
        [PreserveSig] int AddSourceFilter([MarshalAs(UnmanagedType.LPWStr)] string lpcwstrFileName,
                                          [MarshalAs(UnmanagedType.LPWStr)] string lpcwstrFilterName,
                                          out IntPtr ppFilter);
        [PreserveSig] int SetLogFile(IntPtr hFile);
        [PreserveSig] int Abort();
        [PreserveSig] int ShouldOperationContinue();
    }

    [ComImport, Guid("56A868B1-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMediaControl : IDispatch
    {
        [PreserveSig] int Run();
        [PreserveSig] int Pause();
        [PreserveSig] int Stop();
        [PreserveSig] int GetState(int msTimeout, out int pfs);
        [PreserveSig] int SetRate(double dRate);
        [PreserveSig] int GetRate(out double pdRate);
        [PreserveSig] int GetCaps(out int pCaps);
        [PreserveSig] int GetCurrentPosition(out long p);
    }

    [ComImport, Guid("36B73880-C2C8-11CF-8B46-00805F6CEF60"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMediaSeeking : IDispatch
    {
        [PreserveSig] int GetCapabilities(out int pCapabilities);
        [PreserveSig] int CheckCapabilities(ref int pCapabilities);
        [PreserveSig] int IsFormatSupported(ref Guid pFormat);
        [PreserveSig] int QueryPreferredFormat(out Guid pFormat);
        [PreserveSig] int GetTimeFormat(out Guid pFormat);
        [PreserveSig] int IsUsingTimeFormat(ref Guid pFormat);
        [PreserveSig] int SetTimeFormat(ref Guid pFormat);
        [PreserveSig] int GetDuration(out long pDuration);
        [PreserveSig] int GetStopPosition(out long pStop);
        [PreserveSig] int GetCurrentPosition(out long pCurrent);
        [PreserveSig] int ConvertTimeFormat(out long pTarget, ref Guid pTargetFormat,
                                            long Source, ref Guid pSourceFormat);
        [PreserveSig] int SetPositions(ref long pCurrent, int dwCurrentFlags,
                                       ref long pStop, int dwStopFlags);
        [PreserveSig] int GetPositions(out long pCurrent, out long pStop);
        [PreserveSig] int GetAvailable(out long pEarliest, out long pLatest);
        [PreserveSig] int SetRate(double dRate);
        [PreserveSig] int GetRate(out double pdRate);
        [PreserveSig] int GetPreroll(out long pllPreroll);
    }

    [ComImport, Guid("56A868B4-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IVideoWindow : IDispatch
    {
        [PreserveSig] int put_Caption(string strCaption);
        [PreserveSig] int get_Caption(out string strCaption);
        [PreserveSig] int put_WindowStyle(int WindowStyle);
        [PreserveSig] int get_WindowStyle(out int WindowStyle);
        [PreserveSig] int put_WindowStyleEx(int WindowStyleEx);
        [PreserveSig] int get_WindowStyleEx(out int WindowStyleEx);
        [PreserveSig] int put_AutoShow(int AutoShow);
        [PreserveSig] int get_AutoShow(out int AutoShow);
        [PreserveSig] int put_WindowState(int WindowState);
        [PreserveSig] int get_WindowState(out int WindowState);
        [PreserveSig] int put_BackgroundPalette(int BackgroundPalette);
        [PreserveSig] int get_BackgroundPalette(out int BackgroundPalette);
        [PreserveSig] int put_Visible(int Visible);
        [PreserveSig] int get_Visible(out int Visible);
        [PreserveSig] int put_Left(int Left);
        [PreserveSig] int get_Left(out int Left);
        [PreserveSig] int put_Width(int Width);
        [PreserveSig] int get_Width(out int Width);
        [PreserveSig] int put_Top(int Top);
        [PreserveSig] int get_Top(out int Top);
        [PreserveSig] int put_Height(int Height);
        [PreserveSig] int get_Height(out int Height);
        [PreserveSig] int put_Owner(IntPtr Owner);
        [PreserveSig] int get_Owner(out IntPtr Owner);
        [PreserveSig] int put_MessageDrain(IntPtr Drain);
        [PreserveSig] int get_MessageDrain(out IntPtr Drain);
        [PreserveSig] int get_BorderColor(out int Color);
        [PreserveSig] int put_BorderColor(int Color);
        [PreserveSig] int get_FullScreenMode(out int FullScreenMode);
        [PreserveSig] int put_FullScreenMode(int FullScreenMode);
        [PreserveSig] int SetWindowForeground(int Focus);
        [PreserveSig] int NotifyOwnerMessage(IntPtr hwnd, int uMsg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int SetWindowPosition(int Left, int Top, int Width, int Height);
        [PreserveSig] int GetWindowPosition(out int pLeft, out int pTop, out int pWidth, out int pHeight);
        [PreserveSig] int GetMinIdealImageSize(out int pWidth, out int pHeight);
        [PreserveSig] int GetMaxIdealImageSize(out int pWidth, out int pHeight);
        [PreserveSig] int GetRestorePosition(out int pLeft, out int pTop, out int pWidth, out int pHeight);
        [PreserveSig] int HideCursor(int HideCursor);
        [PreserveSig] int IsCursorHidden(out int HideCursor);
    }

    [ComImport, Guid("56A868B3-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IBasicAudio : IDispatch
    {
        [PreserveSig] int put_Volume(int lVolume);
        [PreserveSig] int get_Volume(out int plVolume);
        [PreserveSig] int put_Balance(int lBalance);
        [PreserveSig] int get_Balance(out int plBalance);
    }

    // ---------------- 播放器 ----------------
    public sealed class DsPlayer : IDisposable
    {
        static readonly Guid CLSID_FilterGraph = new Guid("E436EBB3-524F-11CE-9F53-0020AF0BA770");

        [DllImport("ole32.dll", ExactSpelling = true, PreserveSig = false)]
        static extern void CoCreateInstance(ref Guid rclsid,
            [MarshalAs(UnmanagedType.IUnknown)] object pUnkOuter,
            uint dwClsContext, ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

        const uint CLSCTX_INPROC_SERVER = 1;

        IGraphBuilder graph;
        IMediaControl control;
        IMediaSeeking seeking;
        IVideoWindow video;
        IBasicAudio audio;
        IntPtr host;
        bool disposed;

        public bool HasVideo;
        public long DurationMs;

        public DsPlayer(IntPtr videoHost)
        {
            host = videoHost;
            try
            {
                object obj = null;
                Guid clsid = CLSID_FilterGraph;
                Guid iid = typeof(IGraphBuilder).GUID;
                CoCreateInstance(ref clsid, null, CLSCTX_INPROC_SERVER, ref iid, out obj);
                graph = (IGraphBuilder)obj;

                control = (IMediaControl)obj;
                seeking = obj as IMediaSeeking;
                video = obj as IVideoWindow;
                audio = obj as IBasicAudio;
            }
            catch (Exception ex)
            {
                throw new Exception("无法初始化播放引擎（DirectShow）。\r\n" + ex.Message);
            }
        }

        /// <summary>打开一个媒体文件，自动构建播放链路</summary>
        public void Open(string path)
        {
            if (graph == null) throw new InvalidOperationException("播放引擎未初始化");

            int hr = graph.RenderFile(path, null);
            if (hr != 0)
                throw new Exception("无法播放这个文件（DirectShow 错误 0x" + hr.ToString("X8") + "）。\r\n" +
                                    "通常是这个格式缺少系统解码器，可以装一下对应的解码包。");

            // 注意：IVideoWindow 的 COM 封装在这台机器上会触发 AccessViolation
            // （OLEAUT32 里 0xc0000005），所以只能用最保守的探测方式：
            // 探到了才挂窗口，探不到就不碰 IVideoWindow —— 宁可没画面也不能崩进程。
            HasVideo = false;
            if (video != null)
            {
                int vis;
                if (video.get_Visible(out vis) == 0 && vis != 0) HasVideo = true;
                else
                {
                    int l, t, w, h;
                    if (video.GetWindowPosition(out l, out t, out w, out h) == 0 && w > 1 && h > 1)
                        HasVideo = true;
                }
            }

            if (HasVideo)
            {
                video.put_Owner(host);
                video.put_WindowStyle(0x40000000 | 0x02000000);   // WS_CHILD | WS_CLIPCHILDREN
                video.put_Visible(1);
            }

            if (seeking != null)
            {
                Guid fmt = new Guid("7B8FB07C-DC37-11CF-901E-00C04FD9B4E8");   // TIME_FORMAT_MEDIA_TIME
                seeking.SetTimeFormat(ref fmt);
                long d;
                if (seeking.GetDuration(out d) == 0) DurationMs = d / 10000;
            }
            if (DurationMs <= 0) DurationMs = 0;
        }

        public void Play()
        {
            if (control != null) control.Run();
            if (HasVideo && video != null) video.put_Visible(1);
        }

        public void Pause()
        {
            if (control != null) control.Pause();
        }

        public void Stop()
        {
            if (control != null) control.Stop();
            if (seeking != null)
            {
                long zero = 0;
                Guid fmt = new Guid("7B8FB07C-DC37-11CF-901E-00C04FD9B4E8");
                seeking.SetPositions(ref zero, 1 /*ABSOLUTE*/, ref zero, 1);
            }
        }

        public long PositionMs
        {
            get
            {
                if (seeking == null) return 0;
                long p;
                if (seeking.GetCurrentPosition(out p) != 0) return 0;
                return p / 10000;
            }
        }

        public void Seek(long ms)
        {
            if (seeking == null || ms < 0) return;
            long t = ms * 10000;
            long stop = DurationMs > 0 ? DurationMs * 10000 : 0;
            seeking.SetPositions(ref t, 1 /*ABSOLUTE*/, ref stop, 1);
        }

        /// <summary>0-100</summary>
        public int Volume
        {
            get
            {
                if (audio == null) return 100;
                int v;
                if (audio.get_Volume(out v) != 0) return 100;
                return (v + 10000) / 200;                       // -10000..0 -> 0..50 ... 见 setter
            }
            set
            {
                if (audio == null) return;
                if (value < 0) value = 0;
                if (value > 100) value = 100;
                // DirectShow 音量是 -10000(静音) .. 0(最大)，按分贝线性映射
                int v = value == 0 ? -10000 : (int)(-10000.0 * (1.0 - value / 100.0));
                audio.put_Volume(v);
            }
        }

        /// <summary>倍速播放（部分解码器不支持，调用方自己吞掉失败）</summary>
        public void SetRate(double rate)
        {
            if (control != null) control.SetRate(rate);
        }

        /// <summary>窗口大小变化时把视频画面铺满客户区</summary>
        public void ResizeVideo(Size size)
        {
            if (video == null || !HasVideo) return;
            video.SetWindowPosition(0, 0, size.Width, size.Height);
        }

        public bool IsPlaying
        {
            get
            {
                if (control == null) return false;
                int state;
                if (control.GetState(0, out state) != 0) return false;
                return state == 2;          // State_Running
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { if (control != null) control.Stop(); } catch { }
            try { if (video != null) { video.put_Visible(0); video.put_Owner(IntPtr.Zero); } } catch { }
            try
            {
                if (graph != null) Marshal.ReleaseComObject(graph);
            }
            catch { }
            graph = null; control = null; seeking = null; video = null; audio = null;
        }
    }
}
