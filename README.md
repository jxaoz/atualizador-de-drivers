# Drivers License

Aplicativo para Windows 10 e 11, disponível com instalador ou em versão portátil. A tela **Drivers do PC** reúne todos os dispositivos presentes que o Windows detecta, a versão instalada e as ofertas do Windows Update e dos fabricantes.

[Baixar o instalador ou o ZIP portátil para Windows](https://github.com/jxaoz/atualizador-de-drivers/releases/latest)

## Como usar

1. Execute o arquivo que termina em `-Setup.exe` ou extraia o ZIP portátil e abra `DriversLicense.exe`. O aplicativo usa o .NET Framework 4.8 ou superior.
2. Ao abrir, o app carrega o inventário e começa a busca. Use o velocímetro **Buscar atualizações** para consultar novamente. O app cruza os IDs dos dispositivos com as ofertas do Windows Update. Para dispositivos sem uma oferta correspondente, consulta os catálogos oficiais integrados. A coluna **Origem** identifica a fonte de cada atualização. Os dispositivos sem oferta continuam na lista com a situação da busca.
3. Marque os pacotes desejados e clique em **Instalar selecionados**. Para o NVIDIA GeForce Game Ready, o Drivers License pede o aceite da licença, baixa o driver e valida a assinatura digital da NVIDIA antes de abrir o instalador oficial. Conclua a instalação na janela da NVIDIA e busque novamente para conferir a versão.
4. Clique com o botão direito e escolha **Propriedades** para ver o driver instalado ou localizar o suporte do fabricante. A aba **Dispositivos** oferece um filtro do inventário por nome, fornecedor ou categoria.
5. Em **Drivers de vídeo**, veja a versão instalada ou filtre a busca para vídeo. Para placas NVIDIA, AMD e Intel, **Downloads oficiais** abre a página do fabricante.
6. Em **Pacotes de fabricantes**, veja outros pacotes reconhecidos para o hardware detectado. Selecione um para adicioná-lo à lista principal. Você também pode colar o link direto de um instalador `.exe` da Dell, HP, Lenovo, Intel, AMD ou GIGABYTE em um dos domínios oficiais aceitos. Selecione o pacote individualmente para baixar, validar a assinatura e abrir o instalador. Para links adicionados manualmente, confirme o modelo e a versão do Windows antes de iniciar.

**Sem atualização encontrada** significa que as fontes consultadas não retornaram uma oferta confirmada para aquele dispositivo. **Verificação incompleta** sinaliza uma falha na consulta. A versão **Sem driver** identifica os dispositivos que o Windows informa estarem sem um driver instalado. [Orientação da Microsoft sobre drivers de fabricantes](https://support.microsoft.com/en-us/windows/hardware/drivers/update-drivers-through-device-manager-in-windows)

Abra **Detalhes da atualização** no menu de contexto para conferir a previsão de reinício. Após instalar, o aplicativo usa o resultado do Windows Update ou o estado de reinício do Windows. Siga também a orientação do instalador oficial. O interruptor **Modo noturno**, no topo, salva o tema escolhido para a próxima abertura.

![Exemplo ilustrativo de drivers com ícones de vídeo, áudio, rede, USB e outras categorias](docs/categorias.png)

O inventário é lido localmente. As ofertas variam conforme o hardware, a versão do Windows e as políticas locais. A busca direta da NVIDIA cobre modelos GeForce compatíveis em Windows 10 ou 11 x64. Quando **AI Agents and App Connections** está habilitado no NVIDIA App, o Drivers License lê a recomendação Game Ready e verifica se o catálogo público oferece o pacote para o modelo detectado. A busca no catálogo também funciona com a integração desativada. [Catálogo de drivers da NVIDIA](https://www.nvidia.com/en-us/geforce/drivers/) · [Integração do NVIDIA App](https://www.nvidia.com/en-us/software/nvidia-app/release-highlights/)

## Cobertura dos fabricantes

- **Dell:** o catálogo oficial fornece pacotes para os modelos e sistemas presentes no índice Dell. O aplicativo confere a assinatura do catálogo e cruza os IDs PCI publicados com os dispositivos presentes. [Catálogos Dell](https://www.dell.com/support/kbdoc/pt-pt/000453493/cad%C3%AAncias-do-cat%C3%A1logo-de-atualiza%C3%A7%C3%A3o-dos-pcs-comerciais-de-cliente-dell)
- **HP:** os SoftPaqs são consultados pelo ID de plataforma e pela compilação do Windows. O app usa as versões e os IDs de dispositivo publicados pelo catálogo HP. [Soluções para PCs HP](https://www.hp.com/us-en/solutions/client-management-solutions.html)
- **Lenovo:** o catálogo de cada tipo de máquina e versão do Windows é consultado para drivers com dados verificáveis de hardware e compatibilidade. [Lenovo System Update](https://download.lenovo.com/pccbbs/mobiles_pdf/tvsu5_mst_en.pdf)
- **AMD e Intel:** pacotes oficiais reconhecidos de chipset Ryzen e Wi-Fi Intel, respectivamente. Para Wi-Fi, uma oferta do catálogo do fabricante do computador tem prioridade sobre o pacote genérico Intel. [Chipset AMD](https://www.amd.com/en/resources/support-articles/release-notes/RN-RYZEN-CHIPSET-8-09-28-1124.html) · [Wi-Fi Intel](https://downloadmirror.intel.com/929786/ReleaseNotes_WiFi_24.80.0.pdf)
- **GIGABYTE:** para a A520M K V2, revisões 1.0, 1.1 e 1.2, estão disponíveis áudio Realtek e chipset AMD no Windows 10 e 11 x64, além de rede Realtek no Windows 11 x64. [Revisão 1.0](https://www.gigabyte.com/us/Motherboard/A520M-K-V2-rev-10/support) · [Revisão 1.1](https://www.gigabyte.com/Motherboard/A520M-K-V2-rev-11/support) · [Revisão 1.2](https://www.gigabyte.com/Motherboard/A520M-K-V2-rev-12/support)

A busca usa as ofertas aplicáveis retornadas pelo Windows Update e compara a data do driver quando a Microsoft a fornece. Nos catálogos de fabricantes, o app busca pacotes compatíveis para dispositivos sem driver ou compara a versão com a instalada. Pacotes de chipset podem atender ao conjunto de componentes da placa e aparecem como uma oferta separada. A disponibilidade varia conforme o modelo, a versão do Windows e os metadados publicados. O app verifica o SHA-256 dos pacotes de catálogo e a assinatura digital do instalador antes de abri-lo. [Propriedades das ofertas do Windows Update](https://learn.microsoft.com/en-us/windows/win32/wua_sdk/iwindowsdriverupdateentry-properties)

Em **Dispositivos → Propriedades**, o atalho de suporte também está disponível para ASUS e MSI.

O instalador e o aplicativo estão sem assinatura digital. O Windows pode exibir um aviso ou bloquear a abertura de acordo com a configuração do computador. [Microsoft SmartScreen](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation)

## Compilar

Execute `Build.ps1` com o Windows PowerShell em um computador com .NET Framework 4.8. O script gera `DriversLicense.exe` na mesma pasta. Use `-OutputPath` para escolher outro destino.

Para gerar o instalador, execute `BuildInstaller.ps1` após compilar o aplicativo. Use `-CompilerPath` para informar onde está o `ISCC.exe` do Inno Setup 6.6 ou superior, caso o script não o encontre. [Inno Setup](https://jrsoftware.org/inno-setup/downloads)
