using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DriverUpdater
{
    internal static class AppStyle
    {
        public const string Name = "Atualizador de drivers";
        public static readonly Color Surface = SystemColors.Window;
        public static readonly Color Text = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(35, 25, 67);
        public static readonly Color Muted = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(88, 76, 119);
        public static readonly Color Background = SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(246, 243, 255);
        public static readonly Color Header = SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(47, 26, 128);
        public static readonly Color HeaderText = SystemInformation.HighContrast ? SystemColors.HighlightText : Color.White;
        public static readonly Color Accent = SystemInformation.HighContrast ? SystemColors.HotTrack : Color.FromArgb(88, 49, 196);
        public static readonly Color Line = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(229, 223, 246);
        private static readonly Font WindowFont = new Font("Segoe UI", 10F);
        private static readonly Font HeaderFont = new Font("Segoe UI Light", 23F);
        private static readonly Font GridHeaderFont = new Font("Segoe UI", 9.5F, FontStyle.Bold);

        public static void SetWindow(Form window, string title, Size size, Size minimum)
        {
            window.Text = title;
            window.ClientSize = size;
            window.MinimumSize = minimum;
            window.StartPosition = FormStartPosition.CenterScreen;
            window.AutoScaleMode = AutoScaleMode.Dpi;
            window.AutoScaleDimensions = new SizeF(96, 96);
            window.Font = WindowFont;
            window.BackColor = Background;
            Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon != null)
                window.Icon = icon;
        }

        public static Panel MakeHeader(string title, string subtitle)
        {
            var header = new DashboardPanel(DashboardTone.Scan)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(14, 10, 14, 2),
                Padding = new Padding(19, 8, 18, 7)
            };
            var content = MakeLayout(2);
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            var titleLabel = new Label
            {
                Text = title,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                Margin = Padding.Empty,
                Font = HeaderFont,
                ForeColor = HeaderText,
            };
            Label subtitleLabel = MakeLabel(subtitle, HeaderText);
            subtitleLabel.Margin = new Padding(2, 0, 0, 0);
            content.Controls.Add(titleLabel, 0, 0);
            content.Controls.Add(subtitleLabel, 0, 1);
            header.Controls.Add(content);
            return header;
        }

        public static Button MakeButton(string text, bool primary, int width)
        {
            Button button = SystemInformation.HighContrast ? new Button() : new RoundedButton();
            button.Text = text;
            button.Size = new Size(width, 38);
            button.Margin = Padding.Empty;
            SetPrimary(button, primary);
            return button;
        }

        public static void SetPrimary(Button button, bool primary)
        {
            var rounded = button as RoundedButton;
            if (rounded != null)
            {
                rounded.Primary = primary;
                return;
            }

            button.BackColor = primary ? SystemColors.Highlight : SystemColors.Control;
            button.ForeColor = primary ? SystemColors.HighlightText : SystemColors.ControlText;
        }

        public static Label MakeLabel(string text, Color color)
        {
            return new Label
            {
                Text = text,
                BackColor = Color.Transparent,
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
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
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
            grid.RowTemplate.Height = 36;
            grid.DefaultCellStyle.BackColor = Surface;
            grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.SelectionBackColor = SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(235, 222, 255);
            grid.DefaultCellStyle.SelectionForeColor = SystemInformation.HighContrast ? SystemColors.HighlightText : Text;
            grid.DefaultCellStyle.Padding = new Padding(8, 4, 5, 4);
            grid.AlternatingRowsDefaultCellStyle.BackColor = SystemInformation.HighContrast ? Surface : Color.FromArgb(251, 249, 255);
            grid.ColumnHeadersDefaultCellStyle.BackColor = SystemInformation.HighContrast ? Background : Color.FromArgb(241, 236, 254);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
            grid.ColumnHeadersDefaultCellStyle.Font = GridHeaderFont;
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 5, 0);
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
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
        }

        public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 0F)
                return path;

            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private sealed class RoundedButton : Button
        {
            private bool primary;
            private bool hovered;

            public bool Primary
            {
                get { return primary; }
                set
                {
                    primary = value;
                    Invalidate();
                }
            }

            public RoundedButton()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                    ControlStyles.SupportsTransparentBackColor, true);
                FlatStyle = FlatStyle.Flat;
                UseVisualStyleBackColor = false;
                BackColor = Color.Transparent;
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                hovered = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                hovered = false;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (Width < 5 || Height < 5)
                    return;

                Graphics graphics = e.Graphics;
                float scale = graphics.DpiX / 96F;
                var bounds = new RectangleF(2, 2, Width - 4, Height - 4);
                GraphicsState state = graphics.Save();
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath shape = RoundedRectangle(bounds, 11F * scale))
                {
                    bool pressed = hovered && Capture && (MouseButtons & MouseButtons.Left) != 0;
                    bool accented = Enabled && primary;
                    if (accented)
                    {
                        Color start = hovered ? Color.FromArgb(92, 57, 211) : Color.FromArgb(80, 48, 192);
                        Color end = hovered ? Color.FromArgb(134, 57, 200) : Color.FromArgb(113, 47, 176);
                        if (pressed)
                        {
                            start = Header;
                            end = Accent;
                        }
                        using (var gradient = new LinearGradientBrush(bounds, start, end, 16F))
                            graphics.FillPath(gradient, shape);
                    }
                    else
                    {
                        Color surface = !Enabled ? Background : hovered ? Color.FromArgb(241, 234, 255) : Surface;
                        using (var fill = new SolidBrush(surface))
                        using (var border = new Pen(Enabled ? Color.FromArgb(217, 206, 242) : Line))
                        {
                            graphics.FillPath(fill, shape);
                            graphics.DrawPath(border, shape);
                        }
                    }

                    if (Focused && ShowFocusCues && bounds.Width > 8F * scale && bounds.Height > 8F * scale)
                    {
                        bounds.Inflate(-3F * scale, -3F * scale);
                        using (GraphicsPath focus = RoundedRectangle(bounds, 8F * scale))
                        using (var pen = new Pen(accented ? Color.White : Accent, scale) { DashStyle = DashStyle.Dot })
                            graphics.DrawPath(pen, focus);
                    }
                }
                graphics.Restore(state);

                var textBounds = new Rectangle(8, 0, Math.Max(1, Width - 16), Height);
                Color textColor = !Enabled ? Muted : primary ? HeaderText : AppStyle.Text;
                TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding |
                    TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping;
                if (!ShowKeyboardCues)
                    flags |= TextFormatFlags.HidePrefix;
                TextRenderer.DrawText(graphics, Text, Font, textBounds, textColor, flags);
            }
        }
    }
}
