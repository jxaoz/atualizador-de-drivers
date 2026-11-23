using System.Drawing;
using System.Windows.Forms;

namespace DriverUpdater
{
    internal static class AppStyle
    {
        public const string Name = "Atualizador de drivers";
        public static readonly Color Surface = SystemColors.Window;
        public static readonly Color Text = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(28, 40, 61);
        public static readonly Color Muted = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(70, 85, 107);
        public static readonly Color Background = SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(245, 248, 252);
        public static readonly Color Header = SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(22, 38, 67);
        public static readonly Color HeaderText = SystemInformation.HighContrast ? SystemColors.HighlightText : Color.White;
        private static readonly Color Accent = SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(32, 83, 179);
        private static readonly Color Line = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(213, 222, 234);

        public static void SetWindow(Form window, string title, Size size, Size minimum)
        {
            window.Text = title;
            window.ClientSize = size;
            window.MinimumSize = minimum;
            window.StartPosition = FormStartPosition.CenterScreen;
            window.AutoScaleMode = AutoScaleMode.Dpi;
            window.AutoScaleDimensions = new SizeF(96, 96);
            window.Font = new Font("Segoe UI", 10F);
            window.BackColor = Background;
            Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon != null)
                window.Icon = icon;
        }

        public static Panel MakeHeader(string title, string subtitle)
        {
            var header = new Panel { Dock = DockStyle.Fill, BackColor = Header, Margin = Padding.Empty };
            var titleLabel = new Label
            {
                Text = title,
                AutoSize = true,
                Font = new Font("Segoe UI", 23F, FontStyle.Bold),
                ForeColor = HeaderText,
                Location = new Point(24, 13)
            };
            var subtitleLabel = new Label
            {
                Text = subtitle,
                AutoEllipsis = true,
                ForeColor = HeaderText,
                Location = new Point(27, 61),
                Size = new Size(640, 25),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            header.Controls.Add(titleLabel);
            header.Controls.Add(subtitleLabel);
            header.Resize += delegate { subtitleLabel.Width = System.Math.Max(1, header.ClientSize.Width - 51); };
            return header;
        }

        public static Button MakeButton(string text, bool primary, int width)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(width, 38),
                UseVisualStyleBackColor = false,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                BackColor = primary ? Accent : Surface,
                ForeColor = primary ? HeaderText : Text,
                Margin = Padding.Empty
            };
            button.FlatAppearance.BorderColor = primary ? Accent : Line;
            button.EnabledChanged += delegate
            {
                button.BackColor = button.Enabled ? (primary ? Accent : Surface) : Background;
                button.FlatAppearance.BorderColor = button.Enabled && primary ? Accent : Line;
            };
            return button;
        }

        public static Label MakeLabel(string text, Color color)
        {
            return new Label
            {
                Text = text,
                ForeColor = color,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
        }

        public static DataGridView MakeGrid(string name)
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                AccessibleName = name,
                BackgroundColor = Surface,
                BorderStyle = BorderStyle.FixedSingle,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Line,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing,
                ColumnHeadersHeight = 37,
                EnableHeadersVisualStyles = false,
                EditMode = DataGridViewEditMode.EditOnEnter,
                StandardTab = true
            };
            grid.RowTemplate.Height = 33;
            grid.DefaultCellStyle.BackColor = Surface;
            grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.SelectionBackColor = SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(225, 237, 255);
            grid.DefaultCellStyle.SelectionForeColor = SystemInformation.HighContrast ? SystemColors.HighlightText : Text;
            grid.DefaultCellStyle.Padding = new Padding(5, 0, 4, 0);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Background;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(5, 0, 4, 0);
            return grid;
        }

        public static void AddTextColumn(DataGridView grid, string header, int weight, int minimum)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                FillWeight = weight,
                MinimumWidth = minimum,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        public static TableLayoutPanel MakeLayout(int rows)
        {
            return new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = rows,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
        }
    }
}
