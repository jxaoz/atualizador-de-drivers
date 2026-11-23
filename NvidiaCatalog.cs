using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DriverUpdater
{
    internal sealed class NvidiaDriverOffer
    {
        public string Version, InstalledVersion, Title, ProductName;
        public DateTime? ReleaseDate;
        public Uri DownloadUrl, DetailsUrl;
        public bool Studio;
        public bool? IsNewer;
    }

    internal static class NvidiaCatalog
    {
        // These endpoints are used by NVIDIA's public GeForce driver search.
        private const string ProductEndpoint = "https://gfwsl.geforce.com/nvidia_web_services/controller.php?" +
            "com.nvidia.services.Drivers.getMenuArrayProductOperatingSystemMetaData/";
        private const string ProductQuery = "{\"pt\":\"0\",\"pst\":\"0\",\"d2\":\"0\",\"d4\":\"0\",\"d5\":\"0\",\"driverType\":\"all\",\"sa\":\"0\"}";
        private const string DriverEndpoint = "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php";
        private const int MaxResponseBytes = 12 * 1024 * 1024;
        private static readonly Regex NvidiaPci = new Regex(@"^PCI\\VEN_10DE&DEV_[0-9A-F]{4}(?:&|\\|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex PackagePath = new Regex(@"^/Windows/([0-9]{2,4}\.[0-9]{2})/([A-Za-z0-9._-]+)-whql\.exe$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex DriverVersion = new Regex(@"^[0-9]{2,4}\.[0-9]{2}$", RegexOptions.CultureInvariant);
        private static readonly HttpClient CatalogClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            SslProtocols = SslProtocols.Tls12
        });

        public static bool IsSupportedAdapter(InstalledDriver driver)
        {
            if (driver == null || !VideoDriversWindow.IsGraphicsClass(driver.Class) ||
                !NormalizeName(driver.Device).StartsWith("GeForce ", StringComparison.OrdinalIgnoreCase) ||
                Environment.OSVersion.Version.Major != 10 || RuntimeInformation.OSArchitecture != Architecture.X64)
                return false;

            if (IsNvidiaPci(driver.DeviceInstanceId))
                return true;
            if (driver.HardwareIds != null)
                foreach (string id in driver.HardwareIds)
                    if (IsNvidiaPci(id))
                        return true;
            return false;
        }

        private static bool IsNvidiaPci(string id)
        {
            return id != null && NvidiaPci.IsMatch(id);
        }

        public static bool IsStillApplicable(InstalledDriver adapter, NvidiaDriverOffer offer)
        {
            int offered;
            if (!IsSupportedAdapter(adapter) || !SameText(NormalizeName(adapter.Device), NormalizeName(offer.ProductName)) ||
                !TryDriverVersion(offer.Version, out offered) || !IsTrustedDownloadUrl(offer.DownloadUrl))
                return false;

            int? installed = ReadInstalledVersion(adapter);
            return !installed.HasValue || offered > installed.Value;
        }

        public static async Task<NvidiaDriverOffer> SearchAsync(InstalledDriver driver, bool studio, CancellationToken token,
            string preferredVersion = null)
        {
            token.ThrowIfCancellationRequested();
            if (!IsSupportedAdapter(driver))
                return null;

            string platform = Environment.OSVersion.Version.Build >= 22000 ? "Windows 11" : "Windows 10 64-bit";
            var products = await GetJsonAsync(new Uri(ProductEndpoint + Uri.EscapeDataString(ProductQuery)), token)
                .ConfigureAwait(false);
            Product selected = FindMatchingProduct(products, NormalizeName(driver.Device), platform, token);
            if (selected == null)
                return null;

            string query = string.Format(CultureInfo.InvariantCulture,
                "?func=DriverManualLookup&psid={0}&pfid={1}&osID={2}&languageCode=1033&isWHQL=0&beta=0&dch=1" +
                "&qnf=null&ctk=null&sort1=0&numberOfResults=5&dltype=-1&upCRD={3}",
                selected.Series, selected.Family, selected.OperatingSystem, studio ? 1 : 0);
            var response = await GetJsonAsync(new Uri(DriverEndpoint + query), token).ConfigureAwait(false);
            return SelectDriver(response, selected.Name, driver, studio, preferredVersion);
        }

        private static Product FindMatchingProduct(IDictionary<string, object> root, string device, string platform,
            CancellationToken token)
        {
            IDictionary<string, object> products = GetValue(root, "ProductNames") as IDictionary<string, object>;
            if (products == null)
                throw new InvalidDataException("O catálogo de produtos da NVIDIA retornou um formato desconhecido.");

            Product match = null;
            foreach (object value in products.Values)
            {
                token.ThrowIfCancellationRequested();
                var row = value as IDictionary<string, object>;
                if (row == null || GetText(row, "ProductTypeID") != "1")
                    continue;

                string[] parts = GetText(row, "ProductNameMeta").Split('|');
                if (parts.Length != 3 || !SameText(NormalizeName(parts[1]), device) || !SameText(parts[2].Trim(), platform))
                    continue;

                int series, family, operatingSystem;
                if (!TryPositiveId(row, "ProductSeriesID", out series) || !TryPositiveId(row, "ProductFamilyID", out family) ||
                    !TryPositiveId(row, "OperatingSystemID", out operatingSystem))
                    continue;
                // Different desktop and notebook products can share a display name.
                if (match != null && (match.Series != series || match.Family != family || match.OperatingSystem != operatingSystem))
                    return null;
                match = new Product(parts[1].Trim(), series, family, operatingSystem);
            }
            return match;
        }

        private static NvidiaDriverOffer SelectDriver(IDictionary<string, object> root, string productName,
            InstalledDriver driver, bool studio, string preferredVersion)
        {
            int count;
            if (!int.TryParse(GetText(root, "Success"), NumberStyles.None, CultureInfo.InvariantCulture, out count))
                throw new InvalidDataException("O catálogo de drivers da NVIDIA retornou um formato desconhecido.");
            if (count == 0)
                return null;

            object[] packages = GetValue(root, "IDS") as object[];
            if (packages == null || packages.Length == 0)
                throw new InvalidDataException("A NVIDIA informou resultados sem enviar a lista de pacotes.");

            int? installed = ReadInstalledVersion(driver);
            int preferred;
            bool preferApp = TryDriverVersion((preferredVersion ?? string.Empty).Trim(), out preferred) &&
                (!installed.HasValue || preferred > installed.Value);
            int newestVersion = 0;
            NvidiaDriverOffer newest = null;
            foreach (object package in packages)
            {
                var selection = package as IDictionary<string, object>;
                var info = selection == null ? null : GetValue(selection, "downloadInfo") as IDictionary<string, object>;
                if (info == null || GetText(info, "IsActive") != "1" || GetText(info, "IsBeta") != "0" ||
                    GetText(info, "IsDC") != "1" || GetText(info, "IsCRD") != (studio ? "1" : "0"))
                    continue;

                string version = GetText(info, "Version");
                int versionNumber;
                Uri download, details;
                if (!TryDriverVersion(version, out versionNumber) ||
                    !Uri.TryCreate(GetText(info, "DownloadURL"), UriKind.Absolute, out download) || !IsTrustedDownloadUrl(download) ||
                    !download.AbsolutePath.StartsWith("/Windows/" + version + "/" + version + "-", StringComparison.OrdinalIgnoreCase) ||
                    !Uri.TryCreate(GetText(info, "DetailsURL"), UriKind.Absolute, out details) || !IsTrustedDetailsUrl(details))
                    continue;

                bool isPreferred = preferApp && preferred == versionNumber;
                if (!isPreferred && versionNumber <= newestVersion)
                    continue;

                DateTime releaseDate;
                bool hasDate = DateTime.TryParse(Uri.UnescapeDataString(GetText(info, "ReleaseDateTime")), CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out releaseDate);
                newestVersion = versionNumber;
                newest = new NvidiaDriverOffer
                {
                    Version = version,
                    InstalledVersion = installed.HasValue ? FormatDriverVersion(installed.Value) : null,
                    Title = studio ? "NVIDIA Studio Driver" : "GeForce Game Ready Driver",
                    ProductName = productName,
                    ReleaseDate = hasDate ? (DateTime?)releaseDate.Date : null,
                    DownloadUrl = download,
                    DetailsUrl = details,
                    Studio = studio,
                    IsNewer = installed.HasValue ? (bool?)(versionNumber > installed.Value) : null
                };
                if (isPreferred)
                    return newest;
            }
            return newest;
        }

        public static bool IsTrustedDownloadUrl(Uri url)
        {
            if (!IsHttps(url) || url.Query.Length != 0 || url.Fragment.Length != 0)
                return false;

            return (SameText(url.Host, "us.download.nvidia.com") || SameText(url.Host, "international.download.nvidia.com") ||
                SameText(url.Host, "download.nvidia.com")) && PackagePath.IsMatch(url.AbsolutePath);
        }

        private static bool IsTrustedDetailsUrl(Uri url)
        {
            if (!IsHttps(url) || !SameText(url.Host, "www.nvidia.com"))
                return false;

            return url.AbsolutePath.IndexOf("/drivers/details/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                url.AbsolutePath.StartsWith("/Download/driverResults.aspx/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHttps(Uri url)
        {
            return url != null && url.IsAbsoluteUri && url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort && url.UserInfo.Length == 0;
        }

        private static int? ReadInstalledVersion(InstalledDriver driver)
        {
            Version windowsVersion;
            if (!SameText((driver.Manufacturer ?? string.Empty).Trim(), "NVIDIA") ||
                !Version.TryParse(driver.Version, out windowsVersion) || windowsVersion.Build < 0 ||
                windowsVersion.Revision < 0 || windowsVersion.Revision > 9999)
                return null;

            // Windows 32.0.16.2101 represents NVIDIA release 621.01.
            return windowsVersion.Build % 10 * 10000 + windowsVersion.Revision;
        }

        private static bool TryDriverVersion(string version, out int number)
        {
            number = 0;
            return DriverVersion.IsMatch(version) && int.TryParse(version.Replace(".", string.Empty), NumberStyles.None,
                CultureInfo.InvariantCulture, out number);
        }

        private static string FormatDriverVersion(int version)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1:00}", version / 100, version % 100);
        }

        private static async Task<IDictionary<string, object>> GetJsonAsync(Uri uri, CancellationToken token)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                using (HttpResponseMessage result = await CatalogClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead,
                    deadline.Token).ConfigureAwait(false))
                {
                    result.EnsureSuccessStatusCode();
                    if (result.Content.Headers.ContentLength > MaxResponseBytes)
                        throw new InvalidDataException("A resposta do catálogo da NVIDIA excedeu o tamanho esperado.");

                    using (Stream input = await result.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var buffer = new MemoryStream())
                    {
                        // Disposing the response also interrupts reads on older HTTP handlers.
                        using (deadline.Token.Register(result.Dispose))
                        {
                            var chunk = new byte[32768];
                            int length;
                            while ((length = await input.ReadAsync(chunk, 0, chunk.Length, deadline.Token).ConfigureAwait(false)) > 0)
                            {
                                if (buffer.Length + length > MaxResponseBytes)
                                    throw new InvalidDataException("A resposta do catálogo da NVIDIA excedeu o tamanho esperado.");
                                buffer.Write(chunk, 0, length);
                            }
                        }

                        deadline.Token.ThrowIfCancellationRequested();
                        var serializer = new JavaScriptSerializer { MaxJsonLength = MaxResponseBytes };
                        var data = serializer.DeserializeObject(Encoding.UTF8.GetString(buffer.ToArray())) as IDictionary<string, object>;
                        if (data == null)
                            throw new InvalidDataException("A NVIDIA não retornou dados de catálogo reconhecíveis.");
                        return data;
                    }
                }
            }
        }

        private static string NormalizeName(string value)
        {
            string name = Regex.Replace((value ?? string.Empty).Trim(), @"\s+", " ");
            return name.StartsWith("NVIDIA ", StringComparison.OrdinalIgnoreCase) ? name.Substring(7) : name;
        }

        private static bool SameText(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static object GetValue(IDictionary<string, object> row, string key)
        {
            object value;
            return row.TryGetValue(key, out value) ? value : null;
        }

        private static string GetText(IDictionary<string, object> row, string key)
        {
            return Convert.ToString(GetValue(row, key), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static bool TryPositiveId(IDictionary<string, object> row, string key, out int number)
        {
            return int.TryParse(GetText(row, key), NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
        }

        private sealed class Product
        {
            public readonly string Name;
            public readonly int Series, Family, OperatingSystem;

            public Product(string name, int series, int family, int operatingSystem)
            {
                Name = name;
                Series = series;
                Family = family;
                OperatingSystem = operatingSystem;
            }
        }
    }
}
