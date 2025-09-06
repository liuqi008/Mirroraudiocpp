using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MirrorAudio
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s,e)=> Debug.WriteLine(e.Exception);
            using (var app = new TrayApp())
            {
                Application.Run();
            }
        }
    }

    /// <summary>
    /// 与 2.1 版对齐的托盘常驻应用：菜单、开机自启、设备变更去抖重启。
    /// 仅做“宿主”与“控制”，音频执行仍在 EngineHost/原生 DLL。
    /// </summary>
    internal sealed class TrayApp : IDisposable, IMMNotificationClient
    {
        private readonly NotifyIcon _tray = new NotifyIcon();
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly Timer _debounce = new Timer { Interval = 400 };
        private MMDeviceEnumerator _mm = new MMDeviceEnumerator();

        private AppSettings _cfg = ConfigStore.LoadOrDefault();

        public TrayApp()
        {
            // 托盘图标
            _tray.Icon = TryLoadIcon();
            _tray.Visible = true;
            _tray.Text = "MirrorAudio";
            _tray.DoubleClick += (s,e)=> OnSettings();

            // 菜单（与 2.1 一致顺序）
            var miStart = new ToolStripMenuItem("启动/重启(&S)", null, (s,e)=> StartOrRestart());
            var miStop  = new ToolStripMenuItem("停止(&T)",    null, (s,e)=> Stop());
            var miSet   = new ToolStripMenuItem("设置(&G)...", null, (s,e)=> OnSettings());
            var miLog   = new ToolStripMenuItem("打开日志目录", null, (s,e)=> OpenLogDir());
            var miExit  = new ToolStripMenuItem("退出(&X)",    null, (s,e)=> { Stop(); Application.Exit(); });

            _menu.Items.Add(miStart);
            _menu.Items.Add(miStop);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(miSet);
            _menu.Items.Add(miLog);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(miExit);
            _tray.ContextMenuStrip = _menu;

            // 设备变更 → 去抖重启
            _debounce.Tick += (s,e)=> { _debounce.Stop(); StartOrRestart(); };
            _mm.RegisterEndpointNotificationCallback(this);

            EnsureAutoStart(_cfg.AutoStart);
            StartOrRestart();
        }

        private static Icon TryLoadIcon()
        {
            try
            {
                var icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MirrorAudio.ico");
                if (File.Exists(icoPath)) return new Icon(icoPath);
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { return SystemIcons.Application; }
        }

        private void EnsureAutoStart(bool enable)
        {
            try
            {
                using (var run=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (run==null) return;
                    const string name="MirrorAudio";
                    if (enable) run.SetValue(name, "\""+Application.ExecutablePath+"\"");
                    else run.DeleteValue(name, false);
                }
            } catch {}
        }

        private void OpenLogDir()
        {
            try
            {
                var tmp = Path.GetTempPath();
                Process.Start("explorer.exe", tmp);
            } catch {}
        }

        private void OnSettings()
        {
            using (var f = new SettingsForm(_cfg, EngineHost.BuildStatus))
            {
                if (f.ShowDialog() == DialogResult.OK)
                {
                    _cfg = f.Result;
                    ConfigStore.Save(_cfg);
                    EnsureAutoStart(_cfg.AutoStart);
                    StartOrRestart();
                }
            }
        }

        private void StartOrRestart()
        {
            Stop();
            EngineHost.StartOrApply(_cfg);
        }

        private void Stop()
        {
            try { EngineHost.Stop(); } catch {}
        }

        public void Dispose()
        {
            try { _mm?.UnregisterEndpointNotificationCallback(this); } catch {}
            _mm?.Dispose();
            Stop();
            _tray.Visible = false;
            _tray.Dispose();
            _menu.Dispose();
        }

        // —— 设备变更回调：全部触发去抖 —— //
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Debounce();
        public void OnDeviceAdded(string pwstrDeviceId) => Debounce();
        public void OnDeviceRemoved(string deviceId) => Debounce();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => Debounce();
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
        private void Debounce() { _debounce.Stop(); _debounce.Start(); }
    }

    /// <summary>
    /// 连接 render_core.dll 的最小宿主：将 2.1 的“管线创建/缓冲量化/状态回报”迁移到原生层；
    /// 这里仅负责把 AppSettings → Open 参数、Stop 生命周期、状态查询。
    /// </summary>
    internal static class EngineHost
    {
        private static bool _running;

        public static void StartOrApply(AppSettings cfg)
        {
            // 关闭旧实例
            Stop();

            // 传入主通道配置（RAW/独占优先）
            var preferExclusive = cfg.MainShare != ShareModeOption.Shared; // Auto/Exclusive → 独占优先
            var preferRaw = _cfg.ForceRaw || _cfg.ForcePassthrough;        // “RAW 优先”与“强制直通”任一勾选

            var code = MirrorAudio.Interop.RenderCore.Open(
                rate: cfg.MainRate,
                bits: cfg.MainBits,
                ch: 2,
                targetMs: cfg.MainBufMs,
                raw: preferRaw,
                exclusive: preferExclusive
            );

            _running = (code == 0);
        }

        public static void Stop()
        {
            if (_running)
            {
                MirrorAudio.Interop.RenderCore.Close();
                _running = false;
            }
        }

        public static StatusSnapshot BuildStatus()
        {
            var s = new StatusSnapshot();
            var st = MirrorAudio.Interop.RenderCore.GetStatus();

            s.Running = _running;
            s.MainDevice = "-";
            s.MainMode = st.Path == 1 ? "独占" : (st.Path == 2 ? "独占RAW" : "共享");
            s.MainSync = "事件";
            s.MainFormat = "-";
            s.MainBufferMs = st.EffectiveMs;
            s.MainDefaultPeriodMs = 0;
            s.MainMinimumPeriodMs = 0;
            s.MainPassthrough = (st.Path >= 1); // 简化：独占都视作直通；RAW 更优
            s.MainBufRequestedMs = st.RequestedMs;
            s.MainBufQuantizedMs = st.QuantizedMs;

            s.AuxDevice = "-";
            s.AuxMode = "-";
            s.AuxSync = "-";
            s.AuxFormat = "-";
            s.AuxBufferMs = 0;
            s.AuxDefaultPeriodMs = 0;
            s.AuxMinimumPeriodMs = 0;
            s.AuxPassthrough = false;
            s.AuxBufRequestedMs = 0;
            s.AuxBufQuantizedMs = 0;

            return s;
        }
    }
}
