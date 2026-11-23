using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DriverUpdater
{
    internal sealed class VideoDriversWindow : Form
    {
        private static readonly Regex PciVendorPattern = new Regex(@"^PCI\\VEN_([0-9A-F]{4})(?:&|\\|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly VendorPage Nvidia = new VendorPage("NVIDIA", "https://www.nvidia.com/pt-br/drivers/");
        private static readonly VendorPage Amd = new VendorPage("AMD", "https://www.amd.com/pt/support/download/drivers.html");
        private static readonly VendorPage Intel = new VendorPage("Intel", "https://www.intel.com.br/content/www/br/pt/support/articles/000090440/graphics.html");

        private readonly DataGridView adapters = AppStyle.MakeGrid("Adaptadores de vídeo detectados");
        private readonly Label selectionInfo = AppStyle.MakeLabel(string.Empty, AppStyle.Muted);
        private readonly Button downloads = AppStyle.MakeButton("&Downloads oficiais", false, 158);
        private VendorPage selectedPage;

        public VideoDriversWindow(IList<InstalledDriver> drivers)
        {
            AppStyle.SetWindow(this, "Drivers de vídeo", new Size(750, 408), new Size(610, 350));
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            var shell = AppStyle.MakeLayout(3);
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 106));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            shell.Controls.Add(AppStyle.MakeHeader("Drivers de vídeo", "Consulte os adaptadores instalados e escolha onde buscar atualizações."), 0, 0);
            shell.Controls.Add(BuildAdapters(drivers), 0, 1);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(14, 12, 14, 0)
            };
            Button close = AppStyle.MakeButton("Fechar", false, 89);
            close.DialogResult = DialogResult.Cancel;
            Button search = AppStyle.MakeButton("&Buscar atualizações de vídeo", true, 215);
            search.DialogResult = DialogResult.Yes;
            search.Margin = new Padding(0, 0, 8, 0);
            downloads.Margin = new Padding(0, 0, 8, 0);
            downloads.Click += delegate { OpenVendorPage(); };
            actions.Controls.Add(close);
            actions.Controls.Add(search);
            actions.Controls.Add(downloads);
            shell.Controls.Add(actions, 0, 2);
            Controls.Add(shell);
            AcceptButton = search;
            CancelButton = close;

            adapters.CurrentCellChanged += delegate { ShowSelectedAdapter(); };
            Shown += delegate
            {
                if (adapters.Rows.Count > 0)
                {
                    adapters.CurrentCell = adapters.Rows[0].Cells[0];
                    adapters.Focus();
                }
                else
                    search.Focus();

                ShowSelectedAdapter();
            };
            ShowSelectedAdapter();
        }

        public static bool IsGraphicsClass(string driverClass)
        {
            return string.Equals((driverClass ?? string.Empty).Trim(), "Display", StringComparison.OrdinalIgnoreCase);
        }

        private Control BuildAdapters(IList<InstalledDriver> drivers)
        {
            var card = new DashboardPanel(DashboardTone.Card)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(14, 9, 14, 0),
                Padding = new Padding(13, 10, 13, 7)
            };
            var content = AppStyle.MakeLayout(2);
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            adapters.ReadOnly = true;
            AppStyle.AddTextColumn(adapters, "Adaptador", 55, 200);
            AppStyle.AddTextColumn(adapters, "Fornecedor do driver", 26, 128);
            AppStyle.AddTextColumn(adapters, "Versão instalada", 19, 120);
            foreach (InstalledDriver driver in drivers)
            {
                if (!IsGraphicsClass(driver.Class))
                    continue;
                int index = adapters.Rows.Add(Display(driver.Device), Display(driver.Manufacturer), Display(driver.Version));
                adapters.Rows[index].Tag = driver;
            }

            if (adapters.Rows.Count == 0)
            {
                Label empty = AppStyle.MakeLabel("Nenhum adaptador de vídeo identificado no inventário.", AppStyle.Text);
                empty.TextAlign = ContentAlignment.MiddleCenter;
                content.Controls.Add(empty, 0, 0);
            }
            else
                content.Controls.Add(adapters, 0, 0);

            selectionInfo.Margin = new Padding(5, 0, 5, 0);
            content.Controls.Add(selectionInfo, 0, 1);
            card.Controls.Add(content);
            return card;
        }

        private void ShowSelectedAdapter()
        {
            InstalledDriver driver = adapters.CurrentRow == null ? null : adapters.CurrentRow.Tag as InstalledDriver;
            selectedPage = driver == null ? null : FindVendorPage(driver);
            downloads.Enabled = selectedPage != null;
            if (selectedPage != null)
                selectionInfo.Text = "O atalho abre a página oficial de drivers da " + selectedPage.Name + " no navegador.";
            else if (driver != null)
                selectionInfo.Text = "Para este adaptador, consulte as atualizações ou fale com o fabricante do computador.";
            else
                selectionInfo.Text = "Você ainda pode buscar atualizações pelas fontes disponíveis.";
        }

        private void OpenVendorPage()
        {
            if (selectedPage == null)
                return;

            try
            {
                using (Process.Start(new ProcessStartInfo(selectedPage.Url) { UseShellExecute = true })) { }
            }
            catch (Win32Exception)
            {
                ShowBrowserError();
            }
            catch (InvalidOperationException)
            {
                ShowBrowserError();
            }
        }

        private void ShowBrowserError()
        {
            MessageBox.Show(this, "O Windows não conseguiu abrir a página de downloads no navegador.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static VendorPage FindVendorPage(InstalledDriver driver)
        {
            string pciVendor = ReadPciVendor(driver.DeviceInstanceId);
            if (pciVendor == null && driver.HardwareIds != null)
            {
                foreach (string hardwareId in driver.HardwareIds)
                {
                    pciVendor = ReadPciVendor(hardwareId);
                    if (pciVendor != null)
                        break;
                }
            }

            if (pciVendor != null)
            {
                switch (pciVendor)
                {
                    case "10DE": return Nvidia;
                    case "1002": return Amd;
                    case "8086": return Intel;
                    default: return null;
                }
            }

            string provider = (driver.Manufacturer ?? string.Empty).Trim();
            if (HasProviderPrefix(provider, "NVIDIA"))
                return Nvidia;
            if (HasProviderPrefix(provider, "AMD") || HasProviderPrefix(provider, "Advanced Micro Devices") ||
                HasProviderPrefix(provider, "ATI"))
                return Amd;
            return HasProviderPrefix(provider, "Intel") ? Intel : null;
        }

        private static string ReadPciVendor(string hardwareId)
        {
            Match match = PciVendorPattern.Match(hardwareId ?? string.Empty);
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
        }

        private static bool HasProviderPrefix(string provider, string name)
        {
            return string.Equals(provider, name, StringComparison.OrdinalIgnoreCase) ||
                provider.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase) ||
                provider.StartsWith(name + ",", StringComparison.OrdinalIgnoreCase);
        }

        private static string Display(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "—" : value;
        }

        private sealed class VendorPage
        {
            public readonly string Name;
            public readonly string Url;

            public VendorPage(string name, string url)
            {
                Name = name;
                Url = url;
            }
        }
    }
}
