using System;

namespace MirrorAudio
{
    // 供 SettingsForm.cs 编译的最小模型定义（如你的 Program.cs 已有，请忽略/合并）

    public enum ShareModeOption { Auto = 0, Exclusive = 1, Shared = 2 }
    public enum SyncModeOption  { Auto = 0, Event = 1,     Polling = 2 }

    public sealed class AppSettings
    {
        public string InputDeviceId { get; set; }
        public string MainDeviceId  { get; set; }
        public string AuxDeviceId   { get; set; }

        public ShareModeOption MainShare { get; set; } = ShareModeOption.Auto;
        public SyncModeOption  MainSync  { get; set; } = SyncModeOption.Auto;
        public int MainRate  { get; set; } = 48000;
        public int MainBits  { get; set; } = 24;
        public int MainBufMs { get; set; } = 20;

        public ShareModeOption AuxShare { get; set; } = ShareModeOption.Auto;
        public SyncModeOption  AuxSync  { get; set; } = SyncModeOption.Auto;
        public int AuxRate  { get; set; } = 48000;
        public int AuxBits  { get; set; } = 24;
        public int AuxBufMs { get; set; } = 60;

        public bool AutoStart { get; set; } = false;
        public bool EnableLogging { get; set; } = false;
    }

    public sealed class StatusSnapshot
    {
        public bool Running { get; set; }

        public string InputDevice { get; set; }
        public string InputRole   { get; set; }
        public string InputFormat { get; set; }

        public string MainDevice { get; set; }
        public string MainMode   { get; set; }
        public string MainSync   { get; set; }
        public string MainFormat { get; set; }
        public int    MainBufferMs { get; set; }
        public double MainDefaultPeriodMs  { get; set; }
        public double MainMinimumPeriodMs  { get; set; }
        public bool   MainPassthrough { get; set; }
        public int    MainBufRequestedMs  { get; set; }
        public int    MainBufQuantizedMs  { get; set; }

        public string AuxDevice { get; set; }
        public string AuxMode   { get; set; }
        public string AuxSync   { get; set; }
        public string AuxFormat { get; set; }
        public int    AuxBufferMs { get; set; }
        public double AuxDefaultPeriodMs   { get; set; }
        public double AuxMinimumPeriodMs   { get; set; }
        public bool   AuxPassthrough { get; set; }
        public int    AuxBufRequestedMs   { get; set; }
        public int    AuxBufQuantizedMs   { get; set; }
    }

    // 让 SettingsForm.cs 里对 "_cfg.XXX" 的引用可以直接编译
    public static class _cfg
    {
        public static bool ForcePassthrough = false;
        public static bool ForceRaw = false;
        public static bool ShowAdvanced = false;
    }
}
