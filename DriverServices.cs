using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace DriverUpdater
{
    internal sealed class InstalledDriver
    {
        public string Device, Manufacturer, Version, DeviceInstanceId, InfName, Class, Signer;
        public DateTime? Date;
        public string[] HardwareIds;
        public bool? Signed;
    }

    internal sealed class DriverOffer
    {
        public string Title, Manufacturer, Class, Id;
        public int Revision;
        public DateTime? Date;
        public string[] HardwareIds;
        public bool Optional;
    }

    internal sealed class DriverScan
    {
        public List<DriverOffer> Offers = new List<DriverOffer>();
        public bool Partial;
        public string Warnings;
    }

    internal sealed class DriverResult
    {
        public string Title, Detail;
        public bool Installed, RebootRequired;
    }

    internal static class DriverServices
    {
        private const string DriverCriteria = "IsInstalled=0 and IsHidden=0 and Type='Driver'";
        private const string Criteria = "(" + DriverCriteria + " and DeploymentAction='Installation') or (" +
            DriverCriteria + " and DeploymentAction='OptionalInstallation')";
        private const int Succeeded = 2;
        private const int SucceededWithErrors = 3;
        private const int Aborted = 5;
        private const int OptionalInstallation = 4;

        private sealed class PresentDevice
        {
            public string Name;
            public HashSet<string> HardwareIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public static List<InstalledDriver> ReadInventory()
        {
            var present = new Dictionary<string, PresentDevice>(StringComparer.OrdinalIgnoreCase);
            using (var query = new ManagementObjectSearcher(
                "SELECT PNPDeviceID, Name, HardwareID, CompatibleID FROM Win32_PnPEntity WHERE Present=TRUE"))
            using (var devices = query.Get())
            {
                foreach (ManagementObject device in devices)
                {
                    using (device)
                    {
                        var id = ReadText(device["PNPDeviceID"]);
                        if (id.Length == 0)
                            continue;

                        var entry = new PresentDevice { Name = ReadText(device["Name"]) };
                        AddHardwareIds(entry.HardwareIds, device["HardwareID"]);
                        AddHardwareIds(entry.HardwareIds, device["CompatibleID"]);
                        present[id] = entry;
                    }
                }
            }

            var drivers = new List<InstalledDriver>();
            using (var query = new ManagementObjectSearcher(
                "SELECT DeviceID, DeviceName, FriendlyName, Manufacturer, DriverProviderName, DriverVersion, DriverDate, " +
                "HardwareID, CompatID, InfName, DeviceClass, IsSigned, Signer FROM Win32_PnPSignedDriver"))
            using (var packages = query.Get())
            {
                foreach (ManagementObject package in packages)
                {
                    using (package)
                    {
                        var id = ReadText(package["DeviceID"]);
                        PresentDevice device;
                        if (!present.TryGetValue(id, out device))
                            continue;

                        var hardwareIds = new HashSet<string>(device.HardwareIds, StringComparer.OrdinalIgnoreCase);
                        AddHardwareIds(hardwareIds, package["HardwareID"]);
                        AddHardwareIds(hardwareIds, package["CompatID"]);
                        drivers.Add(new InstalledDriver
                        {
                            Device = FirstText(device.Name, package["FriendlyName"], package["DeviceName"], id),
                            Manufacturer = FirstText(package["DriverProviderName"], package["Manufacturer"]),
                            Version = ReadText(package["DriverVersion"]),
                            Date = ParseDriverDate(ReadText(package["DriverDate"])),
                            HardwareIds = CopyHardwareIds(hardwareIds),
                            DeviceInstanceId = id,
                            InfName = ReadText(package["InfName"]),
                            Class = ReadText(package["DeviceClass"]),
                            Signed = package["IsSigned"] as bool?,
                            Signer = ReadText(package["Signer"])
                        });
                    }
                }
            }

            drivers.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.Device, right.Device));
            return drivers;
        }

        public static DriverScan Scan()
        {
            var scan = new DriverScan();
            dynamic session = CreateSession();
            dynamic search = Search(session);
            var warnings = ReadWarnings(search);
            scan.Partial = (int)search.ResultCode == SucceededWithErrors || warnings.Length > 0;
            scan.Warnings = warnings;

            dynamic updates = search.Updates;
            var seen = new HashSet<Tuple<Guid, int>>();
            for (int index = 0; index < (int)updates.Count; index++)
            {
                dynamic update = updates.Item(index);
                Guid id;
                int revision;
                if (!TryReadIdentity(update, out id, out revision))
                {
                    scan.Partial = true;
                    continue;
                }
                if (!seen.Add(Tuple.Create(id, revision)))
                    continue;

                scan.Offers.Add(new DriverOffer
                {
                    Id = id.ToString("D"),
                    Revision = revision,
                    Title = FirstText((object)update.Title, "Atualização de driver"),
                    Manufacturer = ReadText(ReadDriverProperty(() => update.DriverManufacturer)),
                    Class = ReadText(ReadDriverProperty(() => update.DriverClass)),
                    Date = ReadOfferDate(ReadDriverProperty(() => update.DriverVerDate)),
                    HardwareIds = ReadOfferHardwareIds(update),
                    Optional = Equals(ReadDriverProperty(() => update.BrowseOnly), true) ||
                        Equals(ReadDriverProperty(() => update.DeploymentAction), OptionalInstallation)
                });
            }

            scan.Offers.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.Title, right.Title));
            return scan;
        }

        public static List<DriverResult> Install(IList<DriverOffer> selections, Action<string> progress,
            Func<string, string, bool> acceptLicense)
        {
            var results = new List<DriverResult>();
            if (selections.Count == 0)
                return results;

            progress("Confirmando a disponibilidade das atualizações selecionadas…");
            dynamic session = CreateSession();
            dynamic search = Search(session);
            bool partial = (int)search.ResultCode == SucceededWithErrors || ReadWarnings(search).Length > 0;
            dynamic updates = search.Updates;
            var available = new Dictionary<Tuple<Guid, int>, object>();
            for (int index = 0; index < (int)updates.Count; index++)
            {
                dynamic update = updates.Item(index);
                Guid id;
                int revision;
                if (TryReadIdentity(update, out id, out revision))
                    available[Tuple.Create(id, revision)] = (object)update;
                else
                    partial = true;
            }

            bool restartPending = false;
            foreach (var selection in selections)
            {
                var result = new DriverResult { Title = FirstText(selection.Title, "Atualização " + selection.Id) };
                results.Add(result);
                Guid id;
                object matchingUpdate;
                if (!Guid.TryParse(selection.Id, out id) ||
                    !available.TryGetValue(Tuple.Create(id, selection.Revision), out matchingUpdate))
                {
                    result.Detail = partial
                        ? "A consulta ficou incompleta e não confirmou esta atualização."
                        : "Esta revisão deixou de estar disponível. Faça uma nova busca.";
                    continue;
                }

                try
                {
                    dynamic update = matchingUpdate;
                    result.Title = FirstText((object)update.Title, result.Title);
                    if (restartPending)
                    {
                        result.RebootRequired = true;
                        result.Detail = "Reinicie o computador antes de instalar esta atualização.";
                        continue;
                    }
                    InstallOne(session, update, result, progress, acceptLicense);
                }
                catch (COMException error)
                {
                    result.Detail = DescribeError(error);
                }
                catch (UnauthorizedAccessException error)
                {
                    result.Detail = DescribeError(error);
                }
                catch (RuntimeBinderException error)
                {
                    result.Detail = DescribeError(error);
                }
                restartPending |= result.RebootRequired;
            }

            return results;
        }

        public static string DescribeError(Exception error)
        {
            if (error is UnauthorizedAccessException)
                return "O Windows negou acesso a esta operação.";
            if (error is RuntimeBinderException)
                return "O agente do Windows Update não disponibilizou uma operação necessária.";

            var comError = error as COMException;
            return comError == null ? error.Message : DescribeCode(comError.ErrorCode);
        }

        private static void InstallOne(dynamic session, dynamic update, DriverResult result,
            Action<string> progress, Func<string, string, bool> acceptLicense)
        {
            dynamic installer = session.CreateUpdateInstaller();
            if (!CheckInstaller(installer, result))
                return;

            if (!(bool)update.EulaAccepted)
            {
                var license = ReadText((object)update.EulaText);
                if (license.Length == 0)
                {
                    result.Detail = "O Windows Update não forneceu o texto da licença.";
                    return;
                }
                if (!acceptLicense(result.Title, license))
                {
                    result.Detail = "Termos de licença não aceitos.";
                    return;
                }
                update.AcceptEula();
            }

            dynamic collection = CreateComObject("Microsoft.Update.UpdateColl");
            collection.Add(update);
            if (!(bool)update.IsDownloaded)
            {
                progress("Baixando " + result.Title + "…");
                dynamic downloader = session.CreateUpdateDownloader();
                downloader.Updates = collection;
                dynamic download = downloader.Download();
                if (!(bool)update.IsDownloaded)
                {
                    dynamic item = download.GetUpdateResult(0);
                    result.Detail = ResultMessage("O download", (int)item.ResultCode,
                        FirstError((int)item.HResult, (int)download.HResult));
                    return;
                }
            }

            if (!CheckInstaller(installer, result))
                return;

            progress("Instalando " + result.Title + "…");
            installer.AllowSourcePrompts = false;
            installer.Updates = collection;
            dynamic installation = installer.Install();
            result.RebootRequired = (bool)installation.RebootRequired;
            dynamic itemResult = installation.GetUpdateResult(0);
            result.RebootRequired |= (bool)itemResult.RebootRequired;
            int itemCode = (int)itemResult.ResultCode;
            int installationCode = (int)installation.ResultCode;
            int error = FirstError((int)itemResult.HResult, (int)installation.HResult);
            result.Installed = itemCode == Succeeded && error >= 0;
            if (result.Installed)
            {
                result.Detail = result.RebootRequired ? "Instalado. Reinicie para concluir." : "Instalado.";
                return;
            }
            result.Detail = ResultMessage("A instalação", itemCode == Succeeded ? installationCode : itemCode, error);
        }

        private static bool CheckInstaller(dynamic installer, DriverResult result)
        {
            if ((bool)installer.RebootRequiredBeforeInstallation)
            {
                result.RebootRequired = true;
                result.Detail = "Reinicie o computador antes de instalar esta atualização.";
                return false;
            }
            if ((bool)installer.IsBusy)
            {
                result.Detail = "O Windows Update está instalando outra atualização. Tente novamente depois.";
                return false;
            }
            return true;
        }

        private static object CreateSession()
        {
            dynamic session = CreateComObject("Microsoft.Update.Session");
            session.ClientApplicationID = "Atualizador de drivers";
            return session;
        }

        private static object CreateComObject(string programId)
        {
            Type type = Type.GetTypeFromProgID(programId);
            if (type == null)
                throw new InvalidOperationException("O agente do Windows Update não está disponível.");

            return Activator.CreateInstance(type);
        }

        private static object Search(dynamic session)
        {
            dynamic searcher = session.CreateUpdateSearcher();
            searcher.Online = true;
            dynamic search = searcher.Search(Criteria);
            int code = (int)search.ResultCode;
            if (code != Succeeded && code != SucceededWithErrors)
            {
                string warnings = ReadWarnings(search);
                string message = code == Aborted ? "A consulta foi interrompida." : "O Windows Update não concluiu a consulta.";
                throw new InvalidOperationException(FirstText(warnings, message));
            }
            return search;
        }

        private static string ReadWarnings(dynamic search)
        {
            dynamic warnings = search.Warnings;
            var messages = new List<string>();
            for (int index = 0; index < (int)warnings.Count; index++)
            {
                dynamic warning = warnings.Item(index);
                string description = DescribeCode((int)warning.HResult);
                if (!messages.Contains(description))
                    messages.Add(description);
            }
            return string.Join(" ", messages);
        }

        private static bool TryReadIdentity(dynamic update, out Guid id, out int revision)
        {
            dynamic identity = update.Identity;
            revision = (int)identity.RevisionNumber;
            return Guid.TryParse((string)identity.UpdateID, out id) && revision > 0;
        }

        private static object ReadDriverProperty(Func<object> read)
        {
            try { return read(); }
            catch (RuntimeBinderException) { return null; }
            catch (COMException) { return null; }
        }

        private static string[] ReadOfferHardwareIds(dynamic update)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddHardwareIds(ids, ReadDriverProperty(() => update.DriverHardwareID));
            dynamic entries = ReadDriverProperty(() => update.WindowsDriverUpdateEntries);
            if (entries != null)
            {
                object count = ReadDriverProperty(() => entries.Count);
                int entryCount = count is int ? (int)count : 0;
                for (int index = 0; index < entryCount; index++)
                    AddHardwareIds(ids, ReadDriverProperty(() => entries.Item(index).DriverHardwareID));
            }
            return CopyHardwareIds(ids);
        }

        private static void AddHardwareIds(HashSet<string> target, object value)
        {
            var values = value as string[];
            if (values != null)
            {
                foreach (string id in values)
                    AddHardwareIds(target, id);
                return;
            }

            var identifier = value as string;
            if (!string.IsNullOrWhiteSpace(identifier))
                target.Add(identifier.Trim());
        }

        private static string[] CopyHardwareIds(HashSet<string> ids)
        {
            var values = new string[ids.Count];
            ids.CopyTo(values);
            return values;
        }

        private static DateTime? ParseDriverDate(string value)
        {
            DateTime date;
            // An INF identifies a calendar date. WMI's trailing offset can be unspecified.
            if (value.Length >= 8 && DateTime.TryParseExact(value.Substring(0, 8), "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return date;
            return null;
        }

        private static DateTime? ReadOfferDate(object value)
        {
            if (value is DateTime && ((DateTime)value).Year > 1900)
                return ((DateTime)value).Date;
            return null;
        }

        private static string ReadText(object value)
        {
            return (Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty).Trim();
        }

        private static string FirstText(params object[] values)
        {
            foreach (var value in values)
            {
                var text = ReadText(value);
                if (text.Length > 0)
                    return text;
            }
            return string.Empty;
        }

        private static int FirstError(int item, int operation)
        {
            return item < 0 ? item : operation;
        }

        private static string ResultMessage(string operation, int status, int error)
        {
            if (error < 0)
                return operation + " falhou. " + DescribeCode(error);
            if (status == SucceededWithErrors)
                return operation + " terminou com erros. Faça uma nova busca para verificar.";
            if (status == Aborted)
                return operation + " foi interrompido pelo Windows Update.";
            return operation + " não foi concluído pelo Windows Update.";
        }

        private static string DescribeCode(int error)
        {
            string detail;
            switch (unchecked((uint)error))
            {
                case 0x80070005: detail = "O Windows negou acesso a esta operação."; break;
                case 0x80240009:
                case 0x80240016: detail = "Outra operação do Windows Update está em andamento ou há uma reinicialização pendente."; break;
                case 0x80240017: detail = "A atualização deixou de se aplicar a este computador."; break;
                case 0x8024001F:
                case 0x80072EE2:
                case 0x80072EE7:
                case 0x80072EFD:
                case 0x8024402C: detail = "O Windows Update não conseguiu se conectar. Verifique a conexão e tente novamente."; break;
                case 0x80240025:
                case 0x8024002E:
                case 0x8024002F: detail = "O acesso ao Windows Update está desabilitado ou foi restringido por uma política do computador."; break;
                default: detail = "O Windows Update retornou um erro."; break;
            }
            return detail + " (0x" + error.ToString("X8", CultureInfo.InvariantCulture) + ")";
        }
    }
}
