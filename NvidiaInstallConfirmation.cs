using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace DriverUpdater
{
    internal static class NvidiaInstallConfirmation
    {
        public static bool Confirm(IWin32Window owner, NvidiaDriverOffer offer, int totalUpdates, bool includesFirmware)
        {
            Uri details = offer.DetailsUrl;
            if (details == null || !details.IsAbsoluteUri || details.Scheme != Uri.UriSchemeHttps ||
                !String.Equals(details.IdnHost, "www.nvidia.com", StringComparison.OrdinalIgnoreCase) ||
                !details.IsDefaultPort || details.UserInfo.Length != 0)
            {
                MessageBox.Show(owner, "A página oficial com a licença deste driver da NVIDIA está indisponível.",
                    AppStyle.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            using (var dialog = new Form())
            {
                AppStyle.SetWindow(dialog, "Confirmar instalação", new Size(620, includesFirmware ? 375 : 329), Size.Empty);
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;

                var shell = AppStyle.MakeLayout(2);
                shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 93));
                shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                string selection = totalUpdates == 1 ? "1 atualização selecionada" :
                    totalUpdates + " atualizações selecionadas";
                shell.Controls.Add(AppStyle.MakeHeader("Instalar atualizações", selection), 0, 0);

                var content = AppStyle.MakeLayout(includesFirmware ? 6 : 5);
                content.Padding = new Padding(23, 14, 23, 8);
                content.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
                content.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
                content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
                content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                if (includesFirmware)
                    content.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
                content.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

                Label package = AppStyle.MakeLabel(
                    offer.Title + " " + offer.Version + " será baixado da NVIDIA. A instalação continua na janela do instalador oficial.",
                    AppStyle.Text);
                package.AutoEllipsis = false;
                content.Controls.Add(package, 0, 0);
                var terms = new LinkLabel
                {
                    Text = "Ler detalhes e licença da NVIDIA",
                    LinkColor = AppStyle.Accent,
                    Dock = DockStyle.Fill,
                    Margin = Padding.Empty,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                terms.LinkClicked += delegate { OpenDetails(dialog, details); };
                content.Controls.Add(terms, 0, 1);

                Label consent = AppStyle.MakeLabel(
                    "Ao clicar em Aceitar e continuar, você aceita a licença da NVIDIA para baixar o driver.",
                    AppStyle.Text);
                consent.AutoEllipsis = false;
                content.Controls.Add(consent, 0, 2);
                Label windows = AppStyle.MakeLabel(
                    "O Windows pode solicitar permissão de administrador e pedir que você reinicie o computador ao final.",
                    AppStyle.Muted);
                windows.AutoEllipsis = false;
                content.Controls.Add(windows, 0, 3);
                if (includesFirmware)
                {
                    Label firmware = AppStyle.MakeLabel(
                        "A seleção inclui firmware. Mantenha o computador ligado durante a atualização.",
                        AppStyle.Text);
                    firmware.AutoEllipsis = false;
                    content.Controls.Add(firmware, 0, 4);
                }

                var actions = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft,
                    Margin = Padding.Empty,
                    WrapContents = false
                };
                Button accept = AppStyle.MakeButton("Aceitar e continuar", true, 164);
                accept.DialogResult = DialogResult.Yes;
                Button cancel = AppStyle.MakeButton("Cancelar", false, 96);
                cancel.DialogResult = DialogResult.Cancel;
                cancel.Margin = new Padding(0, 0, 9, 0);
                actions.Controls.Add(accept);
                actions.Controls.Add(cancel);
                content.Controls.Add(actions, 0, content.RowCount - 1);
                shell.Controls.Add(content, 0, 1);
                dialog.Controls.Add(shell);
                dialog.AcceptButton = cancel;
                dialog.CancelButton = cancel;
                dialog.ActiveControl = cancel;
                return dialog.ShowDialog(owner) == DialogResult.Yes;
            }
        }

        private static void OpenDetails(IWin32Window owner, Uri url)
        {
            try
            {
                using (Process browser = Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }))
                {
                }
            }
            catch (Win32Exception)
            {
                ShowBrowserError(owner);
            }
            catch (InvalidOperationException)
            {
                ShowBrowserError(owner);
            }
        }

        private static void ShowBrowserError(IWin32Window owner)
        {
            MessageBox.Show(owner, "O Windows não conseguiu abrir a página da NVIDIA no navegador.",
                AppStyle.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
