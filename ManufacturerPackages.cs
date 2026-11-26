using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace DriverUpdater
{
    internal sealed class ManufacturerDriver
    {
        public string Source, Version, DriverVersion, InstalledVersion, ArchiveHash, ArchiveEntry, DownloadHash;
        public Uri DownloadUrl;
        public string[] Publishers;
    }

    internal static class ManufacturerPackages
    {
        private const string GigabyteCdn = "download.gigabyte.com";
        private const string Board = "A520M K V2";
        private const string GigabyteSigner = "GIGA-BYTE TECHNOLOGY CO., LTD.";

        public static List<DriverOffer> FindForPc(IList<InstalledDriver> inventory, bool onlyUpdates)
        {
            var offers = new List<DriverOffer>();
            if (Environment.OSVersion.Version.Major != 10 || RuntimeInformation.OSArchitecture != Architecture.X64 || !HasSupportedBoard())
                return offers;

            // Identical official packages on the three published A520M K V2 hardware revisions (1.0, 1.1, 1.2).
            InstalledDriver audio = FindDevice(inventory, @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0897&SUBSYS_1458A194");
            AddBoardPackage(offers, onlyUpdates, audio, "6.0.9235.1", new DateTime(2021, 11, 2),
                "Realtek HD Audio", "Media", "mb_driver_612_realtekdch_6.0.9235.1",
                "6AAA59A5C075E60B7F3172965D1113C6977DD3A9EBE7D9C0C2DCC9F33C76281E",
                audio == null ? null : audio.Version,
                audio != null && (audio.MissingDriver || IsNewer("6.0.9235.1", audio.Version, true)));

            if (Environment.OSVersion.Version.Build >= 22000)
            {
                InstalledDriver lan = FindDevice(inventory, @"PCI\VEN_10EC&DEV_8168&SUBSYS_E0001458");
                var date = new DateTime(2023, 12, 29);
                AddBoardPackage(offers, onlyUpdates, lan, "11.16.1123.2023", date,
                    "Realtek LAN", "Net", "mb_driver_654_w11_11.16.1123.2023",
                    "37808EBE891D6B0D0F59FA88699438966D566CA88A560C6B5C37BD5DAA0A46F3",
                    lan == null ? null : lan.Version,
                    lan != null && (lan.MissingDriver || (lan.Date.HasValue && lan.Date.Value <= date &&
                        IsNewer("11.16.1123.2023", lan.Version, true))));
            }

            string chipset = ReadAmdChipsetVersion();
            AddBoardPackage(offers, onlyUpdates, null, "5.11.02.217", new DateTime(2024, 1, 3),
                "AMD Chipset", "System", "mb_driver_597_chipset_5.11.02.217",
                "47557B2523160723C0819AC9ABEA682A016F667B80138CB0DC495A4AD397E7F6",
                chipset, IsNewer("5.11.02.217", chipset, false));
            return offers;
        }

        private static void AddBoardPackage(List<DriverOffer> offers, bool onlyUpdates, InstalledDriver device,
            string version, DateTime date, string name, string driverClass, string file, string hash, string installedVersion, bool newer)
        {
            if ((onlyUpdates && !newer) || (device == null && driverClass != "System"))
                return;
            var offer = new DriverOffer
            {
                Title = name + " " + version, Manufacturer = "GIGABYTE", Class = driverClass, Date = date,
                AssociatedDevice = device == null ? Board : device.Device,
                HardwareIds = device == null ? new string[0] : device.HardwareIds,
                ManufacturerPackage = new ManufacturerDriver
                {
                    Source = "GIGABYTE", Version = version, InstalledVersion = installedVersion,
                    ArchiveHash = hash, ArchiveEntry = file + ".exe",
                    DownloadUrl = new Uri("https://" + GigabyteCdn + "/FileList/Driver/" + file + ".zip"),
                    Publishers = new[] { GigabyteSigner }
                }
            };
            if (device != null)
                offer.Devices.Add(device);
            offers.Add(offer);
        }

        internal static bool MatchesDevice(InstalledDriver device, string hardware)
        {
            return !string.IsNullOrWhiteSpace(hardware) &&
                ((device.DeviceInstanceId ?? string.Empty).StartsWith(hardware + "&", StringComparison.OrdinalIgnoreCase) ||
                (device.DeviceInstanceId ?? string.Empty).StartsWith(hardware + "\\", StringComparison.OrdinalIgnoreCase) ||
                (device.HardwareIds ?? new string[0]).Contains(hardware, StringComparer.OrdinalIgnoreCase));
        }

        private static InstalledDriver FindDevice(IEnumerable<InstalledDriver> inventory, string hardware)
        {
            return inventory.FirstOrDefault(device => MatchesDevice(device, hardware));
        }

        internal static bool IsNewer(string version, string installed, bool compareWithinMajor)
        {
            Version candidate, existing;
            return Version.TryParse(version, out candidate) && Version.TryParse(installed, out existing) &&
                (!compareWithinMajor || candidate.Major == existing.Major) && candidate > existing;
        }

        internal static string ReadAmdChipsetVersion()
        {
            Version latest = null;
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (RegistryKey entries = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (entries == null) continue;
                    foreach (string key in entries.GetSubKeyNames())
                    using (RegistryKey entry = entries.OpenSubKey(key))
                    {
                        Version version;
                        if (entry != null && Equals(entry.GetValue("DisplayName"), "AMD Chipset Software") &&
                            Version.TryParse(entry.GetValue("DisplayVersion") as string, out version) && (latest == null || version > latest))
                            latest = version;
                    }
                }
            }
            return latest == null ? null : latest.ToString();
        }

        private static bool HasSupportedBoard()
        {
            using (var query = new ManagementObjectSearcher("SELECT Manufacturer, Product, Version FROM Win32_BaseBoard"))
            using (var rows = query.Get())
            foreach (ManagementObject row in rows)
            using (row)
            {
                string manufacturer = Convert.ToString(row["Manufacturer"], CultureInfo.InvariantCulture);
                string product = Convert.ToString(row["Product"], CultureInfo.InvariantCulture).Trim();
                string revision = Convert.ToString(row["Version"], CultureInfo.InvariantCulture).Trim().ToLowerInvariant();
                if (manufacturer.StartsWith("Gigabyte", StringComparison.OrdinalIgnoreCase) && product.Equals(Board, StringComparison.OrdinalIgnoreCase) &&
                    (revision == "1.0" || revision == "1.1" || revision == "1.2" || revision == "x.x" || revision.Length == 0))
                    return true;
            }
            return false;
        }

        public static DriverOffer FromOfficialLink(string input)
        {
            Uri uri;
            if (!Uri.TryCreate((input ?? string.Empty).Trim(), UriKind.Absolute, out uri) || !IsHttps(uri) ||
                !uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return null;
            string vendor;
            string[] publishers;
            switch (uri.IdnHost)
            {
                case "dl.dell.com": case "downloads.dell.com":
                    vendor = "Dell"; publishers = new[] { "Dell Inc.", "Dell Technologies Inc." }; break;
                case "ftp.hp.com": case "ftp.ext.hp.com":
                    vendor = "HP"; publishers = new[] { "HP Inc.", "Hewlett-Packard Company", "HP Development Company, L.P." }; break;
                case "download.lenovo.com":
                    vendor = "Lenovo"; publishers = new[] { "Lenovo", "Lenovo (Beijing) Limited", "Lenovo PC HK Limited" }; break;
                case "downloadmirror.intel.com":
                    vendor = "Intel"; publishers = new[] { "Intel Corporation" }; break;
                case "drivers.amd.com":
                    vendor = "AMD"; publishers = new[] { "Advanced Micro Devices", "Advanced Micro Devices, Inc." }; break;
                case GigabyteCdn:
                    vendor = "GIGABYTE"; publishers = new[] { GigabyteSigner }; break;
                default: return null;
            }
            string title = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            if (title.Length == 0 || title.Length > 140)
                return null;
            return new DriverOffer
            {
                Title = title, Manufacturer = vendor, HardwareIds = new string[0],
                ManufacturerPackage = new ManufacturerDriver { Source = vendor, DownloadUrl = uri, Publishers = publishers }
            };
        }

        public static async Task DownloadAndInstallAsync(ManufacturerDriver offer, IProgress<int> progress, Action installing,
            CancellationToken cancellationToken)
        {
            string directory = Path.Combine(Path.GetTempPath(), "DriversLicense-OEM-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string installer = Path.Combine(directory, "Pacote-do-fabricante.exe");
            string archive = Path.Combine(directory, "Pacote.zip");
            try
            {
                string download = offer.ArchiveHash == null ? installer : archive;
                string referer = offer.Source == "AMD" ? "https://www.amd.com/" : null;
                await NvidiaPackage.DownloadFileAsync(offer.DownloadUrl, download, progress, cancellationToken,
                    target => IsHttps(target) && target.IdnHost == offer.DownloadUrl.IdnHost, referer: referer).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (offer.DownloadHash != null)
                    VerifyHash(download, offer.DownloadHash);
                if (offer.ArchiveHash != null)
                    ExtractVerified(archive, installer, offer);

                cancellationToken.ThrowIfCancellationRequested();
                installing();
                NvidiaPackage.RunInstaller(installer, cancellationToken, offer.Publishers);
            }
            finally
            {
                try { File.Delete(installer); File.Delete(archive); Directory.Delete(directory); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        internal static bool IsHttps(Uri uri)
        {
            return uri != null && uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
                uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
        }

        internal static void VerifyHash(string path, string expected)
        {
            using (var stream = File.OpenRead(path))
                VerifyHash(stream, expected);
        }

        private static void VerifyHash(Stream stream, string expected)
        {
            using (var sha = SHA256.Create())
            {
                string digest = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
                if (!string.Equals(digest, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("O arquivo não corresponde ao SHA-256 informado pelo catálogo.");
            }
        }

        private static void ExtractVerified(string archive, string installer, ManufacturerDriver offer)
        {
            using (FileStream input = File.OpenRead(archive))
            {
                VerifyHash(input, offer.ArchiveHash);
                input.Position = 0;
                using (var zip = new ZipArchive(input, ZipArchiveMode.Read))
                {
                    var entry = zip.GetEntry(offer.ArchiveEntry);
                    if (zip.Entries.Count != 1 || entry == null || entry.Length > 1024L * 1024 * 1024)
                        throw new InvalidDataException("O ZIP não contém o instalador esperado.");
                    using (var source = entry.Open())
                    using (var destination = new FileStream(installer, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        source.CopyTo(destination);
                }
            }
        }
    }
}
