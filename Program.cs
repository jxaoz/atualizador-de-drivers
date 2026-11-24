using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle(DriverUpdater.AppStyle.Name)]
[assembly: AssemblyDescription("Consulta e instalação de atualizações de driver pelo Windows Update")]
[assembly: AssemblyProduct(DriverUpdater.AppStyle.Name)]
[assembly: AssemblyVersion("1.4.0.0")]
[assembly: AssemblyFileVersion("1.4.0.0")]
[assembly: AssemblyInformationalVersion("1.4.0")]

namespace DriverUpdater
{
    internal static class Program
    {
        private const string InstallArgument = "--install";
        private const string NightArgument = "--night";
        private const string PreferenceKey = @"Software\AtualizadorDeDrivers";
        private const string NightModeValue = "NightMode";

        [STAThread]
        private static int Main(string[] args)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length == 0)
            {
                AppStyle.IsDark = ReadNightMode();
                Application.Run(new MainWindow());
                return 0;
            }

            var selections = new List<DriverOffer>();
            bool useDarkTheme;
            if (!TryParseInstallation(args, selections, out useDarkTheme) || !IsAdministrator())
            {
                MessageBox.Show("Abra o aplicativo e selecione as atualizações que deseja instalar.",
                    AppStyle.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 2;
            }

            AppStyle.IsDark = useDarkTheme;
            using (var window = new InstallWindow(selections))
            {
                Application.Run(window);
                return window.ResultCode;
            }
        }

        public static Process StartInstaller(IList<DriverOffer> selections, bool useDarkTheme)
        {
            var args = new List<string> { InstallArgument };
            if (useDarkTheme)
                args.Add(NightArgument);
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

        internal static bool ReadNightMode()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PreferenceKey))
                    return key != null && Equals(key.GetValue(NightModeValue), 1);
            }
            catch (SecurityException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }
        }

        internal static void SaveNightMode(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(PreferenceKey))
                {
                    if (key != null)
                        key.SetValue(NightModeValue, enabled ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch (SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
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

        private static bool TryParseInstallation(string[] args, List<DriverOffer> selections, out bool useDarkTheme)
        {
            useDarkTheme = false;
            if (args.Length < 2 || args[0] != InstallArgument)
                return false;

            useDarkTheme = args[1] == NightArgument;
            int firstOffer = useDarkTheme ? 2 : 1;
            if (firstOffer == args.Length)
                return false;

            var seen = new HashSet<Tuple<Guid, int>>();
            for (int i = firstOffer; i < args.Length; i++)
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
