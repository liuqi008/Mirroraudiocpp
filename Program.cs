using System;
using System.Linq;
using System.Reflection;
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

            Form mainForm = null;
            var asm = Assembly.GetExecutingAssembly();

            // Try type MirrorAudio.SettingsForm or global SettingsForm
            var t = asm.GetType("MirrorAudio.SettingsForm", throwOnError: false)
                     ?? asm.GetType("SettingsForm", throwOnError: false);

            if (t != null && typeof(Form).IsAssignableFrom(t))
            {
                try
                {
                    // Prefer ctor(AppSettings, Func<StatusSnapshot>)
                    var ctor = t.GetConstructors()
                                .FirstOrDefault(c =>
                                {
                                    var ps = c.GetParameters();
                                    return ps.Length == 2
                                        && ps[0].ParameterType.FullName == "MirrorAudio.AppSettings"
                                        && ps[1].ParameterType.FullName.StartsWith("System.Func");
                                });

                    if (ctor != null)
                    {
                        var cur = new AppSettings(); // default settings
                        Func<StatusSnapshot> provider = () => new StatusSnapshot { Running = false };
                        mainForm = (Form)ctor.Invoke(new object[] { cur, provider });
                    }
                    else
                    {
                        // If there is a parameterless ctor, use it
                        var ctor0 = t.GetConstructor(Type.EmptyTypes);
                        if (ctor0 != null)
                        {
                            mainForm = (Form)Activator.CreateInstance(t);
                        }
                    }
                }
                catch
                {
                    mainForm = null;
                }
            }

            // Fallback to placeholder if we couldn't construct
            if (mainForm == null) mainForm = new GenericMainForm();

            Application.Run(mainForm);
        }
    }
}
