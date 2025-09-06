using System;
using System.Drawing;
using System.Windows.Forms;

namespace MirrorAudio
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayAppContext());
        }
    }

    /// <summary>
    /// 托盘应用上下文：保持进程常驻，提供托盘菜单与设置入口。
    /// </summary>
    internal sealed class TrayAppContext : ApplicationContext
    {
        private readonly NotifyIcon _tray;
        private SettingsForm _settings;

        public TrayAppContext()
        {
            var menu = new ContextMenuStrip();
            var mOpen = new ToolStripMenuItem("打开设置", null, (s,e) => ShowSettings());
            var mExit = new ToolStripMenuItem("退出", null, (s,e) => ExitApp());
            menu.Items.Add(mOpen);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(mExit);

            _tray = new NotifyIcon
            {
                Text = "MirrorAudio",
                Icon = TryLoadIcon(),
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.DoubleClick += (s,e) => ShowSettings();

            // 初次启动：加载配置并启动引擎
            var cfg = ConfigStore.LoadOrDefault();
            EngineHost.StartOrApply(cfg);

            // 可选：首次显示设置窗口
            // ShowSettings();
        }

        private static Icon TryLoadIcon()
        {
            try
            {
                // 若 csproj 中配置了 MirrorAudio.ico，会自动嵌入/复制；否则用系统图标兜底
                return new Icon("MirrorAudio.ico");
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        private void ShowSettings()
        {
            if (_settings != null && !_settings.IsDisposed)
            {
                _settings.Activate();
                return;
            }

            var cur = ConfigStore.Current ?? new AppSettings();
            _settings = new SettingsForm(cur, () => EngineHost.BuildStatus());
            _settings.FormClosed += OnSettingsClosed;
            _settings.Show();
        }

        private void OnSettingsClosed(object sender, FormClosedEventArgs e)
        {
            var form = sender as SettingsForm;
            if (form != null && form.DialogResult == DialogResult.OK && form.Result != null)
            {
                // 保存配置并应用到引擎（保持常驻运行，不退出）
                ConfigStore.Save(form.Result);
                EngineHost.StartOrApply(form.Result);
                _tray.BalloonTipTitle = "MirrorAudio";
                _tray.BalloonTipText = "设置已保存，正在托盘常驻运行。";
                _tray.ShowBalloonTip(1500);
            }
        }

        private void ExitApp()
        {
            try { EngineHost.Stop(); } catch {}
            _tray.Visible = false;
            _tray.Dispose();
            ExitThread();
        }
    }

    /// <summary>
    /// 配置存储（最小实现）：你可替换为 JSON/注册表等。
    /// </summary>
    internal static class ConfigStore
    {
        public static AppSettings Current { get; private set; }

        public static AppSettings LoadOrDefault()
        {
            // 这里为了最小可运行，直接提供默认配置；你可以改成磁盘持久化
            Current = Current ?? new AppSettings();
            return Current;
        }

        public static void Save(AppSettings cfg)
        {
            // TODO: 持久化到文件/注册表；当前仅驻内存保存
            Current = cfg;
        }
    }

    /// <summary>
    /// 引擎宿主（最小桩）：连接你的音频后端（NAudio 或 C++ DLL）。
    /// 此处仅维持“运行中”状态 & 回报缓冲/周期占位值，方便 UI 与托盘常驻。
    /// </summary>
    internal static class EngineHost
    {
        private static bool _running;

        public static void StartOrApply(AppSettings cfg)
        {
            // 在这里启动或重配底层音频管线（WASAPI/RAW/独占等）。
            // 最小桩：仅标记为运行
            _running = true;
        }

        public static void Stop()
        {
            _running = false;
            // TODO: 关闭音频管线
        }

        public static StatusSnapshot BuildStatus()
        {
            // 这里用占位值供 UI 显示；接入真实后端时替换为实际查询。
            return new StatusSnapshot
            {
                Running = _running,
                InputDevice = "-", InputRole = "-", InputFormat = "-",
                MainDevice = "-", MainMode = _cfg.ForcePassthrough ? "独占" : "共享",
                MainSync   = "事件",
                MainFormat = "PCM 48000/24/2",
                MainBufferMs = _cfg.ForcePassthrough ? 20 : 60,
                MainDefaultPeriodMs = 10, MainMinimumPeriodMs = 3,
                MainPassthrough = _cfg.ForcePassthrough || _cfg.ForceRaw,
                MainBufRequestedMs = _cfg.ForcePassthrough ? 20 : 60,
                MainBufQuantizedMs = _cfg.ForcePassthrough ? 20 : 60,

                AuxDevice = "-", AuxMode = "共享",
                AuxSync   = "事件",
                AuxFormat = "PCM 48000/24/2",
                AuxBufferMs = 80,
                AuxDefaultPeriodMs = 10, AuxMinimumPeriodMs = 3,
                AuxPassthrough = false,
                AuxBufRequestedMs = 80,
                AuxBufQuantizedMs = 80
            };
        }
    }
}
