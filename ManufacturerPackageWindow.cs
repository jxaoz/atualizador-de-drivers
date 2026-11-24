using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DriverUpdater
{
    internal sealed class ManufacturerPackageWindow : Form
    {
        private readonly ComboBox packages = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox link = new TextBox { Dock = DockStyle.Fill, AccessibleName = "Link oficial do instalador" };
        private readonly Label message = AppStyle.MakeLabel("O pacote ficará disponível na lista principal.", AppStyle.Muted);

        public DriverOffer SelectedOffer { get; private set; }

        public ManufacturerPackageWindow(IList<DriverOffer> catalog)
        {
            AppStyle.SetWindow(this, "Pacotes de fabricantes", new Size(620, 315), Size.Empty);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            var shell = AppStyle.MakeLayout(2);
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.Controls.Add(AppStyle.MakeHeader("Pacotes de fabricantes", "Escolha um pacote identificado ou informe o link oficial do instalador."), 0, 0);
            var content = AppStyle.MakeLayout(6);
            content.Padding = new Padding(21, 10, 21, 12);
            for (int index = 0; index < 4; index++) content.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            content.Controls.Add(AppStyle.MakeLabel("Catálogo identificado neste PC", AppStyle.Text), 0, 0);
            packages.AccessibleName = "Pacotes do catálogo";
            packages.BackColor = AppStyle.Surface;
            packages.ForeColor = AppStyle.Text;
            packages.Items.Add(catalog.Count == 0 ? "Nenhum pacote de catálogo identificado" : "Selecione um pacote do catálogo");
            foreach (DriverOffer offer in catalog)
                packages.Items.Add(offer.Manufacturer + " · " + offer.Title);
            packages.SelectedIndex = 0;
            packages.Enabled = catalog.Count > 0;
            packages.SelectedIndexChanged += delegate
            {
                string installed = packages.SelectedIndex > 0 ? catalog[packages.SelectedIndex - 1].ManufacturerPackage.InstalledVersion : null;
                message.Text = string.IsNullOrWhiteSpace(installed) ? "O pacote ficará disponível na lista principal." :
                    "Versão instalada: " + installed + ". Compare com a versão do pacote antes de instalar.";
            };
            content.Controls.Add(packages, 0, 1);
            content.Controls.Add(AppStyle.MakeLabel("Ou cole o link direto HTTPS de um instalador .exe", AppStyle.Text), 0, 2);
            link.BackColor = AppStyle.Surface;
            link.ForeColor = AppStyle.Text;
            content.Controls.Add(link, 0, 3);
            content.Controls.Add(message, 0, 4);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
            Button add = AppStyle.MakeButton("Adicionar à lista", true, 145);
            Button cancel = AppStyle.MakeButton("Cancelar", false, 90);
            cancel.Margin = new Padding(0, 0, 8, 0);
            cancel.DialogResult = DialogResult.Cancel;
            add.Click += delegate
            {
                SelectedOffer = string.IsNullOrWhiteSpace(link.Text)
                    ? (packages.SelectedIndex > 0 ? catalog[packages.SelectedIndex - 1] : null)
                    : ManufacturerPackages.FromOfficialLink(link.Text);
                if (SelectedOffer == null)
                {
                    message.Text = "Escolha um pacote ou use um link aceito de Dell, HP, Lenovo, Intel, AMD ou GIGABYTE.";
                    return;
                }
                DialogResult = DialogResult.OK;
            };
            actions.Controls.Add(add);
            actions.Controls.Add(cancel);
            content.Controls.Add(actions, 0, 5);
            shell.Controls.Add(content, 0, 1);
            Controls.Add(shell);
            AcceptButton = add;
            CancelButton = cancel;
        }
    }
}
