using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Forms;

[assembly: AssemblyTitle("Atualizador de drivers")]
[assembly: AssemblyDescription("Consulta e instalação de atualizações de driver pelo Windows Update")]
[assembly: AssemblyProduct("Atualizador de drivers")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

namespace DriverUpdater
{
    internal static class Program
    {
        private const string InstallArgument = "--install";

        [STAThread]
        private static int Main(string[] args)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length == 0)
            {
                Application.Run(new MainWindow());
                return 0;
            }

            var selections = new List<DriverOffer>();
            if (!TryParseInstallation(args, selections) || !IsAdministrator())
            {
                MessageBox.Show("Abra o aplicativo e selecione as atualizações que deseja instalar.",
                    "Atualizador de drivers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 2;
            }

            using (var window = new InstallWindow(selections))
            {
                Application.Run(window);
                return window.ResultCode;
            }
        }

        public static Process StartInstaller(IList<DriverOffer> selections)
        {
            var args = new List<string> { InstallArgument };
            foreach (DriverOffer offer in selections)
            {
                args.Add(Guid.Parse(offer.Id).ToString("D") + ":" +
                    offer.Revision.ToString(CultureInfo.InvariantCulture));
            }

            string executable = Application.ExecutablePath;
            return Process.Start(new ProcessStartInfo(executable, string.Join(" ", args))
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(executable)
            });
        }

        public static bool PermissionWasDeclined(Exception error)
        {
            var systemError = error as Win32Exception;
            return systemError != null && systemError.NativeErrorCode == 1223;
        }

        private static bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private static bool TryParseInstallation(string[] args, List<DriverOffer> selections)
        {
            if (args.Length < 2 || args[0] != InstallArgument)
                return false;

            var seen = new HashSet<Tuple<Guid, int>>();
            for (int i = 1; i < args.Length; i++)
            {
                string[] parts = args[i].Split(':');
                Guid id;
                int revision;
                if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "D", out id) ||
                    id == Guid.Empty ||
                    !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out revision) || revision <= 0 ||
                    !seen.Add(Tuple.Create(id, revision)))
                    return false;

                selections.Add(new DriverOffer { Id = id.ToString("D"), Revision = revision });
            }

            return true;
        }
    }
}
