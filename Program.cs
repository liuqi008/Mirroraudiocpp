using System;
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
            // TODO: 如果您有自定义启动窗体，替换 SettingsForm 为您的主窗体类名
            Application.Run(new SettingsForm());
        }
    }
}
