using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DriverUpdater
{
    internal static class AppStyle
    {
        public const string Name = "Atualizador de drivers";
        private const int CaptionColorAttribute = 35;
        private const int CaptionTextColorAttribute = 36;
        private const int DefaultCaptionColor = -1;
        private static readonly Font WindowFont = new Font("Segoe UI", 10F);
        private static readonly Font HeaderFont = new Font("Segoe UI Light", 23F);
        private static readonly Font GridHeaderFont = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        private static readonly ToolStripRenderer DarkMenuRenderer = new ToolStripProfessionalRenderer(new DarkMenuColors());

        public static bool IsDark { get; set; }
        public static bool UsesDarkPalette { get { return IsDark && !SystemInformation.HighContrast; } }
        public static Color Surface { get { return Palette(SystemColors.Window, Color.FromArgb(33, 27, 49), SystemColors.Window); } }
        public static Color Text { get { return Palette(Color.FromArgb(35, 25, 67), Color.FromArgb(245, 240, 255), SystemColors.WindowText); } }
        public static Color Muted { get { return Palette(Color.FromArgb(88, 76, 119), Color.FromArgb(190, 177, 213), SystemColors.WindowText); } }
        public static Color Background { get { return Palette(Color.FromArgb(246, 243, 255), Color.FromArgb(21, 18, 34), SystemColors.Control); } }
        public static Color Header { get { return Palette(Color.FromArgb(47, 26, 128), Color.FromArgb(37, 22, 78), SystemColors.Highlight); } }
        public static Color HeaderText { get { return SystemInformation.HighContrast ? SystemColors.HighlightText : Color.White; } }
        public static Color Accent { get { return Palette(Color.FromArgb(88, 49, 196), Color.FromArgb(188, 163, 255), SystemColors.HotTrack); } }
        public static Color Line { get { return Palette(Color.FromArgb(229, 223, 246), Color.FromArgb(66, 54, 86), SystemColors.WindowText); } }

        private static Color Palette(Color light, Color dark, Color highContrast)
        {
            return SystemInformation.HighContrast ? highContrast : IsDark ? dark : light;
        }

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
            window.ForeColor = Text;
            window.HandleCreated += delegate { ApplyCaption(window); };
            window.Shown += delegate { ApplyTheme(window); };
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
            Label titleLabel = MakeLabel(title, HeaderText);
            titleLabel.Font = HeaderFont;
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
            LabelTone tone = color == HeaderText ? LabelTone.Header : color == Muted ? LabelTone.Muted : LabelTone.Text;
            return new PaletteLabel(tone)
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
            grid.DefaultCellStyle.Padding = new Padding(8, 4, 5, 4);
            grid.ColumnHeadersDefaultCellStyle.Font = GridHeaderFont;
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 5, 0);
            ApplyGridTheme(grid);
            return grid;
        }

        public static void ApplyTheme(Control root)
        {
            ApplyThemeToTree(root);
            root.Invalidate(true);
        }

        private static void ApplyThemeToTree(Control control)
        {
            var form = control as Form;
            var grid = control as DataGridView;
            var input = control as TextBoxBase;
            var link = control as LinkLabel;
            var label = control as PaletteLabel;
            var menu = control as ContextMenuStrip;
            if (form != null)
            {
                form.BackColor = Background;
                form.ForeColor = Text;
                ApplyCaption(form);
            }
            else if (grid != null)
            {
                ApplyGridTheme(grid);
            }
            else if (input != null)
            {
                input.BackColor = Surface;
                input.ForeColor = Text;
            }
            else if (link != null)
            {
                link.LinkColor = Accent;
                link.VisitedLinkColor = Accent;
                link.ActiveLinkColor = Text;
            }
            else if (label != null)
            {
                label.ApplyTheme();
            }
            else if (menu != null)
            {
                menu.BackColor = Surface;
                menu.ForeColor = Text;
                if (UsesDarkPalette)
                    menu.Renderer = DarkMenuRenderer;
                else
                    menu.RenderMode = ToolStripRenderMode.ManagerRenderMode;
            }

            foreach (Control child in control.Controls)
                ApplyThemeToTree(child);
        }

        private static void ApplyGridTheme(DataGridView grid)
        {
            grid.BackgroundColor = Surface;
            grid.GridColor = Line;
            grid.DefaultCellStyle.BackColor = Surface;
            grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.SelectionBackColor = Palette(Color.FromArgb(235, 222, 255), Color.FromArgb(74, 52, 116), SystemColors.Highlight);
            grid.DefaultCellStyle.SelectionForeColor = SystemInformation.HighContrast ? SystemColors.HighlightText : Text;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Palette(Color.FromArgb(251, 249, 255), Color.FromArgb(39, 32, 58), Surface);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Palette(Color.FromArgb(241, 236, 254), Color.FromArgb(49, 39, 74), Background);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
        }

        private static void ApplyCaption(Form form)
        {
            if (!form.IsHandleCreated)
                return;

            // Windows 11 accepts explicit COLORREF values. Earlier Windows releases retain their default caption.
            int caption = UsesDarkPalette ? ColorTranslator.ToWin32(Background) : DefaultCaptionColor;
            int captionText = UsesDarkPalette ? ColorTranslator.ToWin32(Text) : DefaultCaptionColor;
            DwmSetWindowAttribute(form.Handle, CaptionColorAttribute, ref caption, sizeof(int));
            DwmSetWindowAttribute(form.Handle, CaptionTextColorAttribute, ref captionText, sizeof(int));
        }

        [DllImport("dwmapi.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

        private enum LabelTone
        {
            Text,
            Muted,
            Header
        }

        private sealed class PaletteLabel : Label
        {
            private readonly LabelTone tone;

            public PaletteLabel(LabelTone tone)
            {
                this.tone = tone;
            }

            public void ApplyTheme()
            {
                ForeColor = tone == LabelTone.Header ? HeaderText : tone == LabelTone.Muted ? Muted : AppStyle.Text;
            }
        }

        private sealed class DarkMenuColors : ProfessionalColorTable
        {
            private static readonly Color Selected = Color.FromArgb(74, 52, 116);

            public override Color ToolStripDropDownBackground { get { return Surface; } }
            public override Color ImageMarginGradientBegin { get { return Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Surface; } }
            public override Color ImageMarginGradientEnd { get { return Surface; } }
            public override Color MenuBorder { get { return Line; } }
            public override Color MenuItemBorder { get { return Accent; } }
            public override Color MenuItemSelected { get { return Selected; } }
            public override Color MenuItemSelectedGradientBegin { get { return Selected; } }
            public override Color MenuItemSelectedGradientEnd { get { return Selected; } }
            public override Color SeparatorDark { get { return Line; } }
            public override Color SeparatorLight { get { return Surface; } }
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
                FlatStyle = FlatStyle.Flat;
                // The rounded corners need the parent background painted before OnPaint.
                SetStyle(ControlStyles.Opaque, false);
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                    ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                    ControlStyles.SupportsTransparentBackColor, true);
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
                    if (accented && !SystemInformation.HighContrast)
                    {
                        Color start = hovered ? Color.FromArgb(92, 57, 211) : Color.FromArgb(80, 48, 192);
                        Color end = hovered ? Color.FromArgb(134, 57, 200) : Color.FromArgb(113, 47, 176);
                        if (pressed)
                        {
                            start = Header;
                            end = Color.FromArgb(88, 49, 196);
                        }
                        using (var gradient = new LinearGradientBrush(bounds, start, end, 16F))
                            graphics.FillPath(gradient, shape);
                    }
                    else
                    {
                        Color hover = Palette(Color.FromArgb(241, 234, 255), Color.FromArgb(52, 42, 77), SystemColors.Control);
                        Color surface = accented ? SystemColors.Highlight : !Enabled ? Background : hovered ? hover : Surface;
                        using (var fill = new SolidBrush(surface))
                        using (var border = new Pen(Enabled ? Palette(Color.FromArgb(217, 206, 242), Color.FromArgb(88, 70, 114), Line) : Line))
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
