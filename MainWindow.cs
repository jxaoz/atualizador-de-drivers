using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DriverUpdater
{
    internal sealed class MainWindow : Form
    {
        private const int OfferCheckColumn = 0;
        private const int OfferIconColumn = 1;
        private const int DriverNameColumn = 2;
        private const int OfferDetailsColumn = 4;
        private const int OfferSourceColumn = 5;
        private const int WindowSizeMessage = 0x0005;
        private const int SizeMinimized = 1;
        private const uint InvalidateWindow = 0x0001;
        private const uint RepaintChildWindows = 0x0080;
        private const uint RepaintImmediately = 0x0100;

        private readonly Panel pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = Padding.Empty };
        private readonly Button updateTab = AppStyle.MakeButton("&Drivers do PC", true, 144);
        private readonly Button inventoryTab = AppStyle.MakeButton("&Dispositivos", false, 168);
        private readonly ThemeToggle themeToggle = new ThemeToggle();
        private readonly SpeedometerButton scanButton = new SpeedometerButton { Dock = DockStyle.Fill, Margin = Padding.Empty };
        private readonly Button installButton = AppStyle.MakeButton("Instalar selecionados", true, 190);
        private readonly Button cancelDownload = AppStyle.MakeButton("Cancelar download", false, 148);
        private readonly Button videoButton = AppStyle.MakeButton("Drivers de vídeo", false, 144);
        private readonly Button manufacturerButton = AppStyle.MakeButton("Pacotes de fabricantes", false, 164);
        private readonly Button refreshButton = AppStyle.MakeButton("Atualizar lista", false, 124);
        private readonly Button inventoryProperties = AppStyle.MakeButton("Propriedades", false, 123);
        private readonly Label scanTitle = HeroTitle("Pronto para buscar", 29);
        private readonly Label updateStatus = AppStyle.MakeLabel("Inicie a busca para ver quais drivers têm atualizações disponíveis para este computador.", AppStyle.HeaderText);
        private readonly Label resultsTitle = SectionTitle("Drivers do PC");
        private readonly Label inventoryTitle = HeroTitle("Dispositivos detectados", 27);
        private readonly Label inventoryStatus = AppStyle.MakeLabel("Carregando dispositivos…", AppStyle.Muted);
        private readonly Label selectionStatus = AppStyle.MakeLabel(string.Empty, AppStyle.Text);
        private readonly DataGridView updateGrid = AppStyle.MakeGrid("Drivers e atualizações do computador");
        private readonly DataGridView inventoryGrid = AppStyle.MakeGrid("Dispositivos detectados");
        private readonly TextBox filter = new TextBox { AccessibleName = "Filtrar dispositivos", Dock = DockStyle.Fill, Margin = Padding.Empty };
        private readonly ToolTip toolTip = new ToolTip();
        private readonly ContextMenuStrip updateMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip inventoryMenu = new ContextMenuStrip();
        private readonly ToolStripMenuItem packageProperties = new ToolStripMenuItem("Detalhes da atualização…");
        private readonly DriverCategoryIcons categoryIcons = new DriverCategoryIcons();
        private readonly TableLayoutPanel updatePage;
        private readonly Control inventoryPage;
        private Bitmap brandImage;
        private CancellationTokenSource activeDownload;
        private List<InstalledDriver> inventory = new List<InstalledDriver>();
        private readonly List<DriverOffer> extraOffers = new List<DriverOffer>();
        private DriverScan displayedScan;
        private bool showingVideoOnly;
        private bool hasInventory;
        private bool readingInventory;
        private bool scanning;
        private bool installing;
        private bool readingManufacturerCatalog;
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
            manufacturerButton.Click += async delegate { await AddManufacturerPackageAsync(); };
            installButton.Click += async delegate { await InstallAsync(); };
            cancelDownload.Click += delegate { CancelPackageDownload(); };
            refreshButton.Click += async delegate { await RefreshInventoryAsync(true); };
            inventoryProperties.Click += delegate { OpenProperties(inventoryGrid.CurrentRow); };
            filter.TextChanged += delegate { DisplayInventory(); };
            inventoryGrid.SelectionChanged += delegate { UpdateInventoryProperties(); };
            AttachDriverActions(inventoryGrid, inventoryMenu);
            AttachDriverActions(updateGrid, updateMenu);
            updateGrid.CellValueChanged += SynchronizePackageSelection;
            updateGrid.CurrentCellDirtyStateChanged += delegate
            {
                if (updateGrid.IsCurrentCellDirty && updateGrid.CurrentCell is DataGridViewCheckBoxCell)
                    updateGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            Shown += async delegate
            {
                await RefreshInventoryAsync();
                if (hasInventory && !IsDisposed && !Disposing)
                    await ScanAsync(false, true);
            };
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (installing && args.CloseReason == CloseReason.UserClosing)
                {
                    args.Cancel = true;
                    if (activeDownload != null)
                        CancelPackageDownload();
                    else
                        SetUpdateStatus("Instalando drivers…", "Aguarde a preparação ou conclua a instalação na outra janela.");
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
                ColumnCount = 3, RowCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty,
                BackColor = Color.Transparent
            };
            resultsHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            resultsHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
            resultsHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 172));
            videoButton.Anchor = AnchorStyles.Right;
            manufacturerButton.Anchor = AnchorStyles.Right;
            resultsHeading.Controls.Add(resultsTitle, 0, 0);
            resultsHeading.Controls.Add(videoButton, 1, 0);
            resultsHeading.Controls.Add(manufacturerButton, 2, 0);
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
            AppStyle.AddTextColumn(updateGrid, "Dispositivo", 40, 145);
            AppStyle.AddTextColumn(updateGrid, "Versão instalada", 17, 115);
            AppStyle.AddTextColumn(updateGrid, "Atualização ou situação", 48, 160);
            AppStyle.AddTextColumn(updateGrid, "Origem", 17, 105);
            updateGrid.Columns[DriverNameColumn].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            updateGrid.Columns[OfferDetailsColumn].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            updateGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders;
            panel.Controls.Add(updateGrid, 0, 1);

            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                Margin = new Padding(0, 9, 0, 0), BackColor = Color.Transparent
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            actions.Controls.Add(selectionStatus, 0, 0);
            actions.Controls.Add(cancelDownload, 1, 0);
            actions.Controls.Add(installButton, 2, 0);
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
            var source = AppStyle.MakeLabel("Windows Update e fabricantes", AppStyle.HeaderText);
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
            AppStyle.AddTextColumn(inventoryGrid, "Versão do driver", 15, 105);
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
            using (Stream iconStream = typeof(MainWindow).Assembly.GetManifestResourceStream("DriverUpdater.AppBrand.png"))
            {
                if (iconStream != null)
                {
                    using (Image icon = Image.FromStream(iconStream))
                        brandImage = new Bitmap(icon);
                }
                else
                    brandImage = Icon.ToBitmap();
            }
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
            if (grid == updateGrid)
            {
                packageProperties.Click += delegate
                {
                    var entry = updateGrid.CurrentRow == null ? null : updateGrid.CurrentRow.Tag as DriverRow;
                    if (entry != null && entry.Offer != null)
                        DriverProperties.Show(this, entry.Offer, entry.Device == null ? entry.Offer.AssociatedDevice : entry.Device.Device, entry.Device);
                };
                menu.Items.Add(packageProperties);
            }

            grid.MouseUp += delegate(object sender, MouseEventArgs args)
            {
                if (args.Button != MouseButtons.Right)
                    return;
                DataGridView.HitTestInfo hit = grid.HitTest(args.X, args.Y);
                if (hit.RowIndex < 0 || hit.ColumnIndex < 0)
                    return;

                DataGridViewRow row = grid.Rows[hit.RowIndex];
                int column = grid == updateGrid ? Math.Max(DriverNameColumn, hit.ColumnIndex) : Math.Max(1, hit.ColumnIndex);
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
            if (grid == updateGrid)
            {
                var entry = row.Tag as DriverRow;
                packageProperties.Visible = entry != null && entry.Device != null && entry.Offer != null;
            }
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
            var entry = row.Tag as DriverRow;
            var driver = entry == null ? row.Tag as InstalledDriver : entry.Device;
            if (driver != null)
                return () => DriverProperties.Show(this, driver);
            return entry == null || entry.Offer == null ? (Action)null :
                () => DriverProperties.Show(this, entry.Offer, entry.Offer.AssociatedDevice);
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

        private async Task RefreshInventoryAsync(bool userRequested = false)
        {
            if (readingInventory)
                return;

            readingInventory = true;
            inventoryStatus.Text = "Carregando dispositivos…";
            toolTip.SetToolTip(inventoryStatus, string.Empty);
            UpdateControls();
            try
            {
                var current = await Task.Run(() => DriverServices.ReadInventory());
                if (IsDisposed || Disposing)
                    return;
                inventory = current;
                hasInventory = true;
                ClearOffers();
                if (userRequested)
                    SetUpdateStatus("Pronto para buscar", "Inicie a busca para conferir o inventário atualizado.");
            }
            catch (Exception error)
            {
                if (IsDisposed || Disposing)
                    return;
                string message = "Falha ao ler os dispositivos. " + DriverServices.DescribeError(error);
                inventoryStatus.Text = message;
                toolTip.SetToolTip(inventoryStatus, message);
                if (inventory.Count > 0)
                    inventoryStatus.Text += " A lista anterior continua disponível.";
                return;
            }
            finally
            {
                readingInventory = false;
                if (!IsDisposed && !Disposing)
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
                Contains(compare, driver.VersionDisplay, query) || Contains(compare, DriverCategoryIcons.GetLabel(driver.Class), query)).ToList();

            inventoryGrid.Rows.Clear();
            foreach (InstalledDriver driver in matches)
            {
                int index = inventoryGrid.Rows.Add(categoryIcons.GetIcon(driver.Class, DeviceDpi), Display(driver.Device),
                    Display(driver.Manufacturer), driver.VersionDisplay, DisplayDate(driver.Date));
                DataGridViewRow row = inventoryGrid.Rows[index];
                row.Tag = driver;
                SetCategoryDescription(row.Cells[0], driver.Class);
            }
            inventoryGrid.ClearSelection();
            UpdateInventoryProperties();
            inventoryTitle.Text = inventory.Count == 1 ? "1 dispositivo detectado" : inventory.Count + " dispositivos detectados";
            int missing = matches.Count(driver => driver.MissingDriver);
            string status = query.Length == 0 ? string.Empty : matches.Count + " de " + inventory.Count + " dispositivos exibidos. ";
            if (missing > 0)
                status += missing == 1 ? "1 dispositivo sem driver." : missing + " dispositivos sem driver.";
            inventoryStatus.Text = inventory.Count == 0 ? "Nenhum dispositivo foi retornado pelo Windows." : status.TrimEnd();
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

        private async Task ScanAsync(bool videoOnly, bool reuseInventory = false)
        {
            if (scanning || installing || readingInventory)
                return;

            scanning = true;
            SetUpdateStatus(videoOnly ? "Buscando vídeo…" : "Buscando drivers…",
                "Consultando o Windows Update e os catálogos oficiais para os dispositivos sem oferta.");
            UpdateControls();
            try
            {
                var current = reuseInventory ? Task.FromResult(inventory) : Task.Run(() => DriverServices.ReadInventory());
                DriverScan result = await DriverSearch.ScanAsync(current, videoOnly);
                if (!IsDisposed && !Disposing)
                    ShowOffers(result, videoOnly);
            }
            catch (Exception error)
            {
                if (!IsDisposed && !Disposing)
                    SetUpdateStatus("Falha na busca", DriverServices.DescribeError(error));
            }
            finally
            {
                scanning = false;
                if (!IsDisposed && !Disposing)
                    UpdateControls();
            }
        }

        private void ShowOffers(DriverScan result, bool videoOnly)
        {
            if (result.Inventory != null)
            {
                inventory = result.Inventory;
                hasInventory = true;
                DisplayInventory();
            }
            displayedScan = result;
            showingVideoOnly = videoOnly;
            extraOffers.Clear();
            RenderDriverRows();
            if (result.Partial)
                SetUpdateStatus("Busca parcial", result.Warnings ?? "Uma das fontes retornou um resultado parcial. Tente buscar novamente.");
            else if (!videoOnly && result.Offers.Count == 0 && result.MissingDrivers > 0)
                SetUpdateStatus("Há dispositivos sem driver", "As fontes não ofereceram um pacote. Veja os dispositivos e o suporte do fabricante.");
            else if (result.Offers.Count == 0)
                SetUpdateStatus("Busca concluída", videoOnly
                    ? "As fontes consultadas não ofereceram atualizações de vídeo. Veja os downloads oficiais em Drivers de vídeo."
                    : "Nenhuma atualização encontrada nas fontes consultadas. ☺");
            else
                SetUpdateStatus("Busca concluída", "Veja a versão instalada e marque as atualizações desejadas na lista.");
            UpdateControls();
        }

        private void RenderDriverRows()
        {
            var offers = displayedScan == null ? extraOffers : displayedScan.Offers.Concat(extraOffers).ToList();
            var selected = new HashSet<DriverOffer>(GetSelectedOffers());
            var rows = DriverSearch.BuildRows(inventory, offers, showingVideoOnly).ToList();
            refreshingOffers = true;
            try
            {
                updateGrid.Rows.Clear();
                foreach (DriverRow row in rows)
                    AddDriverRow(row, row.Offer != null && selected.Contains(row.Offer));
                updateGrid.ClearSelection();
            }
            finally
            {
                refreshingOffers = false;
            }
            int count = rows.Where(row => row.Device != null).Select(row => row.Device).Distinct().Count();
            resultsTitle.Text = (showingVideoOnly ? "Vídeo" : "Drivers do PC") + (hasInventory ? " · " + count : string.Empty) +
                (offers.Count == 0 ? string.Empty : (offers.Count == 1 ? " · 1 oferta" : " · " + offers.Count + " ofertas"));
        }

        private void AddDriverRow(DriverRow entry, bool selected)
        {
            DriverOffer offer = entry.Offer;
            InstalledDriver device = entry.Device;
            string driverClass = device != null && !string.IsNullOrWhiteSpace(device.Class) ? device.Class :
                offer != null ? DriverCategoryIcons.ResolveOfferClass(offer.Class, offer.Title) : null;
            string name = device != null ? device.Device : offer.AssociatedDevice ??
                (offer.ManufacturerPackage == null ? "Pacote oferecido pelo Windows" : "Pacote do fabricante");
            string installed = device != null ? device.VersionDisplay :
                offer.ManufacturerPackage != null ? Display(offer.ManufacturerPackage.InstalledVersion) : "—";
            string status = offer == null ? DriverStatus(device) : offer.Title;
            if (offer != null && offer.ManufacturerPackage != null)
            {
                string version = offer.ManufacturerPackage.Version;
                if (!string.IsNullOrWhiteSpace(version) && !status.Contains(version))
                    status += " · " + version;
            }
            int index = updateGrid.Rows.Add(selected, categoryIcons.GetIcon(driverClass, DeviceDpi), name, installed,
                status, offer == null ? "—" : DriverSearch.GetSource(offer));
            DataGridViewRow row = updateGrid.Rows[index];
            row.Tag = entry;
            SetCategoryDescription(row.Cells[OfferIconColumn], driverClass);
            if (device != null)
                row.Cells[DriverNameColumn].ToolTipText = device.Device + "\r\n" + Display(device.Manufacturer);
            if (offer == null)
            {
                row.Cells[OfferCheckColumn] = new DataGridViewTextBoxCell { Value = string.Empty };
                row.Cells[OfferCheckColumn].ReadOnly = true;
                return;
            }
            row.Cells[OfferDetailsColumn].ToolTipText = status +
                (string.IsNullOrWhiteSpace(offer.Manufacturer) ? string.Empty : "\r\nFornecedor do pacote: " + offer.Manufacturer) +
                "\r\n" + DriverServices.DescribeRestart(offer);
            row.Cells[OfferSourceColumn].ToolTipText = offer.ManufacturerPackage != null ? "Instalador oficial do fabricante." :
                offer.Nvidia != null ? (offer.FromNvidiaApp ? "Recomendação do NVIDIA App confirmada no catálogo da NVIDIA." :
                    "Pacote do catálogo oficial da NVIDIA.") : "Windows Update · " + OfferType(offer);
        }

        private string DriverStatus(InstalledDriver device)
        {
            if (displayedScan == null)
                return "Aguardando busca";
            if (displayedScan.Partial)
                return "Verificação incompleta";
            return device.MissingDriver ? "Sem pacote encontrado" : "Sem atualização encontrada";
        }

        private async Task AddManufacturerPackageAsync()
        {
            if (readingManufacturerCatalog || scanning || installing)
                return;
            readingManufacturerCatalog = true;
            manufacturerButton.Text = "Buscando pacotes…";
            UpdateControls();
            DriverScan catalog;
            try
            {
                catalog = await Task.Run(() => OfficialManufacturerCatalogs.FindForPcAsync(DriverServices.ReadInventory(), false));
            }
            catch (Exception)
            {
                catalog = new DriverScan { Partial = true };
            }
            finally
            {
                readingManufacturerCatalog = false;
                if (!IsDisposed)
                {
                    manufacturerButton.Text = "Pacotes de fabricantes";
                    UpdateControls();
                }
            }
            if (IsDisposed || scanning || installing)
                return;

            using (var picker = new ManufacturerPackageWindow(catalog.Offers, catalog.Partial))
            {
                if (picker.ShowDialog(this) != DialogResult.OK)
                    return;
                DriverOffer offer = picker.SelectedOffer;
                foreach (DataGridViewRow row in updateGrid.Rows)
                {
                    var entry = row.Tag as DriverRow;
                    var existing = entry == null ? null : entry.Offer;
                    if (existing != null && existing.ManufacturerPackage != null &&
                        existing.ManufacturerPackage.DownloadUrl == offer.ManufacturerPackage.DownloadUrl)
                    {
                        updateGrid.CurrentCell = row.Cells[DriverNameColumn];
                        SetUpdateStatus("Pacote já na lista", "Marque o pacote para iniciar a instalação.");
                        return;
                    }
                }
                extraOffers.Add(offer);
                RenderDriverRows();
                SetUpdateStatus("Pacote adicionado", "Marque o pacote do fabricante e clique em Instalar selecionados.");
                UpdateControls();
            }
        }

        private async Task InstallAsync()
        {
            if (installing || scanning)
                return;

            updateGrid.EndEdit();
            List<DriverOffer> selected = GetSelectedOffers();
            if (selected.Count == 0)
                return;

            DriverOffer manufacturer = selected.FirstOrDefault(offer => offer.ManufacturerPackage != null);
            if (manufacturer != null)
            {
                if (selected.Count == 1)
                    await InstallManufacturerPackageAsync(manufacturer);
                else
                    SetUpdateStatus("Instalação do fabricante", "Execute o pacote do fabricante separadamente. Ele pode incluir vários drivers.");
                return;
            }

            List<DriverOffer> windows = selected.Where(offer => offer.Nvidia == null).ToList();
            List<DriverOffer> direct = selected.Where(offer => offer.Nvidia != null).ToList();
            if (direct.Count > 1)
            {
                SetUpdateStatus("Seleção de vídeo", "Marque um pacote da NVIDIA por vez. Você pode marcar outros tipos de driver juntos.");
                return;
            }
            DriverOffer nvidia = direct.FirstOrDefault();
            if (nvidia != null && windows.Any(offer => ConflictsWithNvidia(offer, nvidia)))
            {
                SetUpdateStatus("Seleção de vídeo", "Escolha uma fonte por vez para atualizar o mesmo adaptador de vídeo.");
                return;
            }

            bool firmware = windows.Any(IsFirmware);
            if (nvidia == null ? !ConfirmWindowsInstall(windows, firmware) :
                !DriverLicenseConfirmation.Confirm(this, nvidia.Nvidia, selected.Count, firmware))
                return;

            installing = true;
            bool useDarkTheme = AppStyle.IsDark;
            bool windowsInstalledBeforeNvidia = false;
            UpdateControls();
            try
            {
                if (windows.Count > 0)
                {
                    SetUpdateStatus("Instalando drivers…", "Acompanhe os resultados do Windows Update na janela de instalação.");
                    int result = await Task.Run(() => RunWindowsInstaller(windows, useDarkTheme));
                    if (nvidia == null || result != InstallWindow.Success)
                    {
                        ClearOffers();
                        string summary = InstallSummary(result);
                        if (nvidia != null)
                            summary += " O driver da NVIDIA continua pendente.";
                        SetUpdateStatus("Resultado da instalação", summary);
                        await RefreshInventoryAsync();
                        return;
                    }
                    windowsInstalledBeforeNvidia = true;
                }

                await RunNvidiaInstallerAsync(nvidia.Nvidia);
                if (!windowsInstalledBeforeNvidia)
                    ClearOffers();
                bool? restartPending = await Task.Run(() => DriverServices.IsRestartPending());
                ShowNvidiaCompletion(restartPending);
                if (!windowsInstalledBeforeNvidia)
                    await RefreshInventoryAsync();
            }
            catch (OperationCanceledException)
            {
                SetUpdateStatus("Download cancelado", "O download do pacote da NVIDIA foi interrompido.");
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
                if (windowsInstalledBeforeNvidia)
                    ClearOffers();
                installing = false;
                UpdateControls();
            }
            if (windowsInstalledBeforeNvidia)
                await RefreshInventoryAsync();
        }

        private async Task InstallManufacturerPackageAsync(DriverOffer offer)
        {
            string confirmation = "Baixar e abrir o instalador de " + offer.Manufacturer + "?\r\n\r\n" + offer.Title +
                "\r\n" + offer.ManufacturerPackage.DownloadUrl.IdnHost;
            if (!string.IsNullOrWhiteSpace(offer.ManufacturerPackage.InstalledVersion))
                confirmation += "\r\nVersão instalada: " + offer.ManufacturerPackage.InstalledVersion;
            confirmation += "\r\n\r\nConfirme no instalador oficial que o pacote é indicado para o seu modelo e sua versão do Windows.";
            bool confirmed = offer.ManufacturerPackage.Source == "AMD" ? DriverLicenseConfirmation.ConfirmAmd(this, offer) :
                MessageBox.Show(this, confirmation, AppStyle.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            if (!confirmed)
                return;

            installing = true;
            using (var cancellation = new CancellationTokenSource())
            {
                activeDownload = cancellation;
                UpdateControls();
                SetUpdateStatus("Baixando pacote…", "Conectando ao servidor oficial do fabricante.");
                IProgress<int> progress = new Progress<int>(percent =>
                {
                    if (activeDownload == cancellation && !cancellation.IsCancellationRequested)
                        SetUpdateStatus("Baixando pacote…", "Download do fabricante · " + percent + "%");
                });
                IProgress<bool> ready = new Progress<bool>(_ =>
                {
                    if (activeDownload == cancellation)
                    {
                        activeDownload = null;
                        UpdateControls();
                        SetUpdateStatus("Instalador do fabricante", "O Windows verifica a assinatura antes de abrir o instalador oficial.");
                    }
                });
                try
                {
                    if (offer.ManufacturerPackage.ArchiveHash != null)
                    {
                        bool applicable = await Task.Run(() => ManufacturerPackages.FindForPc(DriverServices.ReadInventory(), false)
                            .Any(current => current.ManufacturerPackage.DownloadUrl == offer.ManufacturerPackage.DownloadUrl));
                        if (!applicable)
                            throw new InvalidOperationException("O catálogo deixou de confirmar este pacote para o computador.");
                    }
                    await ManufacturerPackages.DownloadAndInstallAsync(offer.ManufacturerPackage, progress,
                        () => ready.Report(true), cancellation.Token);
                    bool? restart = await Task.Run(() => DriverServices.IsRestartPending());
                    SetUpdateStatus(restart == true ? "Reinício recomendado" : "Instalador encerrado", restart == true
                        ? "O Windows indica um reinício pendente. Salve seu trabalho antes de reiniciar."
                        : "Confira o resultado e a indicação de reinício do instalador do fabricante.");
                    await RefreshInventoryAsync();
                }
                catch (OperationCanceledException)
                {
                    SetUpdateStatus("Download cancelado", "A transferência do pacote do fabricante foi interrompida.");
                }
                catch (Exception error)
                {
                    SetUpdateStatus("Pacote não concluído", DriverServices.DescribeError(error));
                }
                finally
                {
                    activeDownload = null;
                    installing = false;
                    UpdateControls();
                }
            }
        }

        private void ShowNvidiaCompletion(bool? restartPending)
        {
            if (restartPending == true)
                SetUpdateStatus("Reinício recomendado", "O Windows indica um reinício pendente. Salve seu trabalho e reinicie para concluir as atualizações.");
            else
                SetUpdateStatus("Instalador NVIDIA encerrado", "Siga a orientação de reinício exibida pela NVIDIA. Busque novamente para conferir a versão.");
        }

        private bool ConfirmWindowsInstall(IList<DriverOffer> windows, bool includesFirmware)
        {
            int count = windows.Count;
            string action = count == 1 ? "Instalar a atualização selecionada?" :
                "Instalar as " + count + " atualizações selecionadas?";
            string restart = windows.Any(offer => offer.Restart == RestartBehavior.Required)
                ? "Pelo menos um pacote exige reinício após instalar."
                : windows.All(offer => offer.Restart == RestartBehavior.NotExpected)
                    ? "Os pacotes selecionados dispensam reinício."
                    : "O resultado da instalação informará se é preciso reiniciar.";
            string message = action + "\r\n\r\nO Windows pode solicitar permissão de administrador.\r\n" + restart;
            if (includesFirmware)
                message += "\r\n\r\nA seleção inclui firmware. Mantenha o computador ligado durante a atualização.";
            return MessageBox.Show(this, message, AppStyle.Name, MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private static int RunWindowsInstaller(IList<DriverOffer> windows, bool useDarkTheme)
        {
            using (Process process = Program.StartInstaller(windows, useDarkTheme))
            {
                if (process == null)
                    throw new InvalidOperationException("O Windows não abriu a janela de instalação.");
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        private async Task RunNvidiaInstallerAsync(NvidiaDriverOffer offer)
        {
            SetUpdateStatus("Conferindo NVIDIA…", "Verificando o adaptador e a versão de driver instalada.");
            var current = await Task.Run(() => DriverServices.ReadInventory());
            if (!current.Any(adapter => NvidiaCatalog.IsStillApplicable(adapter, offer)))
                throw new InvalidOperationException("Esta atualização da NVIDIA deixou de se aplicar ao computador. Faça uma nova busca.");

            string path = null;
            using (var cancellation = new CancellationTokenSource())
            {
                activeDownload = cancellation;
                UpdateControls();
                SetUpdateStatus("Baixando NVIDIA…", "Preparando o download do pacote oficial.");
                IProgress<int> progress = new Progress<int>(percent =>
                {
                    if (activeDownload == cancellation && !cancellation.IsCancellationRequested)
                    {
                        string message = percent == 100 ? "Assinatura da NVIDIA verificada." :
                            "Download do pacote oficial · " + percent + "%";
                        SetUpdateStatus("Baixando NVIDIA…", message);
                    }
                });
                try
                {
                    path = await NvidiaPackage.DownloadAsync(offer.DownloadUrl, progress, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    activeDownload = null;
                    UpdateControls();
                    SetUpdateStatus("Instalador NVIDIA", "Conclua a instalação na janela oficial da NVIDIA.");
                    await Task.Run(() => NvidiaPackage.RunInstaller(path));
                }
                finally
                {
                    activeDownload = null;
                    if (path != null)
                        NvidiaPackage.Discard(path);
                }
            }
        }

        private void CancelPackageDownload()
        {
            if (activeDownload == null || activeDownload.IsCancellationRequested)
                return;
            activeDownload.Cancel();
            cancelDownload.Enabled = false;
            SetUpdateStatus("Cancelando download…", "O aplicativo está encerrando a transferência do pacote.");
        }

        private static bool ConflictsWithNvidia(DriverOffer windows, DriverOffer nvidia)
        {
            if (!DriverSearch.IsVideoOffer(windows))
                return false;
            if (windows.HardwareIds == null || windows.HardwareIds.Length == 0 || nvidia.HardwareIds == null || nvidia.HardwareIds.Length == 0)
                return true;
            return windows.HardwareIds.Intersect(nvidia.HardwareIds, StringComparer.OrdinalIgnoreCase).Any();
        }

        private static string InstallSummary(int result)
        {
            switch (result)
            {
                case InstallWindow.Success:
                    return "Instalação concluída. O Windows Update não indicou reinicialização.";
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
            displayedScan = null;
            extraOffers.Clear();
            showingVideoOnly = false;
            RenderDriverRows();
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
            scanButton.Enabled = !busy && !readingInventory;
            scanButton.Searching = scanning;
            videoButton.Enabled = !busy && !readingInventory;
            manufacturerButton.Enabled = !busy && !readingManufacturerCatalog;
            refreshButton.Enabled = !readingInventory && !busy;
            UpdateInventoryProperties();
            int count = GetSelectedOffers().Count;
            installButton.Enabled = !busy && count > 0;
            cancelDownload.Visible = activeDownload != null;
            cancelDownload.Enabled = activeDownload != null && !activeDownload.IsCancellationRequested;
            selectionStatus.Text = count == 0 ? string.Empty :
                count == 1 ? "1 atualização selecionada" : count + " atualizações selecionadas";
        }

        private List<DriverOffer> GetSelectedOffers()
        {
            var selected = new List<DriverOffer>();
            var seen = new HashSet<DriverOffer>();
            foreach (DataGridViewRow row in updateGrid.Rows)
            {
                var entry = row.Tag as DriverRow;
                if (entry != null && entry.Offer != null && Equals(row.Cells[OfferCheckColumn].Value, true) && seen.Add(entry.Offer))
                    selected.Add(entry.Offer);
            }
            return selected;
        }

        private void SynchronizePackageSelection(object sender, DataGridViewCellEventArgs args)
        {
            if (refreshingOffers || args.RowIndex < 0 || args.ColumnIndex != OfferCheckColumn)
                return;
            DataGridViewRow changed = updateGrid.Rows[args.RowIndex];
            var selected = changed.Tag as DriverRow;
            if (selected == null || selected.Offer == null)
                return;
            refreshingOffers = true;
            try
            {
                object value = changed.Cells[OfferCheckColumn].Value;
                foreach (DataGridViewRow row in updateGrid.Rows)
                {
                    var entry = row.Tag as DriverRow;
                    if (entry != null && entry.Offer == selected.Offer && !Equals(row.Cells[OfferCheckColumn].Value, value))
                        row.Cells[OfferCheckColumn].Value = value;
                }
            }
            finally
            {
                refreshingOffers = false;
            }
            UpdateControls();
        }

        private static bool Contains(CompareInfo compare, string text, string query)
        {
            return text != null && compare.IndexOf(text, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
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
