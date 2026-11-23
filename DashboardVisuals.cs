using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DriverUpdater
{
    internal enum DashboardTone { Canvas, Scan, Inventory, Card }

    internal sealed class DashboardPanel : Panel
    {
        private readonly DashboardTone tone;

        public DashboardPanel(DashboardTone tone)
        {
            this.tone = tone;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            if (ClientSize.Width < 3 || ClientSize.Height < 3)
                return;

            Graphics graphics = e.Graphics;
            GraphicsState state = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var bounds = new RectangleF(1, 1, ClientSize.Width - 2, ClientSize.Height - 2);
                float scale = graphics.DpiX / 96F;
                if (tone == DashboardTone.Canvas)
                {
                    PaintCanvas(graphics);
                    return;
                }

                using (GraphicsPath shape = AppStyle.RoundedRectangle(bounds, 20F * scale))
                {
                    if (tone == DashboardTone.Card || SystemInformation.HighContrast)
                    {
                        Color color = tone == DashboardTone.Card ? AppStyle.Surface : AppStyle.Header;
                        using (var fill = new SolidBrush(color))
                        using (var rim = new Pen(AppStyle.Line))
                        {
                            graphics.FillPath(fill, shape);
                            graphics.DrawPath(rim, shape);
                        }
                        return;
                    }

                    bool scan = tone == DashboardTone.Scan;
                    bool dark = AppStyle.UsesDarkPalette;
                    Color start = scan ? AppStyle.Header : dark ? Color.FromArgb(10, 58, 71) : Color.FromArgb(7, 89, 111);
                    Color end = scan ? (dark ? Color.FromArgb(105, 40, 139) : Color.FromArgb(169, 61, 196)) :
                        dark ? Color.FromArgb(38, 49, 114) : Color.FromArgb(39, 72, 188);
                    using (var fill = new LinearGradientBrush(bounds, start, end, 17F))
                    {
                        if (scan)
                        {
                            Color middle = dark ? Color.FromArgb(68, 34, 128) : Color.FromArgb(101, 49, 194);
                            fill.InterpolationColors = new ColorBlend
                            {
                                Colors = new[] { start, middle, end },
                                Positions = new[] { 0F, .62F, 1F }
                            };
                        }
                        graphics.FillPath(fill, shape);
                    }
                    graphics.SetClip(shape, CombineMode.Intersect);
                    PaintTracks(graphics, bounds, scale, scan);
                    if (scan)
                        PaintScanMotion(graphics, bounds, scale);
                }
            }
            finally
            {
                graphics.Restore(state);
            }
        }

        private void PaintCanvas(Graphics graphics)
        {
            if (SystemInformation.HighContrast)
            {
                graphics.FillRectangle(SystemBrushes.Control, ClientRectangle);
                return;
            }

            bool dark = AppStyle.UsesDarkPalette;
            Color start = dark ? Color.FromArgb(24, 19, 38) : Color.FromArgb(250, 248, 255);
            Color end = dark ? Color.FromArgb(16, 26, 44) : Color.FromArgb(235, 245, 255);
            using (var gradient = new LinearGradientBrush(ClientRectangle, start, end, 53F))
                graphics.FillRectangle(gradient, ClientRectangle);

            float width = ClientSize.Width;
            float height = ClientSize.Height;
            float orbit = Math.Min(width, height) * .64F;
            using (var violet = new SolidBrush(Color.FromArgb(dark ? 39 : 35, 180, 145, 250)))
            using (var blue = new Pen(Color.FromArgb(dark ? 75 : 60, 150, 189, 250), 2F * graphics.DpiX / 96F))
            {
                graphics.FillEllipse(violet, width - orbit * .58F, -orbit * .62F, orbit, orbit);
                graphics.DrawEllipse(blue, -orbit * .72F, height - orbit * .59F, orbit, orbit);
            }
        }

        private static void PaintTracks(Graphics graphics, RectangleF bounds, float scale, bool scan)
        {
            float width = bounds.Width;
            float height = bounds.Height;
            float orbit = Math.Min(width * .61F, height * 2.3F);
            var track = new RectangleF(width - orbit * .66F, -orbit * .51F, orbit, orbit);
            var innerTrack = track;
            innerTrack.Inflate(-orbit * .09F, -orbit * .09F);
            Color mint = Color.FromArgb(127, 125, 244, 255);
            Color coral = scan ? Color.FromArgb(200, 255, 119, 182) : Color.FromArgb(180, 118, 249, 218);

            using (var wideTrack = new Pen(mint, 2.5F * scale))
            using (var tightTrack = new Pen(coral, 1.45F * scale))
            using (var stripe = new SolidBrush(Color.FromArgb(50, 133, 221, 255)))
            using (var thinStripe = new SolidBrush(Color.FromArgb(59, 221, 168, 255)))
            using (var light = new SolidBrush(coral))
            {
                graphics.FillPolygon(stripe, new[]
                {
                    new PointF(width * .72F, height), new PointF(width * .86F, 0),
                    new PointF(width * .96F, 0), new PointF(width * .82F, height)
                });
                graphics.FillPolygon(thinStripe, new[]
                {
                    new PointF(width * .67F, height), new PointF(width * .81F, 0),
                    new PointF(width * .825F, 0), new PointF(width * .685F, height)
                });
                graphics.DrawEllipse(wideTrack, track);
                graphics.DrawEllipse(tightTrack, innerTrack);
                float dotY = Math.Min(height * .36F, innerTrack.Bottom - scale);
                float radius = innerTrack.Width / 2F;
                float offset = dotY - (innerTrack.Top + radius);
                float dotX = innerTrack.Left + radius - (float)Math.Sqrt(Math.Max(0F, radius * radius - offset * offset));
                float dot = 7F * scale;
                graphics.FillEllipse(light, dotX - dot / 2F, dotY - dot / 2F, dot, dot);
            }
        }

        private static void PaintScanMotion(Graphics graphics, RectangleF bounds, float scale)
        {
            float width = bounds.Width;
            float height = bounds.Height;
            using (var aqua = new SolidBrush(Color.FromArgb(75, 100, 241, 249)))
            using (var pink = new SolidBrush(Color.FromArgb(95, 255, 117, 198)))
            using (var trail = new Pen(Color.FromArgb(119, 221, 207, 255), 1.45F * scale))
            {
                graphics.FillEllipse(aqua, width * .57F, -height * .34F, height * .73F, height * .63F);
                graphics.FillEllipse(pink, width * .53F, height * .81F, height * .73F, height * .8F);
                graphics.DrawBezier(trail, width * .49F, height * 1.06F, width * .63F, height * .91F,
                    width * .58F, height * .37F, width * .75F, height * .24F);
                graphics.DrawBezier(trail, width * .51F, height * 1.1F, width * .65F, height * .95F,
                    width * .60F, height * .39F, width * .77F, height * .26F);
            }
        }
    }
}
