using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DriverUpdater
{
    internal sealed class AllDriversCurrent : Control
    {
        public const string Message = "Todos os drivers estão em dia!";

        private static readonly Font MessageFont = new Font("Segoe UI Semibold", 15F);
        private const TextFormatFlags MessageFlags = TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis |
            TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping;

        public AllDriversCurrent()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Text = Message;
            AccessibleName = Message;
            AccessibleRole = AccessibleRole.StaticText;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            using (var background = new SolidBrush(AppStyle.Surface))
                graphics.FillRectangle(background, ClientRectangle);

            float scale = graphics.DpiX / 96F;
            int padding = (int)Math.Round(8F * scale);
            var content = Rectangle.Inflate(ClientRectangle, -padding, -padding);
            if (content.Width <= 0 || content.Height <= 0)
                return;

            Size caption = TextRenderer.MeasureText(graphics, Message, MessageFont, Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            int badgeSize = (int)Math.Round(65F * scale);
            int gap = (int)Math.Round(10F * scale);
            if (content.Height >= badgeSize + gap + caption.Height)
            {
                int top = content.Top + (content.Height - badgeSize - gap - caption.Height) / 2;
                var badge = new RectangleF(content.Left + (content.Width - badgeSize) / 2F, top, badgeSize, badgeSize);
                PaintFace(graphics, badge);
                var captionBounds = new Rectangle(content.Left, top + badgeSize + gap, content.Width, caption.Height);
                TextRenderer.DrawText(graphics, Message, MessageFont, captionBounds, AppStyle.Text,
                    MessageFlags | TextFormatFlags.HorizontalCenter);
                return;
            }

            badgeSize = Math.Min((int)Math.Round(36F * scale), content.Height);
            if (badgeSize < 18F * scale || content.Width < badgeSize + gap + caption.Width)
            {
                TextRenderer.DrawText(graphics, Message, MessageFont, content, AppStyle.Text,
                    MessageFlags | TextFormatFlags.HorizontalCenter);
                return;
            }

            int left = content.Left + (content.Width - badgeSize - gap - caption.Width) / 2;
            PaintFace(graphics, new RectangleF(left, content.Top + (content.Height - badgeSize) / 2F, badgeSize, badgeSize));
            var textBounds = new Rectangle(left + badgeSize + gap, content.Top, caption.Width, content.Height);
            TextRenderer.DrawText(graphics, Message, MessageFont, textBounds, AppStyle.Text, MessageFlags);
        }

        private static void PaintFace(Graphics graphics, RectangleF badge)
        {
            GraphicsState state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float inset = badge.Width * .12F;
            RectangleF face = RectangleF.Inflate(badge, -inset, -inset);
            bool highContrast = SystemInformation.HighContrast;
            if (!highContrast)
            {
                Color rim = AppStyle.UsesDarkPalette ? Color.FromArgb(38, 61, 75) : Color.FromArgb(224, 245, 247);
                using (var halo = new SolidBrush(rim))
                    graphics.FillEllipse(halo, badge);
                using (var fill = new LinearGradientBrush(face,
                    Color.FromArgb(198, 250, 226), Color.FromArgb(150, 219, 249), 48F))
                    graphics.FillEllipse(fill, face);
            }

            Color ink = highContrast ? SystemColors.WindowText : Color.FromArgb(27, 80, 103);
            using (var brush = new SolidBrush(ink))
            using (var pen = new Pen(ink, Math.Max(1F, face.Width * .046F)))
            {
                float middleX = face.Left + face.Width / 2F;
                float middleY = face.Top + face.Height / 2F;
                float eye = face.Width * .075F;
                float offset = face.Width * .145F;
                float eyeY = middleY - face.Height * .15F;

                if (highContrast)
                    graphics.DrawEllipse(pen, face);
                graphics.FillEllipse(brush, middleX - offset - eye / 2F, eyeY, eye, eye);
                graphics.FillEllipse(brush, middleX + offset - eye / 2F, eyeY, eye, eye);

                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                graphics.DrawArc(pen, middleX - face.Width * .21F, middleY - face.Height * .14F,
                    face.Width * .42F, face.Height * .37F, 33F, 114F);
            }
            graphics.Restore(state);
        }
    }
}
