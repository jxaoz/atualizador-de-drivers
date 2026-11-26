using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DriverUpdater
{
    internal sealed class DriverRow
    {
        public InstalledDriver Device;
        public DriverOffer Offer;
    }

    internal static class DriverSearch
    {
        public static async Task<DriverScan> ScanAsync(Task<List<InstalledDriver>> inventory, bool videoOnly)
        {
            Task<DriverScan> windows = Task.Run(() => ScanWindows());
            DriverScan result = await windows.ConfigureAwait(false);
            try { result.Inventory = await inventory.ConfigureAwait(false); }
            catch (Exception error)
            {
                AddWarning(result, "Inventário do computador: " + DriverServices.DescribeError(error));
                if (videoOnly)
                    result.Offers.RemoveAll(offer => !IsVideoOffer(offer));
                return result;
            }

            result.MissingDrivers = result.Inventory.Count(device => device.MissingDriver);
            var covered = SelectWindowsOffers(result, videoOnly);
            if (result.Inventory.Any(device => !covered.Contains(device) &&
                (!videoOnly || VideoDriversWindow.IsGraphicsClass(device.Class))))
                await AddManufacturerOffersAsync(result, covered, videoOnly).ConfigureAwait(false);
            return result;
        }

        internal static HashSet<InstalledDriver> SelectWindowsOffers(DriverScan result, bool videoOnly)
        {
            var covered = new HashSet<InstalledDriver>();
            var available = new List<DriverOffer>();
            foreach (DriverOffer offer in result.Offers.OrderBy(offer => offer.Optional).ThenByDescending(offer => offer.Date))
            {
                var matches = result.Inventory.Where(device => WindowsTargets(offer, device).Any()).ToList();
                if (videoOnly && !IsVideoOffer(offer) && !matches.Any(device => VideoDriversWindow.IsGraphicsClass(device.Class)))
                    continue;

                foreach (InstalledDriver device in matches)
                {
                    if (WindowsTargets(offer, device).Any(target => device.MissingDriver || !device.Date.HasValue ||
                        !target.Date.HasValue || target.Date.Value.Date >= device.Date.Value.Date) && covered.Add(device))
                        offer.Devices.Add(device);
                }

                if (offer.Devices.Count > 0 || matches.Count == 0)
                    available.Add(offer);
            }
            result.Offers = available;
            return covered;
        }

        private static IEnumerable<WindowsDriverTarget> WindowsTargets(DriverOffer offer, InstalledDriver device)
        {
            if (offer.WindowsTargets == null || device.HardwareIds == null)
                return Enumerable.Empty<WindowsDriverTarget>();
            return offer.WindowsTargets.Where(target =>
                device.HardwareIds.Contains(target.HardwareId, StringComparer.OrdinalIgnoreCase) &&
                (device.MissingDriver || string.IsNullOrWhiteSpace(target.Class) || string.IsNullOrWhiteSpace(device.Class) ||
                    string.Equals(target.Class, device.Class, StringComparison.OrdinalIgnoreCase)));
        }

        public static IEnumerable<DriverRow> BuildRows(IList<InstalledDriver> inventory, IList<DriverOffer> offers, bool videoOnly)
        {
            var byDevice = offers.SelectMany(offer => offer.Devices.Select(device => new { device, offer }))
                .ToLookup(pair => pair.device.DeviceInstanceId, pair => pair.offer, StringComparer.OrdinalIgnoreCase);
            var rows = new List<DriverRow>();
            var placed = new HashSet<DriverOffer>();
            foreach (InstalledDriver device in inventory)
            {
                if (videoOnly && !VideoDriversWindow.IsGraphicsClass(device.Class) && !byDevice.Contains(device.DeviceInstanceId))
                    continue;
                foreach (DriverOffer offer in byDevice[device.DeviceInstanceId].DefaultIfEmpty())
                {
                    rows.Add(new DriverRow { Device = device, Offer = offer });
                    if (offer != null)
                        placed.Add(offer);
                }
            }
            rows.AddRange(offers.Where(offer => !placed.Contains(offer)).Select(offer => new DriverRow { Offer = offer }));
            return rows.OrderBy(row => row.Offer == null).ThenBy(row => row.Device == null || !row.Device.MissingDriver)
                .ThenBy(row => row.Device == null ? row.Offer.Title : row.Device.Device, StringComparer.CurrentCultureIgnoreCase);
        }

        public static bool IsVideoOffer(DriverOffer offer)
        {
            return VideoDriversWindow.IsGraphicsClass(DriverCategoryIcons.ResolveOfferClass(offer.Class, offer.Title));
        }

        public static string GetSource(DriverOffer offer)
        {
            if (offer.ManufacturerPackage != null)
                return offer.ManufacturerPackage.Source;
            if (offer.Nvidia == null)
                return "Windows Update";
            return offer.FromNvidiaApp ? "NVIDIA App" : "NVIDIA";
        }

        private static DriverScan ScanWindows()
        {
            try
            {
                return DriverServices.Scan();
            }
            catch (Exception error)
            {
                return new DriverScan { Partial = true, Warnings = "Windows Update: " + DriverServices.DescribeError(error) };
            }
        }

        private static async Task AddManufacturerOffersAsync(DriverScan result, HashSet<InstalledDriver> covered, bool videoOnly)
        {
            try
            {
                DriverScan catalog = await OfficialManufacturerCatalogs.FindForPcAsync(result.Inventory, true).ConfigureAwait(false);
                AddCatalogFallbacks(result, catalog.Offers, covered, videoOnly);
                if (catalog.Partial)
                    AddWarning(result, catalog.Warnings);
            }
            catch (Exception error) { AddWarning(result, "Catálogo do fabricante: " + DriverServices.DescribeError(error)); }
            var adapters = result.Inventory.Where(device => !covered.Contains(device) && NvidiaCatalog.IsSupportedAdapter(device)).ToList();
            if (adapters.Count == 0)
                return;

            // Prefer a driver offered by the opted-in NVIDIA App if the public catalog also verifies it.
            string recommendation = await NvidiaAppClient.TryGetRecommendedVersionAsync(false, CancellationToken.None).ConfigureAwait(false);
            var packages = new Dictionary<Uri, DriverOffer>();
            foreach (InstalledDriver adapter in adapters)
            {
                try
                {
                    NvidiaDriverOffer driver = await NvidiaCatalog.SearchAsync(adapter, false, CancellationToken.None,
                        recommendation).ConfigureAwait(false);
                    if (driver == null)
                    {
                        AddWarning(result, "A NVIDIA não confirmou uma oferta no catálogo para " + adapter.Device + ".");
                        continue;
                    }
                    if (driver.IsNewer != true && !(driver.IsNewer == null && adapter.MissingDriver))
                        continue;

                    DriverOffer offer;
                    if (packages.TryGetValue(driver.DownloadUrl, out offer))
                    {
                        if (!offer.AssociatedDevice.Split(',').Any(name => string.Equals(name.Trim(), adapter.Device, StringComparison.CurrentCultureIgnoreCase)))
                            offer.AssociatedDevice += ", " + adapter.Device;
                        offer.HardwareIds = offer.HardwareIds.Concat(adapter.HardwareIds ?? new string[0])
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                        offer.Devices.Add(adapter);
                        continue;
                    }

                    offer = new DriverOffer
                    {
                        Title = "NVIDIA GeForce Game Ready " + driver.Version,
                        Manufacturer = "NVIDIA",
                        Class = "Display",
                        Date = driver.ReleaseDate,
                        HardwareIds = adapter.HardwareIds ?? new string[0],
                        Nvidia = driver,
                        FromNvidiaApp = string.Equals(recommendation, driver.Version, StringComparison.Ordinal),
                        AssociatedDevice = adapter.Device
                    };
                    offer.Devices.Add(adapter);
                    packages.Add(driver.DownloadUrl, offer);
                    result.Offers.Add(offer);
                }
                catch (Exception error)
                {
                    AddWarning(result, "NVIDIA: " + DriverServices.DescribeError(error));
                }
            }
        }

        internal static void AddCatalogFallbacks(DriverScan result, IEnumerable<DriverOffer> offers, HashSet<InstalledDriver> covered, bool videoOnly)
        {
            foreach (DriverOffer offer in offers.Where(offer => !videoOnly || IsVideoOffer(offer) ||
                offer.Devices.Any(device => VideoDriversWindow.IsGraphicsClass(device.Class)))
                .OrderBy(offer => offer.Manufacturer == "AMD" || offer.Manufacturer == "Intel"))
            {
                if (offer.Devices.Count > 0)
                {
                    offer.Devices.RemoveAll(device => !covered.Add(device));
                    if (offer.Devices.Count == 0)
                        continue;
                }
                result.Offers.Add(offer);
            }
        }

        private static void AddWarning(DriverScan result, string warning)
        {
            result.Partial = true;
            result.Warnings = JoinWarnings(result.Warnings, warning);
        }

        private static string JoinWarnings(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first))
                return second;
            if (string.IsNullOrWhiteSpace(second) || first == second)
                return first;
            return first + Environment.NewLine + second;
        }
    }
}
