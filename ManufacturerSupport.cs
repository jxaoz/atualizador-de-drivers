using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;

namespace DriverUpdater
{
    internal static class ManufacturerSupport
    {
        private static readonly Dictionary<string, string> SupportPages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Dell", "https://www.dell.com/support/home/pt-br?app=drivers" },
            { "Lenovo", "https://pcsupport.lenovo.com/br/pt/" },
            { "HP", "https://support.hp.com/br-pt/drivers" },
            { "ASUS", "https://www.asus.com/br/support/download-center/" },
            { "ASUSTeK", "https://www.asus.com/br/support/download-center/" },
            { "Gigabyte", "https://www.gigabyte.com/Support" },
            { "MSI", "https://www.msi.com/support" },
            { "Micro-Star", "https://www.msi.com/support" },
            { "NVIDIA", "https://www.nvidia.com/pt-br/drivers/" },
            { "Intel", "https://www.intel.com/content/www/us/en/download-center/home.html" },
            { "AMD", "https://www.amd.com/pt/support/download/drivers.html" },
            { "Advanced Micro Devices", "https://www.amd.com/pt/support/download/drivers.html" }
        };

        public static Uri Find(string manufacturer)
        {
            return FindPage(manufacturer) ?? FindPcPage("Win32_ComputerSystem") ?? FindPcPage("Win32_BaseBoard");
        }

        private static Uri FindPage(string manufacturer)
        {
            string name = (manufacturer ?? string.Empty).Trim();
            foreach (var entry in SupportPages)
            {
                if (name.StartsWith(entry.Key, StringComparison.OrdinalIgnoreCase) &&
                    (name.Length == entry.Key.Length || !char.IsLetterOrDigit(name[entry.Key.Length])))
                    return new Uri(entry.Value);
            }
            return null;
        }

        private static Uri FindPcPage(string wmiClass)
        {
            try
            {
                using (var query = new ManagementObjectSearcher("SELECT Manufacturer FROM " + wmiClass))
                using (var results = query.Get())
                {
                    foreach (ManagementObject result in results)
                    {
                        using (result)
                        {
                            Uri page = FindPage(result["Manufacturer"] as string);
                            if (page != null)
                                return page;
                        }
                    }
                }
            }
            catch (ManagementException) { }
            catch (UnauthorizedAccessException) { }
            catch (COMException) { }
            return null;
        }
    }
}
