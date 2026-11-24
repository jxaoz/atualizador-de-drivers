using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DriverUpdater
{
    internal static class DriverSearch
    {
        public static async Task<DriverScan> ScanAsync(Task<List<InstalledDriver>> inventory, bool videoOnly)
        {
            Task<DriverScan> windows = Task.Run(() => ScanWindows(videoOnly));
            Task<DriverScan> manufacturers = ScanManufacturersAsync(inventory, videoOnly);
            await Task.WhenAll(windows, manufacturers).ConfigureAwait(false);
            DriverScan windowsResult = await windows.ConfigureAwait(false);
            DriverScan manufacturerResult = await manufacturers.ConfigureAwait(false);

            windowsResult.Offers.AddRange(manufacturerResult.Offers);
            windowsResult.Offers.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.Title, right.Title));
            windowsResult.Partial |= manufacturerResult.Partial;
            windowsResult.MissingDrivers = manufacturerResult.MissingDrivers;
            windowsResult.Warnings = JoinWarnings(windowsResult.Warnings, manufacturerResult.Warnings);
            return windowsResult;
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

        private static DriverScan ScanWindows(bool videoOnly)
        {
            try
            {
                DriverScan result = DriverServices.Scan();
                if (videoOnly)
                    result.Offers.RemoveAll(offer => !IsVideoOffer(offer));
                return result;
            }
            catch (Exception error)
            {
                return new DriverScan { Partial = true, Warnings = "Windows Update: " + DriverServices.DescribeError(error) };
            }
        }

        private static async Task<DriverScan> ScanManufacturersAsync(Task<List<InstalledDriver>> inventory, bool videoOnly)
        {
            List<InstalledDriver> installed;
            try { installed = await inventory.ConfigureAwait(false); }
            catch (Exception error)
            {
                return new DriverScan { Partial = true, Warnings = "Inventário para a consulta de fabricantes: " + DriverServices.DescribeError(error) };
            }

            var result = new DriverScan { MissingDrivers = installed.Count(adapter => adapter.MissingDriver) };
            if (!videoOnly)
            {
                try { result.Offers.AddRange(ManufacturerPackages.FindForPc(installed, true)); }
                catch (Exception error) { AddWarning(result, "Catálogo do fabricante: " + DriverServices.DescribeError(error)); }
            }
            var adapters = installed.Where(NvidiaCatalog.IsSupportedAdapter).ToList();
            if (adapters.Count == 0)
                return result;

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
                    if (driver.IsNewer == false)
                        continue;

                    DriverOffer offer;
                    if (packages.TryGetValue(driver.DownloadUrl, out offer))
                    {
                        if (!offer.AssociatedDevice.Split(',').Any(name => string.Equals(name.Trim(), adapter.Device, StringComparison.CurrentCultureIgnoreCase)))
                            offer.AssociatedDevice += ", " + adapter.Device;
                        offer.HardwareIds = offer.HardwareIds.Concat(adapter.HardwareIds ?? new string[0])
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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
                    packages.Add(driver.DownloadUrl, offer);
                    result.Offers.Add(offer);
                }
                catch (Exception error)
                {
                    AddWarning(result, "NVIDIA: " + DriverServices.DescribeError(error));
                }
            }
            return result;
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
