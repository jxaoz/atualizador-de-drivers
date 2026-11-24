using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DriverUpdater
{
    internal sealed class InstallWindow : Form
    {
        public const int Success = 0;
        public const int Incomplete = 1;
        public const int SuccessWithRestart = 3;
        public const int IncompleteWithRestart = 4;

        private readonly IList<DriverOffer> selections;
        private readonly Label status = AppStyle.MakeLabel("Preparando a instalação…", AppStyle.Text);
        private readonly ProgressBar activity = new ProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 28, AccessibleName = "Instalação em andamento" };
        private readonly DataGridView grid = AppStyle.MakeGrid("Resultados da instalação");
        private readonly Button closeButton = AppStyle.MakeButton("Fechar", true, 114);
        private readonly ToolTip toolTip = new ToolTip();
        private bool running = true;

        public int ResultCode { get; private set; }

        public InstallWindow(IList<DriverOffer> selections)
        {
            this.selections = selections;
            ResultCode = Incomplete;
            AppStyle.SetWindow(this, "Instalação de drivers", new Size(810, 440), new Size(665, 350));

            var shell = AppStyle.MakeLayout(2);
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 103));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            string count = selections.Count == 1 ? "1 atualização selecionada" : selections.Count + " atualizações selecionadas";
            shell.Controls.Add(AppStyle.MakeHeader("Instalação de drivers", count), 0, 0);

            var body = AppStyle.MakeLayout(3);
            body.Padding = new Padding(24, 12, 24, 17);
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            body.Controls.Add(status, 0, 0);
            grid.ReadOnly = true;
            AppStyle.AddTextColumn(grid, "Atualização", 43, 192);
            AppStyle.AddTextColumn(grid, "Resultado", 57, 234);
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders;
            body.Controls.Add(grid, 0, 1);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 13, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114));
            activity.Dock = DockStyle.Fill;
            activity.Margin = new Padding(0, 16, 0, 14);
            footer.Controls.Add(activity, 0, 0);
            closeButton.Enabled = false;
            closeButton.Click += delegate { Close(); };
            footer.Controls.Add(closeButton, 2, 0);
            body.Controls.Add(footer, 0, 2);
            shell.Controls.Add(body, 0, 1);
            Controls.Add(shell);

            Shown += async delegate { await RunInstallationAsync(); };
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (running && args.CloseReason == CloseReason.UserClosing)
                    args.Cancel = true;
            };
        }

        private async Task RunInstallationAsync()
        {
            var progress = new Progress<string>(SetStatus);
            try
            {
                List<DriverResult> results = await Task.Run(() => DriverServices.Install(selections,
                    ((IProgress<string>)progress).Report, ConfirmLicense));
                DisplayResults(results);
            }
            catch (Exception error)
            {
                string detail = DriverServices.DescribeError(error);
                grid.Rows.Add("Atualizações selecionadas", detail);
                grid.ClearSelection();
                SetStatus("O Windows Update não concluiu a instalação.");
            }
            finally
            {
                running = false;
                activity.Visible = false;
                closeButton.Enabled = true;
                AcceptButton = closeButton;
                closeButton.Focus();
            }
        }

        private void DisplayResults(List<DriverResult> results)
        {
            for (int index = 0; index < results.Count; index++)
            {
                DriverResult result = results[index];
                string title = string.IsNullOrWhiteSpace(result.Title) ? selections[index].Id : result.Title;
                grid.Rows.Add(title, result.Detail);
            }
            grid.ClearSelection();

            int installed = results.Count(result => result.Installed);
            bool reboot = results.Any(result => result.RebootRequired);
            bool complete = installed == selections.Count;
            ResultCode = reboot ? (complete ? SuccessWithRestart : IncompleteWithRestart) : (complete ? Success : Incomplete);
            string summary = installed + " de " + selections.Count + " atualizações instaladas.";
            if (reboot)
                summary += " O Windows solicita a reinicialização do computador.";
            else if (complete && installed > 0)
                summary += " Nenhum pacote instalado solicitou reinicialização.";
            SetStatus(summary);
        }

        private void SetStatus(string message)
        {
            status.Text = message;
            toolTip.SetToolTip(status, message);
        }

        private bool ConfirmLicense(string title, string license)
        {
            if (InvokeRequired)
                return (bool)Invoke(new Func<string, string, bool>(ConfirmLicense), title, license);

            using (var dialog = new Form())
            {
                AppStyle.SetWindow(dialog, "Termos de licença", new Size(680, 440), new Size(490, 325));
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;

                var panel = AppStyle.MakeLayout(3);
                panel.Padding = new Padding(17);
                panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
                panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
                Label licenseTitle = AppStyle.MakeLabel(title, AppStyle.Text);
                panel.Controls.Add(licenseTitle, 0, 0);
                var terms = new TextBox
                {
                    Text = license,
                    ReadOnly = true,
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    Dock = DockStyle.Fill,
                    BackColor = AppStyle.Surface,
                    ForeColor = AppStyle.Text,
                    AccessibleName = "Texto dos termos de licença"
                };
                panel.Controls.Add(terms, 0, 1);

                var actions = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft,
                    WrapContents = false,
                    Margin = new Padding(0, 11, 0, 0)
                };
                Button accept = AppStyle.MakeButton("Aceitar e instalar", true, 156);
                accept.DialogResult = DialogResult.Yes;
                Button decline = AppStyle.MakeButton("Recusar", false, 98);
                decline.Margin = new Padding(0, 0, 9, 0);
                decline.DialogResult = DialogResult.No;
                actions.Controls.Add(accept);
                actions.Controls.Add(decline);
                panel.Controls.Add(actions, 0, 2);
                dialog.Controls.Add(panel);
                dialog.CancelButton = decline;
                dialog.ActiveControl = terms;
                return dialog.ShowDialog(this) == DialogResult.Yes;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                toolTip.Dispose();
            base.Dispose(disposing);
        }
    }
}
