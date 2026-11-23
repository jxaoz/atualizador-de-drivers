using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace DriverUpdater
{
    internal sealed class SpeedometerButton : Button
    {
        private const float CanvasWidth = 320F;
        private const float CanvasHeight = 220F;
        private const float StartAngle = 158F;
        private const float SweepAngle = 224F;
        private static readonly PointF DialCenter = new PointF(160F, 109F);

        private readonly Stopwatch elapsed = new Stopwatch();
        private readonly Timer animation = new Timer { Interval = 33 };
        private bool searching;
        private bool hovered;
        private bool pressed;

        public SpeedometerButton()
        {
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint | ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Size = new Size((int)CanvasWidth, (int)CanvasHeight);
            Text = "Buscar atualizações";
            AccessibleName = Text;
            animation.Tick += delegate { Invalidate(); };
        }

        public bool Searching
        {
            get { return searching; }
            set
            {
                if (searching == value)
                    return;

                searching = value;
                Text = value ? "Buscando atualizações…" : "Buscar atualizações";
                AccessibleName = Text;
                if (value)
                    elapsed.Restart();
                else
                    elapsed.Reset();
                animation.Enabled = value && Visible && !SystemInformation.HighContrast;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            float scale = Math.Min(ClientSize.Width / CanvasWidth, ClientSize.Height / CanvasHeight);
            if (scale <= 0)
                return;

            Graphics graphics = args.Graphics;
            GraphicsState state = graphics.Save();
            graphics.TranslateTransform((ClientSize.Width - CanvasWidth * scale) / 2,
                (ClientSize.Height - CanvasHeight * scale) / 2);
            graphics.ScaleTransform(scale, scale);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            bool highContrast = SystemInformation.HighContrast;
            DrawCard(graphics, highContrast);
            DrawDial(graphics, highContrast);
            DrawNeedle(graphics, highContrast);
            DrawLabel(graphics, highContrast);
            graphics.Restore(state);
        }

        private void DrawCard(Graphics graphics, bool highContrast)
        {
            var bounds = new RectangleF(2F, 2F, 316F, 216F);
            using (GraphicsPath card = RoundedRectangle(bounds, 25F))
            {
                if (highContrast)
                {
                    using (var background = new SolidBrush(SystemColors.Window))
                        graphics.FillPath(background, card);
                }
                else
                {
                    Color right = hovered && Enabled && !searching ? Color.FromArgb(77, 39, 132) : Color.FromArgb(49, 29, 102);
                    using (var background = new LinearGradientBrush(bounds, Color.FromArgb(15, 24, 54), right, 26F))
                        graphics.FillPath(background, card);

                    GraphicsState state = graphics.Save();
                    graphics.SetClip(card, CombineMode.Intersect);
                    DrawGlow(graphics, new RectangleF(-86F, -66F, 239F, 219F), Color.FromArgb(128, 7, 164, 212));
                    DrawGlow(graphics, new RectangleF(207F, 46F, 215F, 218F), Color.FromArgb(117, 215, 46, 157));
                    using (var shapes = new Pen(Color.FromArgb(64, 211, 208, 255), 1F))
                    {
                        graphics.DrawEllipse(shapes, 226F, -94F, 222F, 182F);
                        graphics.DrawEllipse(shapes, 237F, -103F, 222F, 182F);
                        graphics.DrawEllipse(shapes, -86F, 136F, 163F, 129F);
                    }
                    graphics.Restore(state);
                }
                Color borderColor = highContrast ? SystemColors.WindowText : Color.FromArgb(118, 164, 185, 251);
                using (var border = new Pen(borderColor, 1F))
                    graphics.DrawPath(border, card);
            }

            if (Focused && ShowFocusCues)
            {
                Color focusColor = highContrast ? SystemColors.Highlight : Color.White;
                using (var pen = new Pen(focusColor, 1.6F) { DashStyle = DashStyle.Dash })
                using (GraphicsPath focus = RoundedRectangle(new RectangleF(7F, 7F, 306F, 206F), 20F))
                    graphics.DrawPath(pen, focus);
            }
        }

        private void DrawDial(Graphics graphics, bool highContrast)
        {
            const float radius = 76F;
            var track = new RectangleF(DialCenter.X - radius, DialCenter.Y - radius, radius * 2, radius * 2);
            if (highContrast)
            {
                using (var arc = new Pen(SystemColors.WindowText, 8F) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    graphics.DrawArc(arc, track, StartAngle, SweepAngle);
            }
            else
            {
                using (var glow = new Pen(Color.FromArgb(45, 163, 115, 255), 17F) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    graphics.DrawArc(glow, track, StartAngle, SweepAngle);

                var gradientBounds = RectangleF.Inflate(track, 6F, 0F);
                using (var spectrum = new LinearGradientBrush(gradientBounds, Color.Turquoise, Color.HotPink, 0F))
                {
                    spectrum.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { Color.FromArgb(49, 240, 202), Color.FromArgb(85, 221, 255),
                            Color.FromArgb(149, 142, 255), Color.FromArgb(249, 112, 210), Color.FromArgb(255, 170, 131) },
                        Positions = new[] { 0F, .23F, .52F, .79F, 1F }
                    };
                    using (var arc = new Pen(spectrum, 9F) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        graphics.DrawArc(arc, track, StartAngle, SweepAngle);
                }
            }

            Color tickColor = highContrast ? SystemColors.WindowText : Color.FromArgb(185, 219, 231, 255);
            using (var tick = new Pen(tickColor, 1.2F) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                for (int i = 0; i <= 16; i++)
                {
                    float angle = StartAngle + SweepAngle * i / 16F;
                    float length = i % 4 == 0 ? 7F : 3.5F;
                    graphics.DrawLine(tick, Polar(62F, angle), Polar(62F - length, angle));
                }
            }
        }

        private void DrawNeedle(Graphics graphics, bool highContrast)
        {
            double phase = elapsed.Elapsed.TotalSeconds * 2.8;
            float position = searching ? (highContrast ? .68F : .55F + .35F * (float)Math.Sin(phase)) :
                pressed ? .24F : (hovered && Enabled ? .22F : .13F);
            float angle = StartAngle + SweepAngle * position;
            PointF tip = Polar(57F, angle);
            PointF[] needle = { Polar(10F, angle + 180F), Polar(4F, angle + 90F),
                tip, Polar(4F, angle - 90F) };
            Color needleColor = highContrast ? SystemColors.Highlight : Color.White;
            if (!highContrast)
            {
                using (var trace = new Pen(Color.FromArgb(115, 79, 228, 255), 7F) { EndCap = LineCap.Round })
                    graphics.DrawLine(trace, DialCenter, tip);
                using (var halo = new SolidBrush(Color.FromArgb(101, 88, 208, 255)))
                    graphics.FillEllipse(halo, DialCenter.X - 10F, DialCenter.Y - 10F, 20F, 20F);
            }
            using (var brush = new SolidBrush(needleColor))
            {
                graphics.FillPolygon(brush, needle);
                graphics.FillEllipse(brush, DialCenter.X - 5F, DialCenter.Y - 5F, 10F, 10F);
            }
        }

        private void DrawLabel(Graphics graphics, bool highContrast)
        {
            float top = pressed ? 156F : 154F;
            var bounds = new RectangleF(34F, top, 252F, 47F);
            Color textColor = highContrast ? SystemColors.HighlightText : Color.White;
            bool inactive = !Enabled && !searching;
            if (inactive && highContrast)
                textColor = SystemColors.WindowText;

            using (GraphicsPath path = RoundedRectangle(bounds, 15F))
            {
                if (highContrast)
                {
                    using (var background = new SolidBrush(inactive ? SystemColors.Control : SystemColors.Highlight))
                        graphics.FillPath(background, path);
                    using (var border = new Pen(SystemColors.WindowText, 1F))
                        graphics.DrawPath(border, path);
                }
                else
                {
                    Color blue = pressed ? Color.FromArgb(30, 79, 170) : Color.FromArgb(32, 99, 193);
                    Color violet = hovered && Enabled ? Color.FromArgb(122, 53, 216) : Color.FromArgb(102, 65, 208);
                    if (inactive)
                    {
                        blue = Color.FromArgb(47, 55, 90);
                        violet = Color.FromArgb(60, 47, 96);
                    }
                    using (var fill = new LinearGradientBrush(bounds, blue, violet, 7F))
                        graphics.FillPath(fill, path);
                    using (var border = new Pen(Color.FromArgb(143, 184, 201, 255), 1F))
                        graphics.DrawPath(border, path);
                }

                using (var ink = new SolidBrush(textColor))
                using (var textFont = new Font("Segoe UI Semibold", 16F, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var alignment = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    var labelBounds = new RectangleF(bounds.X + 9F, bounds.Y, bounds.Width - 33F, bounds.Height);
                    graphics.DrawString(Text, textFont, ink, labelBounds, alignment);
                }

                float arrowY = top + bounds.Height / 2;
                using (var arrow = new Pen(textColor, 1.6F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    graphics.DrawLine(arrow, 253F, arrowY, 266F, arrowY);
                    graphics.DrawLines(arrow, new[] { new PointF(262F, arrowY - 4F), new PointF(266F, arrowY), new PointF(262F, arrowY + 4F) });
                }
            }
        }

        private static void DrawGlow(Graphics graphics, RectangleF bounds, Color tone)
        {
            using (var ellipse = new GraphicsPath())
            {
                ellipse.AddEllipse(bounds);
                using (var glow = new PathGradientBrush(ellipse))
                {
                    glow.CenterColor = tone;
                    glow.SurroundColors = new[] { Color.FromArgb(0, tone) };
                    graphics.FillEllipse(glow, bounds);
                }
            }
        }

        private static PointF Polar(float radius, float degrees)
        {
            double radians = degrees * Math.PI / 180;
            return new PointF(DialCenter.X + radius * (float)Math.Cos(radians),
                DialCenter.Y + radius * (float)Math.Sin(radians));
        }

        private static GraphicsPath RoundedRectangle(RectangleF box, float radius)
        {
            float diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(box.X, box.Y, diameter, diameter, 180F, 90F);
            path.AddArc(box.Right - diameter, box.Y, diameter, diameter, 270F, 90F);
            path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0F, 90F);
            path.AddArc(box.X, box.Bottom - diameter, diameter, diameter, 90F, 90F);
            path.CloseFigure();
            return path;
        }

        protected override void OnMouseEnter(EventArgs args)
        {
            base.OnMouseEnter(args);
            hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs args)
        {
            base.OnMouseLeave(args);
            hovered = false;
            pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs args)
        {
            base.OnMouseDown(args);
            if (args.Button == MouseButtons.Left)
                pressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs args)
        {
            pressed = false;
            base.OnMouseUp(args);
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs args)
        {
            base.OnGotFocus(args);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs args)
        {
            base.OnLostFocus(args);
            pressed = false;
            Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs args)
        {
            base.OnVisibleChanged(args);
            if (animation != null)
                animation.Enabled = searching && Visible && !SystemInformation.HighContrast;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                animation.Dispose();
            base.Dispose(disposing);
        }
    }
}
