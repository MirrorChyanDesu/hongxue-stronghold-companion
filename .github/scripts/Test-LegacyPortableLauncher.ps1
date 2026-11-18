[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=Join-Path ([IO.Path]::GetTempPath()) ('hongxue-legacy-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$processes=[Collections.Generic.List[Diagnostics.Process]]::new()
$jobs=[Collections.Generic.List[string]]::new()
try {
    & (Join-Path $PSScriptRoot 'Build-PortableLauncher.ps1') -OutputPath (Join-Path $root 'ASPAssistant.App.exe') -Version '1.9.4' -Commit local -LegacyUpdater
    $probe=Join-Path $root 'probe.cs'
    @'
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
class Probe {
    static int Main(string[] args) {
        var root=AppDomain.CurrentDomain.BaseDirectory;
        var lines=new string[args.Length+3];
        lines[0]=Environment.CurrentDirectory;
        lines[1]=Environment.GetEnvironmentVariable("HONGXUE_LAUNCHER_TEST");
        lines[2]=Process.GetCurrentProcess().Id.ToString();
        Array.Copy(args,0,lines,3,args.Length);
        File.WriteAllLines(Path.Combine(root,"result.tmp"),lines,Encoding.UTF8);
        File.Move(Path.Combine(root,"result.tmp"),Path.Combine(root,"result.txt"));
        var mode=File.ReadAllText(Path.Combine(root,"mode.txt"));
        if(mode=="exit") return 17;
        var marker=Environment.GetEnvironmentVariable("ASPA_UPDATE_STARTUP_RESULT");
        File.WriteAllText(marker+".tmp",mode=="failed" ? "failed\nfixture startup failure" : "ready",Encoding.UTF8);
        File.Move(marker+".tmp",marker);
        if(mode=="failed") return 18;
        var clock=Stopwatch.StartNew();
        while(!File.Exists(Path.Combine(root,"release")) && clock.Elapsed.TotalSeconds<15)
            System.Threading.Thread.Sleep(10);
        return 23;
    }
}
'@ | Set-Content -LiteralPath $probe -Encoding utf8BOM
    & (Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe') /nologo /target:winexe /platform:x64 "/out:$root/probe.exe" $probe
    if($LASTEXITCODE){throw 'Legacy fixture compile failed'}
    $arguments=@('', '普通参数', 'with space', 'C:\trailing slash\', 'a"b', 'a\"b', 'a\\"b', '& $ [x]')
    $results=@(foreach($mode in @('ready','failed','exit','missing','invalid','bad-marker')) {
        $directory=Join-Path $root "中文 空格 & [目录] O'Neil `$旧入口 $mode"
        $app=Join-Path $directory 'app'
        New-Item -ItemType Directory -Path $app | Out-Null
        Copy-Item -LiteralPath (Join-Path $root 'ASPAssistant.App.exe') -Destination $directory
        if($mode -eq 'invalid') {[IO.File]::WriteAllText((Join-Path $app 'HongxueCompanion.exe'),'not an executable')}
        elseif($mode -notin @('missing','bad-marker')) {Copy-Item -LiteralPath (Join-Path $root 'probe.exe') -Destination (Join-Path $app 'HongxueCompanion.exe')}
        [IO.File]::WriteAllText((Join-Path $app 'mode.txt'),$mode)
        $job=Join-Path ([IO.Path]::GetTempPath()) ('ASPAssistant-Update-'+[guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $job | Out-Null
        $jobs.Add($job)
        $marker=Join-Path $job ('startup-'+[guid]::NewGuid().ToString('N')+'.status')
        if($mode -eq 'bad-marker'){$marker=Join-Path $directory 'not-an-update-marker.status'}
        $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $directory 'ASPAssistant.App.exe'))
        $start.UseShellExecute=$false
        $start.WorkingDirectory=$root
        $start.Environment['ASPA_UPDATE_STARTUP_RESULT']=$marker
        $start.Environment['HONGXUE_LAUNCHER_TEST']='inherited 继承'
        foreach($argument in $arguments){$start.ArgumentList.Add($argument)}
        $clock=[Diagnostics.Stopwatch]::StartNew()
        $process=[Diagnostics.Process]::Start($start)
        $processes.Add($process)
        $metrics=@{}
        if($mode -eq 'ready') {
            while(-not (Test-Path -LiteralPath $marker) -and $clock.Elapsed.TotalSeconds -lt 10){Start-Sleep -Milliseconds 10}
            if([IO.File]::ReadAllText($marker) -cne 'ready' -or $process.HasExited){throw 'Compatibility entry exited before the main process was ready'}
            $lines=[IO.File]::ReadAllLines((Join-Path $app 'result.txt'))
            if($lines.Length -ne $arguments.Count+3 -or $lines[0] -cne $app -or $lines[1] -cne 'inherited 继承'){throw 'Legacy working directory or environment differs'}
            for($index=0;$index -lt $arguments.Count;$index++){if($lines[$index+3] -cne $arguments[$index]){throw "Legacy argument $index differs"}}
            $child=[Diagnostics.Process]::GetProcessById([int]$lines[2]); $processes.Add($child)
            $process.Refresh()
            $metrics=@{ReadyMs=$clock.Elapsed.TotalMilliseconds;PrivateBytes=$process.PrivateMemorySize64;WorkingSetBytes=$process.WorkingSet64}
            $cpu=$process.TotalProcessorTime.TotalMilliseconds
            Start-Sleep -Milliseconds 1000
            $process.Refresh()
            $metrics.IdleCpuMs=$process.TotalProcessorTime.TotalMilliseconds-$cpu
            if($process.HasExited -or $child.HasExited){throw 'Legacy entry did not wait for the main process'}
            [IO.File]::WriteAllText((Join-Path $app 'release'),'release')
        }
        if(-not $process.WaitForExit(10000)){throw "Legacy entry did not exit for $mode"}
        $expected=switch($mode){ready{23} failed{18} exit{17} default{1}}
        if($process.ExitCode -ne $expected){throw "Legacy exit $($process.ExitCode), expected $expected for $mode"}
        if($mode -in @('failed','missing','invalid') -and -not [IO.File]::ReadAllText($marker).StartsWith("failed`n")){throw 'Legacy startup failure was not reported to the updater'}
        if($mode -eq 'exit' -and (Test-Path -LiteralPath $marker)){throw 'Early exit was incorrectly reported as ready'}
        if($mode -eq 'bad-marker' -and ((Test-Path -LiteralPath $marker) -or (Test-Path -LiteralPath ($marker+'.tmp')))){throw 'Legacy entry wrote outside the updater marker boundary'}
        [pscustomobject]@{Mode=$mode;ExitCode=$process.ExitCode;Arguments=$arguments.Count;Metrics=$metrics}
    })
    $results
}
finally {
    foreach($process in $processes){try{if(-not $process.HasExited){$process.Kill($true)};$process.WaitForExit(10000) | Out-Null}finally{$process.Dispose()}}
    foreach($job in $jobs){
        $resolved=(Resolve-Path -LiteralPath $job).Path
        if([IO.Path]::GetDirectoryName($resolved) -ne [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetTempPath()) -or
            [IO.Path]::GetFileName($resolved) -notlike 'ASPAssistant-Update-*'){throw 'Refusing cleanup outside update fixture directory'}
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    $resolved=(Resolve-Path -LiteralPath $root).Path
    if([IO.Path]::GetDirectoryName($resolved) -ne [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetTempPath()) -or
        [IO.Path]::GetFileName($resolved) -notlike 'hongxue-legacy-test-*'){throw 'Refusing cleanup outside legacy fixture directory'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
