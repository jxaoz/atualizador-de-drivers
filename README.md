# Drivers License

Aplicativo para Windows 10 e 11, disponível com instalador ou em versão portátil. O Drivers License consulta o Windows Update e o catálogo da NVIDIA para listar atualizações compatíveis na mesma tela.

[Baixar o instalador ou o ZIP portátil para Windows](https://github.com/jxaoz/atualizador-de-drivers/releases/latest)

![Exemplo ilustrativo das ofertas do Windows Update e da NVIDIA na mesma lista](docs/velocimetro.png)

## Como usar

1. Execute o arquivo que termina em `-Setup.exe` ou extraia o ZIP portátil e abra `DriversLicense.exe`. O aplicativo usa o .NET Framework 4.8 ou superior.
2. Clique no velocímetro **Buscar atualizações**. A coluna **Origem** identifica as ofertas do Windows Update, do catálogo oficial da NVIDIA e as recomendações verificadas do NVIDIA App.
3. Marque os pacotes desejados e clique em **Instalar selecionados**. Para o NVIDIA GeForce Game Ready, o Drivers License pede o aceite da licença, baixa o driver e valida a assinatura digital da NVIDIA antes de abrir o instalador oficial. Conclua a instalação na janela da NVIDIA e busque novamente para conferir a versão.
4. Em **Dispositivos**, veja os componentes presentes que o Windows detectou, incluindo itens **Sem driver**. Clique com o botão direito e escolha **Propriedades** para abrir o dispositivo no Windows ou localizar o suporte do fabricante.
5. Em **Drivers de vídeo**, veja a versão instalada ou filtre a busca para vídeo. Para placas NVIDIA, AMD e Intel, **Downloads oficiais** abre a página do fabricante.

Quando uma busca completa termina sem ofertas e sem dispositivos identificados como sem driver, a área de resultados exibe **Todos os drivers estão em dia!**. A mensagem se refere às fontes consultadas. Alguns fabricantes distribuem pacotes apenas no próprio site. [Orientação da Microsoft sobre drivers de fabricantes](https://support.microsoft.com/en-us/windows/hardware/drivers/update-drivers-through-device-manager-in-windows)

Abra **Propriedades** de uma oferta para conferir a previsão de reinício. Após instalar, o aplicativo usa o resultado do Windows Update para indicar se precisa reiniciar. Para o pacote NVIDIA, siga a orientação do instalador oficial. O interruptor **Modo noturno**, no topo, salva o tema escolhido para a próxima abertura.

![Exemplo ilustrativo de drivers com ícones de vídeo, áudio, rede, USB e outras categorias](docs/categorias.png)

O inventário é lido localmente. As ofertas variam conforme o hardware, a versão do Windows e as políticas locais. A busca direta da NVIDIA cobre modelos GeForce compatíveis em Windows 10 ou 11 x64. Quando **AI Agents and App Connections** está habilitado no NVIDIA App, o Drivers License lê a recomendação Game Ready e verifica se o catálogo público oferece o pacote para o modelo detectado. A busca no catálogo também funciona com a integração desativada. [Catálogo de drivers da NVIDIA](https://www.nvidia.com/en-us/geforce/drivers/) · [Integração do NVIDIA App](https://www.nvidia.com/en-us/software/nvidia-app/release-highlights/)

O instalador e o aplicativo estão sem assinatura digital. O Windows pode exibir um aviso ou bloquear a abertura de acordo com a configuração do computador. [Microsoft SmartScreen](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation)

## Compilar

Execute `Build.ps1` com o Windows PowerShell em um computador com .NET Framework 4.8. O script gera `DriversLicense.exe` na mesma pasta. Use `-OutputPath` para escolher outro destino.

Para gerar o instalador, execute `BuildInstaller.ps1` após compilar o aplicativo. Use `-CompilerPath` para informar onde está o `ISCC.exe` do Inno Setup 6.6 ou superior, caso o script não o encontre. [Inno Setup](https://jrsoftware.org/inno-setup/downloads)
