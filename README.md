# Atualizador de Drivers

Aplicativo portátil para Windows 10 e 11. O Atualizador lê os drivers do computador em que é executado e consulta o Windows Update para oferecer atualizações compatíveis, inclusive opcionais.

[Baixar a versão para Windows](https://github.com/jxaoz/atualizador-de-drivers/releases/latest)

![Tela inicial com o botão de busca em forma de velocímetro](docs/velocimetro.png)

## Como usar

1. Extraia o ZIP da release e abra `AtualizadorDeDrivers.exe`. O aplicativo usa o .NET Framework 4.8 ou superior.
2. Clique no velocímetro **Buscar atualizações**. O ponteiro se move enquanto o Windows Update responde.
3. Marque os pacotes desejados e clique em **Instalar selecionados**. O Windows solicita permissão de administrador. Ao final, confira o resultado de cada atualização e reinicie se o Windows solicitar.
4. Em **Drivers instalados**, identifique as categorias pelos ícones. Clique com o botão direito em um driver e escolha **Propriedades**. Para ver o painel nativo do dispositivo, clique em **Abrir no Windows**.

![Exemplo ilustrativo de drivers com ícones de vídeo, áudio, rede, USB e outras categorias](docs/categorias.png)

O inventário é lido localmente. Para busca e instalação, o Windows se comunica com a fonte de atualizações definida para aquele computador. As ofertas disponíveis variam conforme o hardware, a versão do Windows e as políticas locais.

O executável desta versão está sem assinatura digital. O Windows pode exibir um aviso ou bloquear a abertura de acordo com a configuração do computador. [Microsoft SmartScreen](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation)

## Compilar

Execute `Build.ps1` com o Windows PowerShell em um computador com .NET Framework 4.8. O script gera `AtualizadorDeDrivers.exe` na mesma pasta. Use `-OutputPath` para escolher outro destino.
