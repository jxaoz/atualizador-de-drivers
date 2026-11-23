using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DriverUpdater
{
    internal static class DriverProperties
    {
        public static void Show(IWin32Window owner, InstalledDriver driver)
        {
            ShowDialog(owner, "Propriedades do driver", driver.Device,
                new KeyValuePair<string, string>[]
                {
                    Field("Fornecedor", driver.Manufacturer),
                    Field("Versão instalada", driver.Version),
                    Field("Data do driver", DisplayDate(driver.Date)),
                    Field("Categoria", DisplayCategory(driver.Class)),
                    Field("Arquivo INF", driver.InfName),
                    Field("Assinatura", DisplaySignature(driver)),
                    Field("ID da instância", driver.DeviceInstanceId)
                }, driver.HardwareIds, driver.DeviceInstanceId);
        }

        public static void Show(IWin32Window owner, DriverOffer offer, string associatedDevice)
        {
            ShowDialog(owner, "Propriedades da atualização", offer.Title,
                new KeyValuePair<string, string>[]
                {
                    Field("Dispositivo associado", associatedDevice),
                    Field("Fornecedor", offer.Manufacturer),
                    Field("Categoria", DisplayCategory(DriverCategoryIcons.ResolveOfferClass(offer.Class, offer.Title))),
                    Field("Data do driver", DisplayDate(offer.Date)),
                    Field("Tipo de oferta", offer.Optional ? "Opcional" : "Driver"),
                    Field("ID da atualização", offer.Id),
                    Field("Revisão da oferta", offer.Revision.ToString(CultureInfo.CurrentCulture))
                }, offer.HardwareIds, null);
        }

        private static void ShowDialog(IWin32Window owner, string title, string name,
            IList<KeyValuePair<string, string>> fields, string[] hardwareIds, string deviceInstanceId)
        {
            using (var dialog = new Form())
            {
                AppStyle.SetWindow(dialog, title, new Size(710, 565), new Size(560, 510));
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.ShowInTaskbar = false;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;

                var shell = AppStyle.MakeLayout(2);
                shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 103));
                shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                shell.Controls.Add(AppStyle.MakeHeader(title, name), 0, 0);

                var content = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    Margin = Padding.Empty,
                    Padding = new Padding(24, 16, 24, 17),
                    ColumnCount = 2,
                    RowCount = fields.Count + 3
                };
                content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 159));
                content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                for (int index = 0; index < fields.Count; index++)
                {
                    KeyValuePair<string, string> field = fields[index];
                    content.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
                    var label = AppStyle.MakeLabel(field.Key, AppStyle.Muted);
                    label.Margin = new Padding(0, 0, 8, 5);
                    content.Controls.Add(label, 0, index);
                    content.Controls.Add(MakeReadOnlyField(field.Key, field.Value), 1, index);
                }

                content.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
                content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                content.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
                string idLabelText = "IDs de hardware e compatibilidade";
                var idLabel = AppStyle.MakeLabel(idLabelText, AppStyle.Muted);
                content.Controls.Add(idLabel, 0, fields.Count);
                content.SetColumnSpan(idLabel, 2);
                var idText = MakeReadOnlyField(idLabelText, FormatIds(hardwareIds));
                idText.Multiline = true;
                idText.ScrollBars = ScrollBars.Vertical;
                idText.Margin = Padding.Empty;
                content.Controls.Add(idText, 0, fields.Count + 1);
                content.SetColumnSpan(idText, 2);

                var actions = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft,
                    WrapContents = false,
                    Margin = new Padding(0, 14, 0, 0)
                };
                var close = AppStyle.MakeButton("Fechar", false, 96);
                close.DialogResult = DialogResult.Cancel;
                actions.Controls.Add(close);
                if (CanOpenWindows(deviceInstanceId))
                {
                    var openWindows = AppStyle.MakeButton("Abrir no Windows", true, 162);
                    openWindows.Margin = new Padding(0, 0, 10, 0);
                    openWindows.Click += delegate { OpenInWindows(dialog, deviceInstanceId); };
                    actions.Controls.Add(openWindows);
                }
                content.Controls.Add(actions, 0, fields.Count + 2);
                content.SetColumnSpan(actions, 2);
                shell.Controls.Add(content, 0, 1);
                dialog.Controls.Add(shell);
                dialog.CancelButton = close;
                dialog.ActiveControl = close;
                dialog.ShowDialog(owner);
            }
        }

        private static TextBox MakeReadOnlyField(string label, string value)
        {
            return new TextBox
            {
                AccessibleName = label,
                Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
                ReadOnly = true,
                ForeColor = AppStyle.Text,
                BackColor = AppStyle.Surface,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 6)
            };
        }

        private static KeyValuePair<string, string> Field(string label, string value)
        {
            return new KeyValuePair<string, string>(label, value);
        }

        private static string DisplayDate(DateTime? date)
        {
            return date.HasValue ? date.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : null;
        }

        private static string DisplayCategory(string driverClass)
        {
            string label = DriverCategoryIcons.GetLabel(driverClass);
            return string.IsNullOrWhiteSpace(driverClass) ? label : label + " (" + driverClass + ")";
        }

        private static string DisplaySignature(InstalledDriver driver)
        {
            if (!driver.Signed.HasValue)
                return driver.Signer;
            if (!driver.Signed.Value)
                return "Não assinado";
            return string.IsNullOrWhiteSpace(driver.Signer) ? "Assinado" : driver.Signer;
        }

        private static string FormatIds(string[] hardwareIds)
        {
            return hardwareIds == null ? null : string.Join(Environment.NewLine, hardwareIds);
        }

        private static bool CanOpenWindows(string deviceInstanceId)
        {
            return !string.IsNullOrWhiteSpace(deviceInstanceId) &&
                deviceInstanceId.IndexOfAny(new[] { '"', '\0', '\r', '\n' }) < 0 &&
                !deviceInstanceId.EndsWith("\\", StringComparison.Ordinal);
        }

        private static void OpenInWindows(IWin32Window owner, string deviceInstanceId)
        {
            try
            {
                DevicePropertiesRunDll(owner.Handle, IntPtr.Zero, "/DeviceID \"" + deviceInstanceId + "\"", 1);
            }
            catch (DllNotFoundException)
            {
                ShowWindowsError(owner);
            }
            catch (EntryPointNotFoundException)
            {
                ShowWindowsError(owner);
            }
        }

        private static void ShowWindowsError(IWin32Window owner)
        {
            MessageBox.Show(owner, "O painel de propriedades do Windows está indisponível.",
                "Propriedades do dispositivo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // https://learn.microsoft.com/windows-hardware/drivers/install/invoking-a-device-properties-dialog-box-programmatically-in-an-install
        [DllImport("devmgr.dll", EntryPoint = "DeviceProperties_RunDLLW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern void DevicePropertiesRunDll(IntPtr owner, IntPtr applicationInstance,
            string arguments, int showCommand);
    }
}
