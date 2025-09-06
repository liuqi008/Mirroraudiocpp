using System;
using System.Runtime.InteropServices;

namespace MirrorAudio.Interop
{
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct RcOpenParams
    {
        public int SampleRate;
        public int Bits;
        public int Channels;
        public int TargetBufferMs;
        public int PreferRaw;        // 0/1
        public int PreferExclusive;  // 0/1
    }

    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct RcStatus
    {
        public int RequestedMs;
        public int QuantizedMs;
        public int EffectiveMs;
        public int Path;       // 0=shared,1=exclusive,2=exclusive-raw
        public int EventMode;  // 0=poll,1=event
        public int Running;    // 0/1
    }

    public static class RenderCore
    {
        const string DllName = "render_core.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int rc_open(ref RcOpenParams p);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void rc_close();
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void rc_get_status(out RcStatus s);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int rc_write(IntPtr data, int bytes);

        public static int Open(int rate, int bits, int ch, int targetMs, bool raw, bool exclusive)
        {
            RcOpenParams p = new RcOpenParams {
                SampleRate = rate, Bits = bits, Channels = ch,
                TargetBufferMs = targetMs, PreferRaw = raw?1:0, PreferExclusive = exclusive?1:0
            };
            return rc_open(ref p);
        }

        public static void Close() => rc_close();

        public static RcStatus GetStatus()
        {
            rc_get_status(out RcStatus s);
            return s;
        }

        public static int Write(ReadOnlySpan<byte> pcm)
        {
            unsafe
            {
                fixed (byte* ptr = pcm)
                {
                    return rc_write((IntPtr)ptr, pcm.Length);
                }
            }
        }
    }
}
