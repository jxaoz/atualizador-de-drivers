using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DriverUpdater
{
    internal sealed class ThemeToggle : CheckBox
    {
        public ThemeToggle()
        {
            Text = "Modo noturno";
            AccessibleName = Text;
            Size = new Size(151, 38);
            Margin = Padding.Empty;
            AutoSize = false;
            Cursor = Cursors.Hand;

            if (SystemInformation.HighContrast)
            {
                FlatStyle = FlatStyle.System;
                return;
            }

            FlatStyle = FlatStyle.Flat;
            // Paint the themed parent around the switch before drawing its rounded track.
            SetStyle(ControlStyles.Opaque, false);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            UseVisualStyleBackColor = false;
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (SystemInformation.HighContrast)
            {
                base.OnPaint(e);
                return;
            }

            float scale = DeviceDpi / 96F;
            float trackWidth = 43F * scale;
            float trackHeight = 23F * scale;
            var track = new RectangleF(Width - trackWidth - 4F * scale,
                (Height - trackHeight) / 2F, trackWidth, trackHeight);
            if (track.Left < 0F || track.Top < 0F)
                return;

            Graphics graphics = e.Graphics;
            GraphicsState state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color accent = Enabled ? AppStyle.Accent : AppStyle.Muted;
            using (GraphicsPath outline = AppStyle.RoundedRectangle(track, trackHeight / 2F))
            using (var fill = new SolidBrush(Checked ? accent : Color.FromArgb(40, accent)))
            using (var border = new Pen(Color.FromArgb(125, accent), scale))
            {
                graphics.FillPath(fill, outline);
                if (!Checked)
                    graphics.DrawPath(border, outline);
            }

            float inset = 3F * scale;
            float diameter = trackHeight - 2F * inset;
            float thumbLeft = Checked ? track.Right - inset - diameter : track.Left + inset;
            var thumb = new RectangleF(thumbLeft, track.Top + inset, diameter, diameter);
            Color thumbColor = Checked ? Color.FromArgb(253, 249, 255) : AppStyle.Surface;
            using (var fill = new SolidBrush(thumbColor))
                graphics.FillEllipse(fill, thumb);
            var center = new PointF(thumb.X + diameter / 2F, thumb.Y + diameter / 2F);
            if (Checked)
                DrawMoon(graphics, center, scale, thumbColor);
            else
                DrawSun(graphics, center, scale, accent);

            if (Focused && ShowFocusCues)
            {
                var focusBounds = new RectangleF(scale, scale, Width - 2F * scale, Height - 2F * scale);
                using (GraphicsPath focus = AppStyle.RoundedRectangle(focusBounds, 6F * scale))
                using (var pen = new Pen(accent, scale) { DashStyle = DashStyle.Dot })
                    graphics.DrawPath(pen, focus);
            }
            graphics.Restore(state);

            var textBounds = new Rectangle((int)(7F * scale), 0, Math.Max(1, (int)(track.Left - 11F * scale)), Height);
            TextRenderer.DrawText(graphics, Text, Font, textBounds, Enabled ? AppStyle.Text : AppStyle.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
                TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping);
        }

        private static void DrawMoon(Graphics graphics, PointF center, float scale, Color thumbColor)
        {
            using (var crescent = new SolidBrush(Color.FromArgb(79, 49, 149)))
            using (var cutout = new SolidBrush(thumbColor))
            {
                graphics.FillEllipse(crescent, center.X - 4F * scale, center.Y - 4F * scale, 8F * scale, 8F * scale);
                graphics.FillEllipse(cutout, center.X - scale, center.Y - 5F * scale, 6F * scale, 6F * scale);
            }
        }

        private static void DrawSun(Graphics graphics, PointF center, float scale, Color color)
        {
            using (var pen = new Pen(color, scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                graphics.DrawEllipse(pen, center.X - 2F * scale, center.Y - 2F * scale, 4F * scale, 4F * scale);
                for (int ray = 0; ray < 8; ray++)
                {
                    double angle = ray * Math.PI / 4D;
                    float x = (float)Math.Cos(angle) * scale;
                    float y = (float)Math.Sin(angle) * scale;
                    graphics.DrawLine(pen, center.X + 3.6F * x, center.Y + 3.6F * y,
                        center.X + 5F * x, center.Y + 5F * y);
                }
            }
        }
    }
}
