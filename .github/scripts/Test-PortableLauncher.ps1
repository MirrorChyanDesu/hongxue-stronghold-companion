[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Join-Path ([IO.Path]::GetTempPath()) ('hongxue-launcher-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $package = Join-Path $root "中文 空格 & [目录] O'Neil `$入口"
    $app = Join-Path $package 'app'
    New-Item -ItemType Directory -Path $app | Out-Null
    $probe = Join-Path $root 'probe.cs'
    @'
using System;
using System.IO;
using System.Text;
class Probe {
    static int Main(string[] args) {
        var lines = new string[args.Length + 2];
        lines[0] = Environment.CurrentDirectory;
        lines[1] = Environment.GetEnvironmentVariable("HONGXUE_LAUNCHER_TEST");
        Array.Copy(args, 0, lines, 2, args.Length);
        File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "result.txt"), lines, Encoding.UTF8);
        return 0;
    }
}
'@ | Set-Content -LiteralPath $probe -Encoding utf8BOM
    & (Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe') /nologo /target:winexe /platform:x64 "/out:$app/HongxueCompanion.exe" $probe
    if ($LASTEXITCODE -ne 0) { throw '测试主程序编译失败' }
    & (Join-Path $PSScriptRoot 'Build-PortableLauncher.ps1') -OutputPath (Join-Path $package 'HongxueCompanion.exe') -Version '1.9.4' -Commit local
    $arguments = @('', '普通参数', 'with space', 'C:\trailing slash\', 'a"b', 'a\"b', 'a\\"b', '& $ [x]')
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $package 'HongxueCompanion.exe'))
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.Environment['HONGXUE_LAUNCHER_TEST'] = 'inherited 继承'
    foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(10000)) { $process.Kill(); throw '根目录入口没有退出' }
        if ($process.ExitCode -ne 0) { throw '根目录入口启动失败' }
        $launcherMs = $clock.Elapsed.TotalMilliseconds
        $result = Join-Path $app 'result.txt'
        while (-not (Test-Path -LiteralPath $result) -and $clock.Elapsed.TotalSeconds -lt 10) { Start-Sleep -Milliseconds 10 }
        $lines = [IO.File]::ReadAllLines($result)
        if ($lines[0] -cne $app -or $lines[1] -cne 'inherited 继承') { throw '启动目录或环境未正确传递' }
        if ($lines.Length -ne $arguments.Count + 2) { throw '参数数量改变' }
        for ($index=0; $index -lt $arguments.Count; $index++) {
            if ($lines[$index+2] -cne $arguments[$index]) { throw "第 $index 个参数改变" }
        }
        $version = (Get-Item -LiteralPath $start.FileName).VersionInfo
        if ($version.FileVersion -ne '1.9.4.0' -or $version.ProductVersion -ne '1.9.4+local') { throw '入口版本信息错误' }
        [pscustomobject]@{ Status='Passed'; Arguments=$arguments.Count; LauncherExitMs=[math]::Round($launcherMs,2); ProbeReadyMs=[math]::Round($clock.Elapsed.TotalMilliseconds,2) }
    }
    finally { $process.Dispose() }
}
finally {
    $resolved = (Resolve-Path -LiteralPath $root).Path
    if ([IO.Path]::GetDirectoryName($resolved) -ne [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetTempPath()) -or
        [IO.Path]::GetFileName($resolved) -notlike 'hongxue-launcher-test-*') { throw '拒绝清理测试目录之外的路径' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
