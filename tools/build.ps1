# Compile files inside this archive only; does not install or start anything.
$ErrorActionPreference = 'Stop'
$archiveRoot = Split-Path -Parent $PSScriptRoot
$extension = Join-Path $archiveRoot 'user-data\extensions\manual_phrase'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw '.NET Framework C# compiler not found.' }
$residentArgs = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', ('/out:' + (Join-Path $extension 'ManualPhrase.Resident.exe')), (Join-Path $extension 'ManualPhrase.Resident.cs'))
& $compiler @residentArgs
if ($LASTEXITCODE -ne 0) { throw 'Resident compilation failed.' }
$launcherArgs = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', ('/out:' + (Join-Path $archiveRoot 'install-dir\WeaselWithPhrase.exe')), (Join-Path $extension 'WeaselWithPhrase.cs'))
& $compiler @launcherArgs
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed.' }
Write-Output 'Compiled the resident and launcher in this archive. Not installed or started. Rebuilt files may differ from the saved SHA256SUMS.txt.'
