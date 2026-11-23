param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'AtualizadorDeDrivers.exe')
)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Compilador do .NET Framework 4.x indisponivel.'
}

$files = @('Program.cs', 'MainWindow.cs', 'InstallWindow.cs', 'AppStyle.cs', 'DashboardVisuals.cs',
    'SpeedometerButton.cs', 'DriverServices.cs', 'DriverProperties.cs', 'DriverCategoryIcons.cs',
    'ThemeToggle.cs', 'AllDriversCurrent.cs', 'VideoDriversWindow.cs') |
    ForEach-Object { Join-Path $PSScriptRoot $_ }
$resolvedOutput = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$manifest = Join-Path $PSScriptRoot 'app.manifest'
$icon = Join-Path $PSScriptRoot 'app.ico'

& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /warnaserror+ `
    '/reference:System.Windows.Forms.dll,System.Drawing.dll,System.Management.dll,Microsoft.CSharp.dll' `
    "/win32manifest:$manifest" "/win32icon:$icon" "/out:$resolvedOutput" $files
if ($LASTEXITCODE -ne 0) {
    throw "A compilacao retornou o codigo $LASTEXITCODE."
}
$resolvedOutput
