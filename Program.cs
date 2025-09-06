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

            var cur = new AppSettings(); // 你可在此填入默认/上次保存的配置
            Func<StatusSnapshot> provider = () => new StatusSnapshot { Running = false };

            Application.Run(new SettingsForm(cur, provider));
        }
    }
}
