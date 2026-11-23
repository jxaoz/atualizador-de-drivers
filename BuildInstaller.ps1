param(
    [string]$InputPath = (Join-Path $PSScriptRoot 'DriversLicense.exe'),
    [string]$OutputDirectory = $PSScriptRoot,
    [string]$CompilerPath
)

$ErrorActionPreference = 'Stop'

if (-not $CompilerPath) {
    $command = Get-Command ISCC.exe -CommandType Application -ErrorAction SilentlyContinue
    if ($command) {
        $CompilerPath = $command.Source
    } else {
        $programDirs = @($env:ProgramFiles, [Environment]::GetEnvironmentVariable('ProgramFiles(x86)'),
            (Join-Path $env:LocalAppData 'Programs')) | Where-Object { $_ }
        $CompilerPath = $programDirs | ForEach-Object { Join-Path $_ 'Inno Setup 6\ISCC.exe' } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Inno Setup 6.6 ou superior indisponivel. Informe o caminho de ISCC.exe com -CompilerPath.'
}

$compiler = (Resolve-Path -LiteralPath $CompilerPath).ProviderPath
$program = (Resolve-Path -LiteralPath $InputPath).ProviderPath
$version = (Get-Item -LiteralPath $program).VersionInfo.ProductVersion
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'O executavel deve ter uma versao no formato MAJOR.MINOR.PATCH.'
}

$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
$script = Join-Path $PSScriptRoot 'Installer.iss'
& $compiler "/DAppBinary=$program" "/DAppVersion=$version" "/O$output" $script | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "O Inno Setup retornou o codigo $LASTEXITCODE."
}
$installer = Join-Path $output "Drivers-License-$version-Setup.exe"
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw 'O instalador compilado nao foi encontrado.'
}
$installer
