[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputPath,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^(?:[0-9a-f]{7,40}|local)$')][string]$Commit,
    [switch]$LegacyUpdater
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot)
$source = [IO.File]::ReadAllText((Join-Path $repo 'src/HongxueCompanion.Launcher/Program.cs'))
$source = $source.Replace('"0.0.0.0"', '"'+$Version+'.0"').Replace('"LOCAL"', '"'+$Version+'+'+$Commit+'"')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('hongxue-launcher-'+[guid]::NewGuid().ToString('N')+'.cs')
try {
    [IO.File]::WriteAllText($temporary, $source, [Text.UTF8Encoding]::new($true))
    $defines = @(if ($LegacyUpdater) { '/define:LEGACY_UPDATER' })
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /debug- @defines "/win32icon:$repo/data/icons/aspa_logo.ico" "/out:$OutputPath" $temporary
    if ($LASTEXITCODE -ne 0) { throw '启动入口编译失败' }
}
finally { [IO.File]::Delete($temporary) }
