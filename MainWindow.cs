using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DriverUpdater
{
    internal sealed class MainWindow : Form
    {
        private readonly TabPage updatePage = new TabPage("Atualizações disponíveis");
        private readonly TabPage inventoryPage = new TabPage("Drivers instalados");
        private readonly Button scanButton = AppStyle.MakeButton("Buscar atualizações", true, 174);
        private readonly Button installButton = AppStyle.MakeButton("Instalar selecionados", true, 190);
        private readonly Button refreshButton = AppStyle.MakeButton("Atualizar lista", false, 124);
        private readonly Label updateStatus = AppStyle.MakeLabel("Busque atualizações para consultar os drivers oferecidos para este computador.", AppStyle.Muted);
        private readonly Label inventoryStatus = AppStyle.MakeLabel("Carregando drivers instalados…", AppStyle.Muted);
        private readonly Label selectionStatus = AppStyle.MakeLabel(string.Empty, AppStyle.Text);
        private readonly DataGridView updateGrid = AppStyle.MakeGrid("Atualizações disponíveis");
        private readonly DataGridView inventoryGrid = AppStyle.MakeGrid("Drivers instalados");
        private readonly TextBox filter = new TextBox { AccessibleName = "Filtrar drivers instalados", Dock = DockStyle.Fill, Margin = Padding.Empty };
        private readonly ProgressBar activity = new ProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 28, Visible = false, AccessibleName = "Operação em andamento" };
        private readonly ToolTip toolTip = new ToolTip();
        private List<InstalledDriver> inventory = new List<InstalledDriver>();
        private bool hasInventory;
        private bool readingInventory;
        private bool scanning;
        private bool installing;
        private bool refreshingOffers;

        public MainWindow()
        {
            AppStyle.SetWindow(this, AppStyle.Name, new Size(990, 650), new Size(735, 525));
            var shell = AppStyle.MakeLayout(3);
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 103));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            shell.Controls.Add(AppStyle.MakeHeader(AppStyle.Name, "Consulte o Windows Update e instale as atualizações de driver que você selecionar."), 0, 0);

            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Padding = new Point(14, 6),
                Margin = new Padding(24, 17, 24, 0),
                AccessibleName = "Drivers"
            };
            updatePage.BackColor = AppStyle.Surface;
            inventoryPage.BackColor = AppStyle.Surface;
            updatePage.Controls.Add(BuildUpdates());
            inventoryPage.Controls.Add(BuildInventory());
            tabs.TabPages.Add(updatePage);
            tabs.TabPages.Add(inventoryPage);
            shell.Controls.Add(tabs, 0, 1);
            shell.Controls.Add(BuildFooter(), 0, 2);
            Controls.Add(shell);

            scanButton.Click += async delegate { await ScanAsync(); };
            installButton.Click += async delegate { await InstallAsync(); };
            refreshButton.Click += async delegate { await RefreshInventoryAsync(); };
            filter.TextChanged += delegate { DisplayInventory(); };
            updateGrid.CellValueChanged += delegate { UpdateControls(); };
            updateGrid.CurrentCellDirtyStateChanged += delegate
            {
                if (updateGrid.IsCurrentCellDirty && updateGrid.CurrentCell is DataGridViewCheckBoxCell)
                    updateGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            Shown += async delegate { await RefreshInventoryAsync(); };
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (installing && args.CloseReason == CloseReason.UserClosing)
                {
                    args.Cancel = true;
                    SetUpdateStatus("Acompanhe a instalação na outra janela antes de fechar o aplicativo.");
                }
            };
            UpdateControls();
        }

        private Control BuildUpdates()
        {
            var panel = AppStyle.MakeLayout(4);
            panel.Padding = new Padding(14, 12, 14, 12);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
            panel.Controls.Add(scanButton, 0, 0);
            panel.Controls.Add(updateStatus, 0, 1);

            updateGrid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                HeaderText = "Selecionar",
                Width = 85,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Resizable = DataGridViewTriState.False,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            AppStyle.AddTextColumn(updateGrid, "Atualização", 54, 200);
            AppStyle.AddTextColumn(updateGrid, "Dispositivo associado", 33, 140);
            updateGrid.Columns[2].HeaderCell.ToolTipText = "Dispositivos com ID de hardware ou de compatibilidade correspondente.";
            AppStyle.AddTextColumn(updateGrid, "Tipo", 12, 76);
            AppStyle.AddTextColumn(updateGrid, "Data do driver", 13, 103);
            updateGrid.Columns[1].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            updateGrid.Columns[2].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            updateGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders;
            panel.Controls.Add(updateGrid, 0, 2);

            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 12, 0, 0) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            actions.Controls.Add(selectionStatus, 0, 0);
            actions.Controls.Add(installButton, 1, 0);
            panel.Controls.Add(actions, 0, 3);
            return panel;
        }

        private Control BuildInventory()
        {
            var panel = AppStyle.MakeLayout(3);
            panel.Padding = new Padding(14, 12, 14, 12);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 265));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
            var filterLabel = AppStyle.MakeLabel("&Filtrar", AppStyle.Text);
            filterLabel.TabIndex = 0;
            filter.TabIndex = 1;
            filter.Margin = new Padding(0, 7, 0, 5);
            toolbar.Controls.Add(filterLabel, 0, 0);
            toolbar.Controls.Add(filter, 1, 0);
            toolbar.Controls.Add(refreshButton, 3, 0);
            panel.Controls.Add(toolbar, 0, 0);
            panel.Controls.Add(inventoryStatus, 0, 1);

            inventoryGrid.ReadOnly = true;
            AppStyle.AddTextColumn(inventoryGrid, "Dispositivo", 49, 210);
            AppStyle.AddTextColumn(inventoryGrid, "Fornecedor", 23, 108);
            AppStyle.AddTextColumn(inventoryGrid, "Versão instalada", 15, 116);
            AppStyle.AddTextColumn(inventoryGrid, "Data do driver", 13, 107);
            panel.Controls.Add(inventoryGrid, 0, 2);
            return panel;
        }

        private Control BuildFooter()
        {
            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(26, 0, 26, 0)
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
            var settings = new LinkLabel
            {
                Text = "Abrir Windows Update",
                LinkBehavior = LinkBehavior.HoverUnderline,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty
            };
            settings.LinkClicked += delegate
            {
                try
                {
                    using (Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true })) { }
                }
                catch (Exception error)
                {
                    MessageBox.Show(this, DriverServices.DescribeError(error), AppStyle.Name,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            activity.Dock = DockStyle.Fill;
            activity.Margin = new Padding(0, 20, 0, 19);
            footer.Controls.Add(settings, 0, 0);
            footer.Controls.Add(activity, 1, 0);
            return footer;
        }

        private async Task RefreshInventoryAsync()
        {
            if (readingInventory)
                return;

            readingInventory = true;
            inventoryStatus.Text = "Carregando drivers instalados…";
            toolTip.SetToolTip(inventoryStatus, string.Empty);
            UpdateControls();
            try
            {
                inventory = await Task.Run(() => DriverServices.ReadInventory());
                hasInventory = true;
                RefreshDeviceMatches();
            }
            catch (Exception error)
            {
                string message = "Falha ao ler os drivers instalados. " + DriverServices.DescribeError(error);
                inventoryStatus.Text = message;
                toolTip.SetToolTip(inventoryStatus, message);
                if (inventory.Count > 0)
                    inventoryStatus.Text += " A lista anterior continua disponível.";
                return;
            }
            finally
            {
                readingInventory = false;
                UpdateControls();
            }
            DisplayInventory();
        }

        private void DisplayInventory()
        {
            if (readingInventory || !hasInventory)
                return;

            string query = filter.Text.Trim();
            CompareInfo compare = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;
            var matches = inventory.Where(driver => query.Length == 0 ||
                Contains(compare, driver.Device, query) || Contains(compare, driver.Manufacturer, query) ||
                Contains(compare, driver.Version, query)).ToList();

            inventoryGrid.Rows.Clear();
            foreach (InstalledDriver driver in matches)
                inventoryGrid.Rows.Add(Display(driver.Device), Display(driver.Manufacturer), Display(driver.Version), DisplayDate(driver.Date));
            inventoryGrid.ClearSelection();
            inventoryPage.Text = "Drivers instalados (" + inventory.Count + ")";
            inventoryStatus.Text = inventory.Count == 0 ? "Nenhum driver foi retornado pelo Windows." :
                matches.Count + " de " + inventory.Count + " drivers exibidos.";
            toolTip.SetToolTip(inventoryStatus, string.Empty);
        }

        private async Task ScanAsync()
        {
            if (scanning || installing)
                return;

            scanning = true;
            ClearOffers();
            SetUpdateStatus("Consultando o Windows Update. A busca pode levar alguns minutos…");
            UpdateControls();
            try
            {
                DriverScan result = await Task.Run(() => DriverServices.Scan());
                ShowOffers(result);
            }
            catch (Exception error)
            {
                SetUpdateStatus("Falha na consulta. " + DriverServices.DescribeError(error));
            }
            finally
            {
                scanning = false;
                UpdateControls();
            }
        }

        private void ShowOffers(DriverScan result)
        {
            refreshingOffers = true;
            try
            {
                foreach (DriverOffer offer in result.Offers)
                {
                    int index = updateGrid.Rows.Add(false, offer.Title, "—", OfferType(offer), DisplayDate(offer.Date));
                    DataGridViewRow row = updateGrid.Rows[index];
                    row.Tag = offer;
                    row.Cells[1].ToolTipText = string.IsNullOrWhiteSpace(offer.Manufacturer) ? offer.Title :
                        offer.Title + "\r\nFornecedor do pacote: " + offer.Manufacturer;
                }
                RefreshDeviceMatches();
                updateGrid.ClearSelection();
            }
            finally
            {
                refreshingOffers = false;
            }

            updatePage.Text = "Atualizações disponíveis (" + result.Offers.Count + ")";
            if (result.Partial)
                SetUpdateStatus("O Windows Update retornou um resultado parcial. Tente buscar novamente.", result.Warnings);
            else if (result.Offers.Count == 0)
                SetUpdateStatus("O Windows Update não encontrou atualizações de driver para este computador.");
            else
                SetUpdateStatus("Marque as atualizações que você deseja instalar.");
            UpdateControls();
        }

        private async Task InstallAsync()
        {
            if (installing || scanning)
                return;

            updateGrid.EndEdit();
            List<DriverOffer> selected = GetSelectedOffers();
            if (selected.Count == 0)
                return;

            string action = selected.Count == 1 ? "Instalar a atualização selecionada?" :
                "Instalar as " + selected.Count + " atualizações selecionadas?";
            string confirmation = action + "\r\n\r\nO Windows pode solicitar permissão de administrador e pedir que você reinicie o computador ao final.";
            if (selected.Any(IsFirmware))
                confirmation += "\r\n\r\nA seleção inclui firmware. Mantenha o computador ligado durante a atualização.";
            if (MessageBox.Show(this, confirmation, AppStyle.Name, MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            installing = true;
            SetUpdateStatus("Aguardando a janela de instalação do Windows Update…");
            UpdateControls();
            try
            {
                int result = await Task.Run(() =>
                {
                    using (Process process = Program.StartInstaller(selected))
                    {
                        if (process == null)
                            throw new InvalidOperationException("O Windows não abriu a janela de instalação.");
                        process.WaitForExit();
                        return process.ExitCode;
                    }
                });

                ClearOffers();
                SetUpdateStatus(InstallSummary(result));
                await RefreshInventoryAsync();
            }
            catch (Exception error)
            {
                SetUpdateStatus(Program.PermissionWasDeclined(error) ?
                    "Permissão de administrador cancelada. Nenhuma atualização foi instalada." :
                    "Falha ao abrir a instalação. " + DriverServices.DescribeError(error));
            }
            finally
            {
                installing = false;
                UpdateControls();
            }
        }

        private static string InstallSummary(int result)
        {
            switch (result)
            {
                case InstallWindow.Success:
                    return "Instalação concluída. Busque novamente para consultar outras atualizações.";
                case InstallWindow.SuccessWithRestart:
                    return "Instalação concluída. Reinicie o computador para finalizar as atualizações.";
                case InstallWindow.IncompleteWithRestart:
                    return "Algumas atualizações ficaram pendentes. Reinicie o computador e busque novamente.";
                default:
                    return "A instalação terminou com atualizações pendentes. Busque novamente para verificar.";
            }
        }

        private void ClearOffers()
        {
            refreshingOffers = true;
            updateGrid.Rows.Clear();
            updatePage.Text = "Atualizações disponíveis";
            refreshingOffers = false;
        }

        private void SetUpdateStatus(string message, string details = null)
        {
            updateStatus.Text = message;
            toolTip.SetToolTip(updateStatus, string.IsNullOrWhiteSpace(details) ? message : message + "\r\n" + details);
        }

        private void UpdateControls()
        {
            if (refreshingOffers)
                return;

            bool busy = scanning || installing;
            if (updateGrid.ReadOnly != busy)
                updateGrid.ReadOnly = busy;
            scanButton.Enabled = !busy;
            scanButton.Text = scanning ? "Buscando…" : "Buscar atualizações";
            refreshButton.Enabled = !readingInventory && !installing;
            activity.Visible = busy || readingInventory;
            int count = GetSelectedOffers().Count;
            installButton.Enabled = !busy && count > 0;
            selectionStatus.Text = updateGrid.Rows.Count == 0 ? string.Empty :
                count == 1 ? "1 atualização selecionada" : count + " atualizações selecionadas";
        }

        private List<DriverOffer> GetSelectedOffers()
        {
            var selected = new List<DriverOffer>();
            foreach (DataGridViewRow row in updateGrid.Rows)
            {
                var offer = row.Tag as DriverOffer;
                if (offer != null && Equals(row.Cells[0].Value, true))
                    selected.Add(offer);
            }
            return selected;
        }

        private static bool Contains(CompareInfo compare, string text, string query)
        {
            return text != null && compare.IndexOf(text, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
        }

        private void RefreshDeviceMatches()
        {
            foreach (DataGridViewRow row in updateGrid.Rows)
            {
                var offer = row.Tag as DriverOffer;
                if (offer == null || offer.HardwareIds == null)
                    continue;

                var ids = new HashSet<string>(offer.HardwareIds, StringComparer.OrdinalIgnoreCase);
                string names = string.Join(", ", inventory.Where(driver =>
                        driver.HardwareIds != null && driver.HardwareIds.Any(ids.Contains))
                    .Select(driver => driver.Device).Distinct(StringComparer.CurrentCultureIgnoreCase));
                row.Cells[2].Value = Display(names);
                row.Cells[2].ToolTipText = names.Length > 0 ? names :
                    "O Windows Update ofereceu este pacote para o computador. O aplicativo não encontrou um ID correspondente no inventário.";
            }
        }

        private static string OfferType(DriverOffer offer)
        {
            return IsFirmware(offer) ? "Firmware" : (offer.Optional ? "Opcional" : "Driver");
        }

        private static bool IsFirmware(DriverOffer offer)
        {
            return string.Equals(offer.Class, "Firmware", StringComparison.OrdinalIgnoreCase);
        }

        private static string Display(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? "—" : text;
        }

        private static string DisplayDate(DateTime? date)
        {
            return date.HasValue ? date.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "—";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                toolTip.Dispose();
            base.Dispose(disposing);
        }
    }
}
