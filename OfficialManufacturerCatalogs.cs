using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace DriverUpdater
{
    internal static class OfficialManufacturerCatalogs
    {
        private const int MetadataLimit = 12 * 1024 * 1024;
        private const string DellHost = "downloads.dell.com";
        private const string LenovoHost = "download.lenovo.com";
        private static readonly Regex HexId = new Regex(@"\A[0-9A-F]{4}\z", RegexOptions.IgnoreCase);
        private static readonly Regex MachineType = new Regex(@"\A[0-9][0-9][A-Z0-9]{2}", RegexOptions.IgnoreCase);
        private static readonly Regex Sha256 = new Regex(@"\A[0-9a-f]{64}\z", RegexOptions.IgnoreCase);
        private static readonly Regex LenovoInstaller = new Regex(@"\A[A-Z0-9_-]+\.exe\z", RegexOptions.IgnoreCase);
        private static readonly Regex AmdChipset = new Regex(
            @"\b(A320|A520|B350|B450|B550|B650E?|B840|B850|X370|X399|X470|X570|X670E?|X870E?|TRX40|TRX50|WRX80|WRX90)(?:M|I)?\b",
            RegexOptions.IgnoreCase);
        private static readonly Regex IntelWifi = new Regex(
            @"\b(?:BE20[012]|BE21[13]|AX(?:101|201|203|210|211|231|411)|Wireless-AC (?:9260|946[12]|9560))\b",
            RegexOptions.IgnoreCase);

        public static async Task<DriverScan> FindForPcAsync(IList<InstalledDriver> inventory, bool onlyUpdates)
        {
            var result = new DriverScan();
            Version windows = Environment.OSVersion.Version;
            if (windows.Major != 10 || RuntimeInformation.OSArchitecture != Architecture.X64)
                return result;

            try
            {
                result.Offers.AddRange(ManufacturerPackages.FindForPc(inventory, onlyUpdates));
            }
            catch (Exception error)
            {
                result.Partial = true;
                result.Warnings = "GIGABYTE: " + DriverServices.DescribeError(error);
            }

            string source = "Catálogo do computador";
            try
            {
                using (var query = new ManagementObjectSearcher("SELECT Manufacturer, Model, SystemSKUNumber FROM Win32_ComputerSystem"))
                using (var rows = query.Get())
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
                foreach (ManagementObject pc in rows)
                using (pc)
                {
                    string brand = Convert.ToString(pc["Manufacturer"]).Trim();
                    string model = Convert.ToString(pc["Model"]).Trim();
                    string sku = Convert.ToString(pc["SystemSKUNumber"]).Trim();
                    if (brand.StartsWith("Dell", StringComparison.OrdinalIgnoreCase))
                    {
                        source = "Dell";
                        if (HexId.IsMatch(sku))
                            result.Offers.AddRange(await FindDellAsync(sku, windows.Build, inventory, onlyUpdates, timeout.Token).ConfigureAwait(false));
                    }
                    else if (brand.Equals("HP", StringComparison.OrdinalIgnoreCase) ||
                        brand.StartsWith("Hewlett-Packard", StringComparison.OrdinalIgnoreCase))
                    {
                        source = "HP";
                        string boardId = ReadBoardId();
                        if (HexId.IsMatch(boardId))
                            result.Offers.AddRange(await FindHpAsync(boardId, windows.Build, inventory, onlyUpdates, timeout.Token).ConfigureAwait(false));
                    }
                    else if (brand.Equals("LENOVO", StringComparison.OrdinalIgnoreCase))
                    {
                        source = "Lenovo";
                        if (MachineType.IsMatch(model))
                            result.Offers.AddRange(await FindLenovoAsync(model.Substring(0, 4), windows.Build, inventory,
                                onlyUpdates, ReadBiosVersion(), timeout.Token).ConfigureAwait(false));
                    }
                }
            }
            catch (Exception error)
            {
                result.Partial = true;
                result.Warnings = (result.Warnings == null ? string.Empty : result.Warnings + " ") + source + ": " +
                    (error is OperationCanceledException ? "a consulta excedeu o tempo disponível." : DriverServices.DescribeError(error));
            }

            AddIntel(result.Offers, inventory, onlyUpdates);
            try { AddAmd(result.Offers, inventory, onlyUpdates); }
            catch (Exception error)
            {
                result.Partial = true;
                result.Warnings = (result.Warnings == null ? string.Empty : result.Warnings + " ") + "AMD: " + DriverServices.DescribeError(error);
            }

            var seenDevices = new HashSet<Tuple<string, InstalledDriver>>();
            var packages = new Dictionary<Uri, DriverOffer>();
            foreach (var offer in result.Offers
                .OrderByDescending(offer => ParseVersion(offer.ManufacturerPackage.DriverVersion ?? offer.ManufacturerPackage.Version))
                .ThenByDescending(offer => offer.Date))
            {
                if (offer.Devices.Count != 0 && !seenDevices.Add(Tuple.Create(offer.Manufacturer, offer.Devices[0])))
                    continue;
                Uri url = offer.ManufacturerPackage.DownloadUrl;
                DriverOffer existing;
                if (packages.TryGetValue(url, out existing))
                {
                    existing.Devices.AddRange(offer.Devices);
                    existing.HardwareIds = (existing.HardwareIds ?? new string[0]).Concat(offer.HardwareIds ?? new string[0])
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                }
                else
                    packages.Add(url, offer);
            }
            result.Offers = packages.Values.OrderBy(offer => offer.Manufacturer).ThenBy(offer => offer.Title).ToList();
            return result;
        }

        internal static async Task<List<DriverOffer>> FindDellAsync(string sku, int build, IList<InstalledDriver> inventory,
            bool onlyUpdates, CancellationToken cancellation)
        {
            var offers = new List<DriverOffer>();
            if (!HexId.IsMatch(sku))
                return offers;
            var root = new Uri("https://" + DellHost + "/");
            XNamespace ns = "openmanage/cm/dm";
            XDocument index = await ReadDellCabAsync(new Uri(root, "catalog/CatalogIndexPC.cab"), null, cancellation).ConfigureAwait(false);
            XElement manifest = index.Root.Elements(ns + "GroupManifest")
                .Where(group => group.Descendants(ns + "Model").Any(model =>
                    string.Equals((string)model.Attribute("systemID"), sku, StringComparison.OrdinalIgnoreCase)))
                .SelectMany(group => group.Elements(ns + "ManifestInformation").Where(item => (string)item.Attribute("id") == (string)group.Attribute("latest")))
                .OrderByDescending(item => (string)item.Attribute("creationDateTime"), StringComparer.Ordinal).FirstOrDefault();
            if (manifest == null)
                return offers;

            Uri uri = DellUri(root, (string)manifest.Attribute("path"), ".cab");
            string digest = DellHash(manifest, ns);
            if (uri == null || !ValidHash(digest))
                throw new InvalidDataException("O índice da Dell não informou um catálogo verificável para o modelo.");
            XDocument catalog = await ReadDellCabAsync(uri, digest, cancellation).ConfigureAwait(false);
            string requiredOs = build >= 22000 ? "Windows 11" : "Windows 10";
            foreach (XElement item in catalog.Root.Elements(ns + "SoftwareComponent"))
            {
                if ((string)item.Elements(ns + "ComponentType").Attributes("value").FirstOrDefault() != "DRVR" ||
                    !item.Elements(ns + "SupportedOperatingSystems").Elements(ns + "OperatingSystem").Any(os =>
                        (string)os.Attribute("osArch") == "x64" && os.Elements(ns + "Display").Any(label =>
                        label.Value.StartsWith(requiredOs, StringComparison.OrdinalIgnoreCase))))
                    continue;

                string title = (string)item.Elements(ns + "Name").Elements(ns + "Display")
                    .FirstOrDefault(name => (string)name.Attribute("lang") == "en");
                Uri download = DellUri(root, (string)item.Attribute("path"), ".exe");
                foreach (var device in item.Elements().Where(element => element.Name == ns + "SupportedDevices" ||
                    element.Name == ns + "SupportedDCHDevices").Elements(ns + "Device")
                    .Where(device => (string)device.Attribute("infType") == "base"))
                {
                    var ids = device.Elements(ns + "PCIInfo").Select(PciId).Where(id => id != null).ToArray();
                    AddDeviceOffer(offers, inventory, onlyUpdates, "Dell", download, DellHash(item, ns), title,
                        (string)item.Attribute("vendorVersion"), (string)device.Attribute("version"),
                        (string)item.Attribute("dateTime"), null, ids);
                }
            }
            return offers;
        }

        private static string PciId(XElement pci)
        {
            string vendor = (string)pci.Attribute("vendorID") ?? string.Empty;
            string device = (string)pci.Attribute("deviceID") ?? string.Empty;
            string subVendor = (string)pci.Attribute("subVendorID") ?? string.Empty;
            string subDevice = (string)pci.Attribute("subDeviceID") ?? string.Empty;
            if (!HexId.IsMatch(vendor) || !HexId.IsMatch(device))
                return null;
            string hardwareId = @"PCI\VEN_" + vendor + "&DEV_" + device;
            return HexId.IsMatch(subVendor) && HexId.IsMatch(subDevice) ? hardwareId + "&SUBSYS_" + subDevice + subVendor : hardwareId;
        }

        private static Uri DellUri(Uri root, string path, string extension)
        {
            Uri uri;
            return Uri.TryCreate(root, path, out uri) && ManufacturerPackages.IsHttps(uri) && uri.IdnHost == DellHost &&
                uri.AbsolutePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? uri : null;
        }

        private static string DellHash(XElement item, XNamespace ns)
        {
            return (string)item.Elements(ns + "Cryptography").Elements(ns + "Hash")
                .FirstOrDefault(hash => (string)hash.Attribute("algorithm") == "SHA256");
        }

        private static async Task<XDocument> ReadDellCabAsync(Uri uri, string digest, CancellationToken cancellation)
        {
            string directory = Path.Combine(Path.GetTempPath(), "DriversLicense-Dell-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string cab = Path.Combine(directory, "Catalog.cab");
            string xml = Path.Combine(directory, "Catalog.xml");
            try
            {
                await NvidiaPackage.DownloadFileAsync(uri, cab, null, cancellation,
                    target => ManufacturerPackages.IsHttps(target) && target.IdnHost == DellHost, MetadataLimit).ConfigureAwait(false);
                if (digest != null)
                    ManufacturerPackages.VerifyHash(cab, digest);
                using (FileStream input = File.Open(cab, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    NvidiaPackage.VerifyPublisher(input, cab, "Dell Inc.", "Dell Technologies Inc.");
                    cancellation.ThrowIfCancellationRequested();
                    using (Process expand = Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.Combine(Environment.SystemDirectory, "expand.exe"),
                        Arguments = "\"" + cab + "\" \"" + xml + "\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }))
                    {
                        if (expand == null)
                            throw new InvalidOperationException("Não foi possível abrir o catálogo da Dell.");
                        if (!expand.WaitForExit(10000))
                        {
                            expand.Kill();
                            expand.WaitForExit();
                            throw new InvalidDataException("A leitura do catálogo da Dell excedeu o tempo disponível.");
                        }
                        if (expand.ExitCode != 0)
                            throw new InvalidDataException("O Windows não conseguiu extrair o catálogo da Dell.");
                    }
                }
                using (var input = File.OpenRead(xml))
                    return ReadXml(input);
            }
            finally
            {
                try { File.Delete(cab); File.Delete(xml); Directory.Delete(directory); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        internal static async Task<List<DriverOffer>> FindHpAsync(string boardId, int build, IList<InstalledDriver> inventory,
            bool onlyUpdates, CancellationToken cancellation)
        {
            var offers = new List<DriverOffer>();
            if (!HexId.IsMatch(boardId))
                return offers;
            var uri = new Uri("https://workforceexperience.hp.com/services/srs/api/1.1/recommendations/?system-id=" +
                boardId.ToUpperInvariant() + "&os-arch=x64&os-build-number=" + build.ToString(CultureInfo.InvariantCulture) + "&is-ltsc=false");
            byte[] bytes = await GetMetadataAsync(uri, cancellation).ConfigureAwait(false);
            if (bytes == null)
                return offers;
            var serializer = new JavaScriptSerializer { MaxJsonLength = MetadataLimit, RecursionLimit = 32 };
            HpResponse catalog = serializer.Deserialize<HpResponse>(System.Text.Encoding.UTF8.GetString(bytes));
            if (catalog == null || catalog.os == null || catalog.os.buildNumber != build ||
                catalog.os.architecture != "64-bit" || catalog.softpaqs == null || catalog.softpaqDrivers == null)
                return offers;

            var drivers = catalog.softpaqDrivers.Where(item => item != null).ToLookup(item => item.softpaqId);
            foreach (HpPackage item in catalog.softpaqs.Where(item => item != null && item.type == "Driver" && !item.isUwpApp))
            {
                Uri download;
                if (!Uri.TryCreate(item.downloadUrl, UriKind.Absolute, out download))
                    continue;
                foreach (HpDriver driver in drivers[item.softpaqId].Where(group => group.drivers != null).SelectMany(group => group.drivers)
                    .Where(driver => driver != null))
                    AddDeviceOffer(offers, inventory, onlyUpdates, "HP", download, item.softpaqSha256, item.title,
                        item.version, driver.driverVersion, driver.driverDate, driver.@class, new[] { driver.deviceMatchId ?? driver.deviceId });
            }
            return offers;
        }

        internal static async Task<List<DriverOffer>> FindLenovoAsync(string machine, int build, IList<InstalledDriver> inventory,
            bool onlyUpdates, string bios, CancellationToken cancellation)
        {
            var offers = new List<DriverOffer>();
            if (machine.Length != 4 || !MachineType.IsMatch(machine))
                return offers;
            string os = build >= 22000 ? "Win11" : "Win10";
            byte[] bytes = await GetMetadataAsync(new Uri("https://" + LenovoHost + "/catalog/" +
                machine.ToUpperInvariant() + "_" + os + ".xml"), cancellation).ConfigureAwait(false);
            if (bytes == null)
                return offers;
            using (var input = new MemoryStream(bytes))
            using (var limit = new SemaphoreSlim(6))
            {
                XElement index = ReadXml(input).Root;
                var requests = index.Elements("package").Where(item => IsLenovoDriverCategory((string)item.Element("category")))
                    .Select(async item =>
                    {
                        await limit.WaitAsync(cancellation).ConfigureAwait(false);
                        try { return await ReadLenovoPackageAsync(item, inventory, onlyUpdates, build, bios, cancellation).ConfigureAwait(false); }
                        finally { limit.Release(); }
                    });
                foreach (List<DriverOffer> matches in await Task.WhenAll(requests).ConfigureAwait(false))
                    if (matches != null)
                        offers.AddRange(matches);
            }
            return offers;
        }

        private static bool IsLenovoDriverCategory(string category)
        {
            return category != null && new[] { "audio", "networking", "display", "video", "chipset", "bluetooth", "camera",
                "mouse", "keyboard", "input", "card reader", "power management", "storage" }
                .Any(prefix => category.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0) &&
                category.IndexOf("firmware", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static async Task<List<DriverOffer>> ReadLenovoPackageAsync(XElement item, IList<InstalledDriver> inventory,
            bool onlyUpdates, int build, string bios, CancellationToken cancellation)
        {
            XElement checksum = item.Element("checksum");
            Uri location;
            if (checksum == null || !ValidHash(checksum.Value) || !Uri.TryCreate((string)item.Element("location"), UriKind.Absolute, out location) ||
                !ManufacturerPackages.IsHttps(location) || location.IdnHost != LenovoHost ||
                !location.AbsolutePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                return null;

            byte[] bytes = await GetMetadataAsync(location, cancellation).ConfigureAwait(false);
            if (bytes == null)
                return null;
            using (var input = new MemoryStream(bytes))
            {
                using (var hash = System.Security.Cryptography.SHA256.Create())
                {
                    string actual = BitConverter.ToString(hash.ComputeHash(input)).Replace("-", string.Empty);
                    if (!string.Equals(checksum.Value, actual, StringComparison.OrdinalIgnoreCase))
                        return null;
                    input.Position = 0;
                }
                XElement package = ReadXml(input).Root;
                XElement dependencies = package.Element("Dependencies");
                XElement detection = package.Element("DetectVersion");
                var ids = detection == null ? new string[0] : detection.Elements("_PnPID").Select(id => id.Value.Trim()).ToArray();
                var installer = package.Elements("Files").Elements("Installer").Elements("File").SingleOrDefault();
                string filename = (string)(installer == null ? null : installer.Element("Name"));
                if (dependencies == null || detection == null || (string)detection.Attribute("type") != "Inf" ||
                    !DependenciesMatch(dependencies, build, bios, inventory) || filename == null || !LenovoInstaller.IsMatch(filename))
                    return null;

                string title = (string)package.Elements("Title").Elements("Desc").FirstOrDefault(value => (string)value.Attribute("id") == "EN");
                string version = (string)package.Attribute("version");
                var offers = new List<DriverOffer>();
                AddDeviceOffer(offers, inventory, onlyUpdates, "Lenovo", new Uri(location, filename), (string)installer.Element("CRC"),
                    title, version, version, (string)package.Element("SigningTime"), null, ids);
                return offers;
            }
        }

        private static bool DependenciesMatch(XElement rule, int build, string bios, IList<InstalledDriver> inventory)
        {
            switch (rule.Name.LocalName)
            {
                case "Dependencies": case "And":
                    return rule.HasElements && rule.Elements().All(child => DependenciesMatch(child, build, bios, inventory));
                case "Or":
                    return rule.Elements().Any(child => DependenciesMatch(child, build, bios, inventory));
                case "Not":
                    return rule.Elements().Count() == 1 && IsKnownLenovoRule(rule.Elements().First()) &&
                        !DependenciesMatch(rule.Elements().First(), build, bios, inventory);
                case "_OS":
                    string os = build >= 22000 ? "WIN11" : "WIN10";
                    return rule.Elements("OS").Any(value => value.Value.Equals(os, StringComparison.OrdinalIgnoreCase));
                case "_CPUAddressWidth":
                    return rule.Elements("AddressWidth").Any(value => value.Value == "64");
                case "_WindowsBuildVersion":
                    return rule.Elements("Version").Any(value =>
                    {
                        string raw = value.Value.Trim();
                        int version;
                        return int.TryParse(raw.TrimEnd('^'), out version) &&
                            (raw.EndsWith("^", StringComparison.Ordinal) ? build >= version : build == version);
                    });
                case "_Bios":
                    return rule.Elements("Level").Any(level =>
                    {
                        string value = level.Value.Trim();
                        return value == "*" || (value.EndsWith("*", StringComparison.Ordinal) ?
                            bios.StartsWith(value.TrimEnd('*'), StringComparison.OrdinalIgnoreCase) :
                            bios.Equals(value, StringComparison.OrdinalIgnoreCase));
                    });
                case "_PnPID":
                    return inventory.Any(device => ManufacturerPackages.MatchesDevice(device, rule.Value.Trim()));
                default:
                    return false;
            }
        }

        private static bool IsKnownLenovoRule(XElement rule)
        {
            return rule.Name.LocalName == "_PnPID" || rule.Name.LocalName == "_OS" || rule.Name.LocalName == "_CPUAddressWidth" ||
                rule.Name.LocalName == "_WindowsBuildVersion" || rule.Name.LocalName == "_Bios";
        }

        private static void AddDeviceOffer(List<DriverOffer> offers, IList<InstalledDriver> inventory, bool onlyUpdates,
            string vendor, Uri download, string digest, string title, string packageVersion, string deviceVersion,
            string dateText, string driverClass, string[] ids)
        {
            if (download == null || !ValidHash(digest) || string.IsNullOrWhiteSpace(title) || title.Length > 240)
                return;
            foreach (InstalledDriver device in inventory)
            {
                if (!ids.Any(id => ManufacturerPackages.MatchesDevice(device, id)) ||
                    (onlyUpdates && !device.MissingDriver && !ManufacturerPackages.IsNewer(deviceVersion, device.Version, false)))
                    continue;

                DriverOffer offer = ManufacturerPackages.FromOfficialLink(download.AbsoluteUri);
                if (offer == null || offer.Manufacturer != vendor)
                    return;

                DateTime date;
                if (DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date))
                    offer.Date = date.Date;
                offer.Title = title;
                offer.Class = string.IsNullOrWhiteSpace(device.Class) ? driverClass : device.Class;
                offer.AssociatedDevice = device.Device;
                offer.HardwareIds = device.HardwareIds;
                offer.ManufacturerPackage.Version = packageVersion ?? deviceVersion;
                offer.ManufacturerPackage.DriverVersion = deviceVersion;
                offer.ManufacturerPackage.InstalledVersion = device.Version;
                offer.ManufacturerPackage.DownloadHash = digest;
                offer.Devices.Add(device);
                offers.Add(offer);
            }
        }

        private static void AddAmd(List<DriverOffer> offers, IList<InstalledDriver> inventory, bool onlyUpdates)
        {
            if (!inventory.Any(device => (device.HardwareIds ?? new string[0]).Any(id =>
                id.StartsWith(@"PCI\VEN_1022&", StringComparison.OrdinalIgnoreCase))))
                return;
            string board = ReadBoardId();
            Match chipset = AmdChipset.Match(board);
            if (!chipset.Success)
                return;

            string installed = ManufacturerPackages.ReadAmdChipsetVersion();
            const string version = "8.09.28.1124";
            if (onlyUpdates && !ManufacturerPackages.IsNewer(version, installed, false))
                return;
            DriverOffer offer = ManufacturerPackages.FromOfficialLink("https://drivers.amd.com/drivers/amd_chipset_software_8.09.28.1124.exe");
            offer.Title = "AMD Ryzen Chipset Drivers";
            offer.AssociatedDevice = "AMD " + chipset.Groups[1].Value.ToUpperInvariant() + " · " + board;
            offer.Class = "System";
            offer.Date = new DateTime(2026, 10, 21);
            offer.ManufacturerPackage.Version = version;
            offer.ManufacturerPackage.InstalledVersion = installed;
            offer.ManufacturerPackage.DownloadHash = "57ED1AA9AC842D8AD0064FCED2524E9AAB66C5BA74ED90C0135B34F53278AC7F";
            offers.Add(offer);
        }

        private static void AddIntel(List<DriverOffer> offers, IList<InstalledDriver> inventory, bool onlyUpdates)
        {
            var devices = inventory.Where(device => ManufacturerPackages.MatchesDevice(device, @"PCI\VEN_8086&DEV_2725") ||
                (string.Equals(device.Class, "Net", StringComparison.OrdinalIgnoreCase) && IntelWifi.IsMatch(device.Device ?? string.Empty) &&
                (device.HardwareIds ?? new string[0]).Any(id => id.StartsWith(@"PCI\VEN_8086&DEV_", StringComparison.OrdinalIgnoreCase))))
                .ToArray();
            string[] matches = devices.SelectMany(device => device.HardwareIds ?? new string[0]).ToArray();
            AddDeviceOffer(offers, inventory, onlyUpdates, "Intel",
                new Uri("https://downloadmirror.intel.com/929786/WiFi-24.80.0-Driver64-Win10-Win11.exe"),
                "40D2F714830B406CCC1F9C00547AD6090F164F928748E38F27A1C7C0EF5ED5EA",
                "Intel Wireless Wi-Fi Drivers", "24.80.0", "24.80.0.2", "2026-10-20", "Net", matches);
        }

        private static string ReadBoardId()
        {
            return ReadWmiValue("SELECT Product FROM Win32_BaseBoard", "Product");
        }

        private static string ReadBiosVersion()
        {
            return ReadWmiValue("SELECT SMBIOSBIOSVersion FROM Win32_BIOS", "SMBIOSBIOSVersion");
        }

        private static string ReadWmiValue(string statement, string property)
        {
            using (var query = new ManagementObjectSearcher(statement))
            using (var rows = query.Get())
            foreach (ManagementObject row in rows)
            using (row)
                return Convert.ToString(row[property]).Trim();
            return string.Empty;
        }

        private static async Task<byte[]> GetMetadataAsync(Uri uri, CancellationToken cancellation)
        {
            var request = WebRequest.CreateHttp(uri);
            request.AllowAutoRedirect = false;
            using (cancellation.Register(request.Abort))
            {
                try
                {
                    using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                    {
                        if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > MetadataLimit)
                            throw new InvalidDataException("O servidor não retornou um catálogo válido.");
                        using (Stream stream = response.GetResponseStream())
                        using (var content = new MemoryStream())
                        {
                            if (stream == null)
                                throw new InvalidDataException("O catálogo está vazio.");
                            byte[] buffer = new byte[32768];
                            int read;
                            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) > 0)
                            {
                                if (content.Length + read > MetadataLimit)
                                    throw new InvalidDataException("O catálogo é maior que o tamanho aceito.");
                                content.Write(buffer, 0, read);
                            }
                            return content.ToArray();
                        }
                    }
                }
                catch (WebException error)
                {
                    cancellation.ThrowIfCancellationRequested();
                    using (var response = error.Response as HttpWebResponse)
                    {
                        if (response != null && response.StatusCode == HttpStatusCode.NotFound)
                            return null;
                    }
                    throw;
                }
            }
        }

        private static XDocument ReadXml(Stream stream)
        {
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MetadataLimit
            }))
                return XDocument.Load(reader);
        }

        private static bool ValidHash(string digest)
        {
            return digest != null && Sha256.IsMatch(digest);
        }

        private static Version ParseVersion(string text)
        {
            Version version;
            return Version.TryParse(text, out version) ? version : null;
        }

        private sealed class HpResponse
        {
            public HpOs os { get; set; }
            public HpPackage[] softpaqs { get; set; }
            public HpDrivers[] softpaqDrivers { get; set; }
        }

        private sealed class HpOs
        {
            public int buildNumber { get; set; }
            public string architecture { get; set; }
        }

        private sealed class HpPackage
        {
            public int softpaqId { get; set; }
            public string title { get; set; }
            public string version { get; set; }
            public string softpaqSha256 { get; set; }
            public string type { get; set; }
            public string downloadUrl { get; set; }
            public bool isUwpApp { get; set; }
        }

        private sealed class HpDrivers
        {
            public int softpaqId { get; set; }
            public HpDriver[] drivers { get; set; }
        }

        private sealed class HpDriver
        {
            public string @class { get; set; }
            public string driverDate { get; set; }
            public string driverVersion { get; set; }
            public string deviceId { get; set; }
            public string deviceMatchId { get; set; }
        }
    }
}
