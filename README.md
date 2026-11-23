# Atualizador de drivers

Aplicativo portátil para Windows 10 e 11 com .NET Framework 4.8. Abra `AtualizadorDeDrivers.exe` para listar os drivers dos dispositivos presentes e consultar o Windows Update.

1. Veja as versões, datas e fornecedores na aba **Drivers instalados**.
2. Em **Atualizações disponíveis**, clique em **Buscar atualizações**. A busca usa a fonte definida nas configurações do Windows Update, inclusive em computadores gerenciados por uma organização.
3. Marque os pacotes que deseja aplicar e clique em **Instalar selecionados**. Confirme a operação e a solicitação de administrador do Windows. A janela de instalação apresenta o resultado de cada pacote e avisa quando for necessário reiniciar.

A lista mostra as ofertas compatíveis que o Windows Update disponibiliza para o computador. A seleção do Windows considera a compatibilidade e a classificação do driver, além da versão e da data. Isso significa que o site de um fabricante pode oferecer outras versões.

O inventário é lido no próprio computador. O aplicativo não tem conta, servidor próprio ou telemetria. Ao buscar ou instalar atualizações, o próprio Windows se comunica com a fonte de atualizações configurada para o dispositivo.

## Compilação

Extraia os arquivos para a mesma pasta e execute `Build.ps1` com o Windows PowerShell. O script usa o compilador do .NET Framework e gera `AtualizadorDeDrivers.exe` na pasta do código por padrão. Use `-OutputPath` para definir outro destino.

## Referências

- [API do Windows Update Agent](https://learn.microsoft.com/en-us/windows/desktop/Wua_Sdk/portal-client)
- [Como o Windows seleciona um driver para um dispositivo](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/how-windows-selects-a-driver-for-a-device)
