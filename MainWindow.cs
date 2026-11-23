using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DriverUpdater
{
    internal sealed class MainWindow : Form
    {
        private const int OfferCheckColumn = 0;
        private const int OfferIconColumn = 1;
        private const int OfferNameColumn = 2;
        private const int OfferDeviceColumn = 3;
        private const int WindowSizeMessage = 0x0005;
        private const int SizeMinimized = 1;
        private const uint InvalidateWindow = 0x0001;
        private const uint RepaintChildWindows = 0x0080;
        private const uint RepaintImmediately = 0x0100;

        private readonly Panel pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = Padding.Empty };
        private readonly Button updateTab = AppStyle.MakeButton("&Atualizações", true, 144);
        private readonly Button inventoryTab = AppStyle.MakeButton("&Drivers instalados", false, 168);
        private readonly ThemeToggle themeToggle = new ThemeToggle();
        private readonly SpeedometerButton scanButton = new SpeedometerButton { Dock = DockStyle.Fill, Margin = Padding.Empty };
        private readonly Button installButton = AppStyle.MakeButton("Instalar selecionados", true, 190);
        private readonly Button videoButton = AppStyle.MakeButton("Drivers de vídeo", false, 144);
        private readonly Button refreshButton = AppStyle.MakeButton("Atualizar lista", false, 124);
        private readonly Button inventoryProperties = AppStyle.MakeButton("Propriedades", false, 123);
        private readonly Label scanTitle = HeroTitle("Pronto para buscar", 29);
        private readonly Label updateStatus = AppStyle.MakeLabel("Inicie a busca para ver quais drivers têm atualizações disponíveis para este computador.", AppStyle.HeaderText);
        private readonly Label resultsTitle = SectionTitle("Resultados da busca");
        private readonly Label inventoryTitle = HeroTitle("Drivers instalados", 27);
        private readonly Label inventoryStatus = AppStyle.MakeLabel("Carregando drivers instalados…", AppStyle.Muted);
        private readonly Label selectionStatus = AppStyle.MakeLabel(string.Empty, AppStyle.Text);
        private readonly DataGridView updateGrid = AppStyle.MakeGrid("Atualizações disponíveis");
        private readonly AllDriversCurrent allDriversCurrent = new AllDriversCurrent { Visible = false };
        private readonly DataGridView inventoryGrid = AppStyle.MakeGrid("Drivers instalados");
        private readonly TextBox filter = new TextBox { AccessibleName = "Filtrar drivers instalados", Dock = DockStyle.Fill, Margin = Padding.Empty };
        private readonly ToolTip toolTip = new ToolTip();
        private readonly ContextMenuStrip updateMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip inventoryMenu = new ContextMenuStrip();
        private readonly DriverCategoryIcons categoryIcons = new DriverCategoryIcons();
        private readonly TableLayoutPanel updatePage;
        private readonly Control inventoryPage;
        private Bitmap brandImage;
        private List<InstalledDriver> inventory = new List<InstalledDriver>();
        private bool hasInventory;
        private bool readingInventory;
        private bool scanning;
        private bool installing;
        private bool refreshingOffers;
        private bool wasMinimized;

        public MainWindow()
        {
            AppStyle.SetWindow(this, AppStyle.Name, new Size(1120, 765), new Size(785, 625));
            var canvas = new DashboardPanel(DashboardTone.Canvas) { Dock = DockStyle.Fill };
            var shell = AppStyle.MakeLayout(3);
            shell.BackColor = Color.Transparent;
            shell.Padding = new Padding(26, 13, 26, 22);
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.Controls.Add(BuildMasthead(), 0, 0);
            shell.Controls.Add(BuildNavigation(), 0, 1);
            updatePage = BuildUpdates();
            inventoryPage = BuildInventory();
            pageHost.Controls.Add(inventoryPage);
            pageHost.Controls.Add(updatePage);
            shell.Controls.Add(pageHost, 0, 2);
            canvas.Controls.Add(shell);
            Controls.Add(canvas);

            themeToggle.Checked = AppStyle.IsDark;
            themeToggle.CheckedChanged += delegate
            {
                AppStyle.IsDark = themeToggle.Checked;
                ApplyCurrentTheme();
                Program.SaveNightMode(themeToggle.Checked);
            };

            scanButton.Click += async delegate { await ScanAsync(false); };
            videoButton.Click += async delegate { await ShowVideoDriversAsync(); };
            installButton.Click += async delegate { await InstallAsync(); };
            refreshButton.Click += async delegate { await RefreshInventoryAsync(); };
            inventoryProperties.Click += delegate { OpenProperties(inventoryGrid.CurrentRow); };
            filter.TextChanged += delegate { DisplayInventory(); };
            inventoryGrid.SelectionChanged += delegate { UpdateInventoryProperties(); };
            AttachDriverActions(inventoryGrid, inventoryMenu);
            AttachDriverActions(updateGrid, updateMenu);
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
                    SetUpdateStatus("Instalando drivers…", "Acompanhe a instalação na outra janela antes de fechar o aplicativo.");
                }
            };
            SwitchPage(false);
            UpdateControls();
            ApplyCurrentTheme();
        }

        private TableLayoutPanel BuildUpdates()
        {
            var page = AppStyle.MakeLayout(2);
            page.BackColor = Color.Transparent;
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 234));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(BuildScanHero(), 0, 0);
            page.Resize += delegate { ResizeScanHero(page); };

            var card = new DashboardPanel(DashboardTone.Card) { Dock = DockStyle.Fill, Margin = new Padding(0, 16, 0, 0) };
            var panel = AppStyle.MakeLayout(3);
            panel.BackColor = Color.Transparent;
            panel.Padding = new Padding(19, 8, 19, 14);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
            var resultsHeading = new TableLayoutPanel
            {
                ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty,
                BackColor = Color.Transparent
            };
            resultsHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            resultsHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
            videoButton.Anchor = AnchorStyles.Right;
            resultsHeading.Controls.Add(resultsTitle, 0, 0);
            resultsHeading.Controls.Add(videoButton, 1, 0);
            panel.Controls.Add(resultsHeading, 0, 0);

            updateGrid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                HeaderText = "Marcar",
                Width = 78,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Resizable = DataGridViewTriState.False,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            AddCategoryColumn(updateGrid);
            AppStyle.AddTextColumn(updateGrid, "Atualização", 54, 175);
            AppStyle.AddTextColumn(updateGrid, "Dispositivo associado", 33, 132);
            updateGrid.Columns[OfferDeviceColumn].HeaderCell.ToolTipText = "Dispositivos com ID de hardware ou de compatibilidade correspondente.";
            AppStyle.AddTextColumn(updateGrid, "Tipo", 12, 83);
            AppStyle.AddTextColumn(updateGrid, "Data do driver", 13, 105);
            updateGrid.Columns[OfferNameColumn].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            updateGrid.Columns[OfferDeviceColumn].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            updateGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders;
            var results = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = Padding.Empty };
            results.Controls.Add(updateGrid);
            results.Controls.Add(allDriversCurrent);
            allDriversCurrent.BringToFront();
            updateGrid.Resize += delegate { LayoutEmptyResult(); };
            updateGrid.ColumnHeadersHeightChanged += delegate { LayoutEmptyResult(); };
            panel.Controls.Add(results, 0, 1);

            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Margin = new Padding(0, 9, 0, 0), BackColor = Color.Transparent
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            actions.Controls.Add(selectionStatus, 0, 0);
            actions.Controls.Add(installButton, 1, 0);
            panel.Controls.Add(actions, 0, 2);
            card.Controls.Add(panel);
            page.Controls.Add(card, 0, 1);
            return page;
        }

        private void ResizeScanHero(TableLayoutPanel page)
        {
            if (wasMinimized || WindowState == FormWindowState.Minimized || ClientSize.Width == 0 || ClientSize.Height == 0)
                return;

            float scale = DeviceDpi / 96F;
            float heroHeight = (page.ClientSize.Height < 540 * scale ? 181 : 234) * scale;
            if (page.RowStyles[0].Height != heroHeight)
                page.RowStyles[0].Height = heroHeight;
        }

        private void LayoutEmptyResult()
        {
            int headerHeight = updateGrid.ColumnHeadersHeight;
            allDriversCurrent.SetBounds(updateGrid.Left, updateGrid.Top + headerHeight,
                updateGrid.ClientSize.Width, Math.Max(0, updateGrid.ClientSize.Height - headerHeight));
        }

        private Control BuildScanHero()
        {
            var hero = new DashboardPanel(DashboardTone.Scan) { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(28, 10, 11, 10),
                BackColor = Color.Transparent, Margin = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 346));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var copy = AppStyle.MakeLayout(5);
            copy.BackColor = Color.Transparent;
            copy.Padding = new Padding(0, 0, 15, 0);
            copy.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
            copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            copy.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            var source = AppStyle.MakeLabel("Windows Update", AppStyle.HeaderText);
            source.BackColor = Color.Transparent;
            source.Font = new Font("Segoe UI Semibold", 10F);
            source.TextAlign = ContentAlignment.BottomLeft;
            copy.Controls.Add(source, 0, 1);
            copy.Controls.Add(scanTitle, 0, 2);
            updateStatus.BackColor = Color.Transparent;
            updateStatus.TextAlign = ContentAlignment.TopLeft;
            updateStatus.AutoEllipsis = false;
            updateStatus.MaximumSize = new Size(490, 0);
            copy.Controls.Add(updateStatus, 0, 3);

            layout.Controls.Add(copy, 0, 0);
            layout.Controls.Add(scanButton, 1, 0);
            hero.Controls.Add(layout);
            return hero;
        }

        private Control BuildInventory()
        {
            var page = AppStyle.MakeLayout(2);
            page.BackColor = Color.Transparent;
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var hero = new DashboardPanel(DashboardTone.Inventory) { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var heroCopy = AppStyle.MakeLayout(2);
            heroCopy.BackColor = Color.Transparent;
            heroCopy.Padding = new Padding(28, 15, 24, 13);
            heroCopy.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
            heroCopy.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            heroCopy.Controls.Add(inventoryTitle, 0, 0);
            var tip = AppStyle.MakeLabel("Clique com o botão direito em um driver para abrir suas propriedades.", AppStyle.HeaderText);
            tip.BackColor = Color.Transparent;
            tip.TextAlign = ContentAlignment.TopLeft;
            heroCopy.Controls.Add(tip, 0, 1);
            hero.Controls.Add(heroCopy);
            page.Controls.Add(hero, 0, 0);

            var card = new DashboardPanel(DashboardTone.Card) { Dock = DockStyle.Fill, Margin = new Padding(0, 16, 0, 0) };
            var panel = AppStyle.MakeLayout(3);
            panel.BackColor = Color.Transparent;
            panel.Padding = new Padding(19, 13, 19, 14);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var toolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Margin = Padding.Empty,
                BackColor = Color.Transparent
            };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 123));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
            var filterLabel = AppStyle.MakeLabel("&Filtrar", AppStyle.Text);
            filterLabel.TabIndex = 0;
            filter.TabIndex = 1;
            filter.Margin = new Padding(0, 8, 0, 5);
            toolbar.Controls.Add(filterLabel, 0, 0);
            toolbar.Controls.Add(filter, 1, 0);
            toolbar.Controls.Add(inventoryProperties, 3, 0);
            toolbar.Controls.Add(refreshButton, 5, 0);
            panel.Controls.Add(toolbar, 0, 0);
            panel.Controls.Add(inventoryStatus, 0, 1);

            inventoryGrid.ReadOnly = true;
            AddCategoryColumn(inventoryGrid);
            AppStyle.AddTextColumn(inventoryGrid, "Dispositivo", 49, 176);
            AppStyle.AddTextColumn(inventoryGrid, "Fornecedor", 23, 100);
            AppStyle.AddTextColumn(inventoryGrid, "Versão instalada", 15, 105);
            AppStyle.AddTextColumn(inventoryGrid, "Data do driver", 13, 95);
            panel.Controls.Add(inventoryGrid, 0, 2);
            card.Controls.Add(panel);
            page.Controls.Add(card, 0, 1);
            return page;
        }

        private Control BuildMasthead()
        {
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
                BackColor = Color.Transparent, Margin = Padding.Empty
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 61));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 157));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            brandImage = Icon.ToBitmap();
            var brand = new PictureBox
            {
                Image = brandImage, Size = new Size(45, 45), SizeMode = PictureBoxSizeMode.Zoom,
                Anchor = AnchorStyles.Left, BackColor = Color.Transparent, Margin = Padding.Empty
            };
            header.Controls.Add(brand, 0, 0);
            var title = AppStyle.MakeLabel(AppStyle.Name, AppStyle.Text);
            title.BackColor = Color.Transparent;
            title.Font = new Font("Segoe UI Semilight", 27F);
            header.Controls.Add(title, 1, 0);
            themeToggle.Anchor = AnchorStyles.Right;
            themeToggle.Margin = Padding.Empty;
            header.Controls.Add(themeToggle, 2, 0);
            var settings = new LinkLabel
            {
                Text = "Windows Update ↗",
                AccessibleName = "Abrir Windows Update nas configurações do Windows",
                LinkColor = AppStyle.Accent,
                ActiveLinkColor = AppStyle.Text,
                BackColor = Color.Transparent,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
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
            header.Controls.Add(settings, 3, 0);
            return header;
        }

        private void ApplyCurrentTheme()
        {
            AppStyle.ApplyTheme(this);
            AppStyle.ApplyTheme(updateMenu);
            AppStyle.ApplyTheme(inventoryMenu);
        }

        private Control BuildNavigation()
        {
            var navigation = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = false, WrapContents = false,
                BackColor = Color.Transparent, Margin = Padding.Empty, Padding = new Padding(0, 3, 0, 0)
            };
            updateTab.Margin = new Padding(0, 0, 8, 0);
            updateTab.Click += delegate { SwitchPage(false); };
            inventoryTab.Click += delegate { SwitchPage(true); };
            navigation.Controls.Add(updateTab);
            navigation.Controls.Add(inventoryTab);
            return navigation;
        }

        private void SwitchPage(bool showInventory)
        {
            updatePage.Visible = !showInventory;
            inventoryPage.Visible = showInventory;
            AppStyle.SetPrimary(updateTab, !showInventory);
            AppStyle.SetPrimary(inventoryTab, showInventory);
            updateTab.AccessibleDescription = showInventory ? null : "Seção atual";
            inventoryTab.AccessibleDescription = showInventory ? "Seção atual" : null;
        }

        private void AttachDriverActions(DataGridView grid, ContextMenuStrip menu)
        {
            menu.Font = Font;
            var properties = new ToolStripMenuItem("&Propriedades…") { ShortcutKeyDisplayString = "Alt+Enter" };
            properties.Click += delegate
            {
                var open = menu.Tag as Action;
                if (open != null)
                    open();
            };
            menu.Items.Add(properties);

            grid.MouseUp += delegate(object sender, MouseEventArgs args)
            {
                if (args.Button != MouseButtons.Right)
                    return;
                DataGridView.HitTestInfo hit = grid.HitTest(args.X, args.Y);
                if (hit.RowIndex < 0 || hit.ColumnIndex < 0)
                    return;

                DataGridViewRow row = grid.Rows[hit.RowIndex];
                int column = grid == updateGrid ? Math.Max(OfferNameColumn, hit.ColumnIndex) : Math.Max(1, hit.ColumnIndex);
                grid.CurrentCell = row.Cells[column];
                row.Selected = true;
                grid.Focus();
                ShowPropertiesMenu(grid, menu, row, args.Location);
            };

            grid.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (grid.CurrentRow == null)
                    return;
                if (args.KeyCode == Keys.Apps || (args.Shift && args.KeyCode == Keys.F10))
                {
                    Rectangle cell = grid.GetCellDisplayRectangle(grid.CurrentCell.ColumnIndex, grid.CurrentRow.Index, true);
                    ShowPropertiesMenu(grid, menu, grid.CurrentRow, new Point(cell.Left, cell.Bottom));
                    args.SuppressKeyPress = true;
                }
                else if (args.Alt && args.KeyCode == Keys.Enter)
                {
                    OpenProperties(grid.CurrentRow);
                    args.SuppressKeyPress = true;
                }
            };

            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs args)
            {
                if (args.RowIndex >= 0 && args.ColumnIndex >= 0 && (grid != updateGrid || args.ColumnIndex != OfferCheckColumn))
                    OpenProperties(grid.Rows[args.RowIndex]);
            };
        }

        private void ShowPropertiesMenu(DataGridView grid, ContextMenuStrip menu, DataGridViewRow row, Point location)
        {
            Action open = GetPropertiesAction(row);
            if (open == null)
                return;
            menu.Tag = open;
            menu.Show(grid, location);
        }

        private void OpenProperties(DataGridViewRow row)
        {
            Action open = GetPropertiesAction(row);
            if (open != null)
                open();
        }

        private Action GetPropertiesAction(DataGridViewRow row)
        {
            if (row == null)
                return null;
            var driver = row.Tag as InstalledDriver;
            if (driver != null)
                return () => DriverProperties.Show(this, driver);
            var offer = row.Tag as DriverOffer;
            if (offer == null)
                return null;
            string device = row.Cells[OfferDeviceColumn].Value as string;
            return () => DriverProperties.Show(this, offer, device);
        }

        private void UpdateInventoryProperties()
        {
            inventoryProperties.Enabled = inventoryGrid.SelectedRows.Count == 1 &&
                inventoryGrid.SelectedRows[0].Tag is InstalledDriver;
        }

        private static Label HeroTitle(string title, float size)
        {
            Label label = AppStyle.MakeLabel(title, AppStyle.HeaderText);
            label.BackColor = Color.Transparent;
            label.Font = new Font("Segoe UI Semilight", size);
            return label;
        }

        private static Label SectionTitle(string title)
        {
            Label label = AppStyle.MakeLabel(title, AppStyle.Text);
            label.Font = new Font("Segoe UI Semibold", 13F);
            return label;
        }

        private static void AddCategoryColumn(DataGridView grid)
        {
            var column = new DataGridViewImageColumn
            {
                HeaderText = string.Empty,
                Width = 42,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                ImageLayout = DataGridViewImageCellLayout.Zoom,
                ReadOnly = true,
                Resizable = DataGridViewTriState.False,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            column.HeaderCell.ToolTipText = "Categoria do driver";
            column.DefaultCellStyle.Padding = new Padding(7, 5, 7, 5);
            grid.Columns.Add(column);
        }

        private void SetCategoryDescription(DataGridViewCell cell, string driverClass)
        {
            string category = DriverCategoryIcons.GetLabel(driverClass);
            ((DataGridViewImageCell)cell).Description = category;
            cell.ToolTipText = category;
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
            {
                int index = inventoryGrid.Rows.Add(categoryIcons.GetIcon(driver.Class, DeviceDpi), Display(driver.Device),
                    Display(driver.Manufacturer), Display(driver.Version), DisplayDate(driver.Date));
                DataGridViewRow row = inventoryGrid.Rows[index];
                row.Tag = driver;
                SetCategoryDescription(row.Cells[0], driver.Class);
            }
            inventoryGrid.ClearSelection();
            UpdateInventoryProperties();
            inventoryTitle.Text = inventory.Count == 1 ? "1 driver instalado" : inventory.Count + " drivers instalados";
            inventoryStatus.Text = inventory.Count == 0 ? "Nenhum driver foi retornado pelo Windows." :
                query.Length == 0 ? string.Empty :
                matches.Count + " de " + inventory.Count + " drivers exibidos.";
            toolTip.SetToolTip(inventoryStatus, string.Empty);
        }

        private async Task ShowVideoDriversAsync()
        {
            var graphicsDrivers = inventory.Where(driver => VideoDriversWindow.IsGraphicsClass(driver.Class)).ToList();
            using (var window = new VideoDriversWindow(graphicsDrivers))
            {
                if (window.ShowDialog(this) != DialogResult.Yes)
                    return;
            }
            await ScanAsync(true);
        }

        private async Task ScanAsync(bool videoOnly)
        {
            if (scanning || installing)
                return;

            scanning = true;
            ClearOffers();
            SetUpdateStatus(videoOnly ? "Buscando vídeo…" : "Buscando drivers…", "A consulta pode levar alguns minutos.");
            UpdateControls();
            try
            {
                DriverScan result = await Task.Run(() => DriverServices.Scan());
                if (videoOnly)
                    result.Offers.RemoveAll(offer => !VideoDriversWindow.IsGraphicsClass(
                        DriverCategoryIcons.ResolveOfferClass(offer.Class, offer.Title)));
                ShowOffers(result, videoOnly);
            }
            catch (Exception error)
            {
                SetUpdateStatus("Falha na busca", DriverServices.DescribeError(error));
            }
            finally
            {
                scanning = false;
                UpdateControls();
            }
        }

        private void ShowOffers(DriverScan result, bool videoOnly)
        {
            refreshingOffers = true;
            try
            {
                foreach (DriverOffer offer in result.Offers)
                {
                    string driverClass = DriverCategoryIcons.ResolveOfferClass(offer.Class, offer.Title);
                    int index = updateGrid.Rows.Add(false, categoryIcons.GetIcon(driverClass, DeviceDpi), offer.Title,
                        "—", OfferType(offer), DisplayDate(offer.Date));
                    DataGridViewRow row = updateGrid.Rows[index];
                    row.Tag = offer;
                    SetCategoryDescription(row.Cells[OfferIconColumn], driverClass);
                    row.Cells[OfferNameColumn].ToolTipText = string.IsNullOrWhiteSpace(offer.Manufacturer) ? offer.Title :
                        offer.Title + "\r\nFornecedor do pacote: " + offer.Manufacturer;
                }
                RefreshDeviceMatches();
                updateGrid.ClearSelection();
            }
            finally
            {
                refreshingOffers = false;
            }

            resultsTitle.Text = videoOnly
                ? (result.Offers.Count == 1 ? "1 atualização de vídeo encontrada" : result.Offers.Count + " atualizações de vídeo encontradas")
                : (result.Offers.Count == 1 ? "1 atualização encontrada" : result.Offers.Count + " atualizações encontradas");
            LayoutEmptyResult();
            allDriversCurrent.Visible = !videoOnly && !result.Partial && result.Offers.Count == 0;
            if (result.Partial)
                SetUpdateStatus("Busca parcial", "O Windows Update retornou um resultado parcial. Tente buscar novamente.", result.Warnings);
            else if (result.Offers.Count == 0)
                SetUpdateStatus("Busca concluída", videoOnly
                    ? "Nenhum driver de vídeo disponível no Windows Update. Veja os downloads oficiais em Drivers de vídeo."
                    : "O Windows Update não encontrou atualizações de driver para este computador.");
            else
                SetUpdateStatus("Busca concluída", "Marque as atualizações que você deseja instalar.");
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
            bool useDarkTheme = AppStyle.IsDark;
            SetUpdateStatus("Instalando drivers…", "Acompanhe o resultado na janela de instalação.");
            UpdateControls();
            try
            {
                int result = await Task.Run(() =>
                {
                    using (Process process = Program.StartInstaller(selected, useDarkTheme))
                    {
                        if (process == null)
                            throw new InvalidOperationException("O Windows não abriu a janela de instalação.");
                        process.WaitForExit();
                        return process.ExitCode;
                    }
                });

                ClearOffers();
                SetUpdateStatus("Resultado da instalação", InstallSummary(result));
                await RefreshInventoryAsync();
            }
            catch (Exception error)
            {
                if (Program.PermissionWasDeclined(error))
                    SetUpdateStatus("Instalação cancelada", "A permissão de administrador foi cancelada.");
                else
                    SetUpdateStatus("Falha na instalação", DriverServices.DescribeError(error));
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
            allDriversCurrent.Hide();
            updateGrid.Rows.Clear();
            resultsTitle.Text = "Resultados da busca";
            refreshingOffers = false;
        }

        private void SetUpdateStatus(string title, string message, string details = null)
        {
            scanTitle.Text = title;
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
            scanButton.Searching = scanning;
            videoButton.Enabled = !busy && !readingInventory;
            refreshButton.Enabled = !readingInventory && !installing;
            UpdateInventoryProperties();
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
                if (offer != null && Equals(row.Cells[OfferCheckColumn].Value, true))
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
                row.Cells[OfferDeviceColumn].Value = Display(names);
                row.Cells[OfferDeviceColumn].ToolTipText = names.Length > 0 ? names :
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
            {
                toolTip.Dispose();
                inventoryMenu.Dispose();
                updateMenu.Dispose();
                if (brandImage != null)
                    brandImage.Dispose();
            }
            base.Dispose(disposing);
            if (disposing)
                categoryIcons.Dispose();
        }

        protected override void WndProc(ref Message message)
        {
            bool restoring = false;
            if (message.Msg == WindowSizeMessage)
            {
                bool minimized = message.WParam.ToInt64() == SizeMinimized;
                restoring = wasMinimized && !minimized;
                wasMinimized = minimized;
            }

            base.WndProc(ref message);
            if (restoring && IsHandleCreated && !IsDisposed)
            {
                if (updatePage != null)
                    ResizeScanHero(updatePage);
                PerformLayout();
                RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero,
                    InvalidateWindow | RepaintChildWindows | RepaintImmediately);
            }
        }

        [DllImport("user32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool RedrawWindow(IntPtr window, IntPtr bounds, IntPtr region, uint flags);
    }
}
