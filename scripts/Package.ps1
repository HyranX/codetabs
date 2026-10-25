param([string]$Version, [string]$ScratchRoot, [switch]$SkipUiTests)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $Version) {
    $assembly = [IO.File]::ReadAllText((Join-Path $projectRoot 'src\AssemblyInfo.cs'))
    $Version = [regex]::Match($assembly,'AssemblyInformationalVersion\("([^"]+)"\)').Groups[1].Value
}
if (-not $ScratchRoot) { $ScratchRoot = $env:CODETABS_SCRATCH_ROOT }
if (-not $ScratchRoot) { $ScratchRoot = $env:RUNNER_TEMP }
if (-not $ScratchRoot) { $ScratchRoot = if (Test-Path 'D:\Cash\Codex') { 'D:\Cash\Codex' } else { Join-Path ([IO.Path]::GetTempPath()) 'CodeTabs' } }
$ScratchRoot = [IO.Path]::GetFullPath($ScratchRoot)
if ($ScratchRoot.TrimEnd('\').Equals($projectRoot,[StringComparison]::OrdinalIgnoreCase) -or $ScratchRoot.StartsWith($projectRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'ScratchRoot must be outside the source directory.' }
$packageName = "CodeTabs-$Version-windows-x64-portable"
if ($Version -notmatch '^[0-9A-Za-z.-]+$') { throw 'Invalid version' }
$scratchRoot = Join-Path $ScratchRoot ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-CodeTabs-package-' + [guid]::NewGuid().ToString('N').Substring(0,6))
$staging = Join-Path $scratchRoot $packageName
$releaseRoot = Join-Path $projectRoot 'releases'
$releaseZip = Join-Path $releaseRoot ($packageName + '.zip')
if (Test-Path -LiteralPath $releaseZip) { throw 'This release already exists. Use a new version to preserve historical packages.' }
New-Item -ItemType Directory -Path $staging,$releaseRoot -Force | Out-Null
$exe = Join-Path $staging 'CodeTabs.exe'
& (Join-Path $projectRoot 'Compile.ps1') -OutputName $exe
if ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -ne $Version) { throw 'Package version does not match executable' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\portable-readme.txt') -Destination (Join-Path $staging 'README.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\release-notes.txt') -Destination (Join-Path $staging 'RELEASE-NOTES.txt')
foreach ($name in @('LICENSE','NOTICE')) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination (Join-Path $staging $name) }
$manifest = @('CodeTabs.exe','README.txt','RELEASE-NOTES.txt','LICENSE','NOTICE') | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath (Join-Path $staging $_) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $_"
}
[IO.File]::WriteAllLines((Join-Path $staging 'SHA256SUMS.txt'),$manifest,[Text.Encoding]::ASCII)
$zip = Join-Path $scratchRoot ($packageName + '.zip')
Compress-Archive -LiteralPath $staging -DestinationPath $zip
$verifyRoot = Join-Path $scratchRoot 'verification'
Expand-Archive -LiteralPath $zip -DestinationPath $verifyRoot
$extracted = Join-Path $verifyRoot $packageName
$expected = @('CodeTabs.exe','README.txt','RELEASE-NOTES.txt','LICENSE','NOTICE','SHA256SUMS.txt')
$actual = @(Get-ChildItem -LiteralPath $extracted -File | Select-Object -ExpandProperty Name)
if (Compare-Object $expected $actual) { throw 'Unexpected package contents' }
if (@(Get-ChildItem -LiteralPath $extracted -Directory -Force).Count -gt 0) { throw 'Unexpected directory in package' }
foreach ($line in $manifest) {
    $hash,$name = $line -split '  ',2
    if ((Get-FileHash -LiteralPath (Join-Path $extracted $name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw "Hash mismatch: $name" }
}
$startInfo = New-Object Diagnostics.ProcessStartInfo
$startInfo.FileName = Join-Path $extracted 'CodeTabs.exe'
$startInfo.Arguments = '--smoke-test'
$startInfo.WorkingDirectory = $extracted
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardError = $true
$testModes = if ($SkipUiTests) { @('--notification-test','--window-layout-test') } else { @('--startup-test','--smoke-test','--notification-test','--window-layout-test','--window-layout-ui-test') }
if ($SkipUiTests) { Write-Output 'CI mode: desktop startup/smoke tests explicitly skipped; notification and window layout tests still run.' }
foreach ($testMode in $testModes) {
    $startInfo.Arguments = if ($testMode -eq '--notification-test') { '--notification-test "' + (Join-Path $scratchRoot 'notification-fixture.jsonl') + '"' } elseif ($testMode -eq '--window-layout-test') { '--window-layout-test "' + (Join-Path $scratchRoot 'window-layout-fixture.json') + '"' } elseif ($testMode -eq '--window-layout-ui-test') { '--window-layout-ui-test "' + $scratchRoot + '"' } else { $testMode }
    $process = [Diagnostics.Process]::Start($startInfo)
    $testTimeout = if ($testMode -eq '--smoke-test') { 90000 } else { 35000 }
    if (-not $process.WaitForExit($testTimeout)) { throw "Extracted executable $testMode timed out" }
    $testError = $process.StandardError.ReadToEnd()
    if ($process.ExitCode -ne 0) { throw "Extracted executable $testMode failed: $testError" }
    $process.Dispose()
}
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Copy-Item -LiteralPath $zip -Destination $releaseZip
$zip = $releaseZip
[IO.File]::WriteAllText(($zip + '.sha256'),"$zipHash  $packageName.zip`r`n",[Text.Encoding]::ASCII)
Write-Output "Package: $zip"
Write-Output "SHA256: $zipHash"
Write-Output ('Extracted package integrity and selected tests passed: ' + ($testModes -join ', '))
New-Item -ItemType Directory -Path (Join-Path $projectRoot 'versions') -Force | Out-Null
Copy-Item -LiteralPath $exe -Destination (Join-Path $projectRoot "versions\CodeTabs-$Version.exe") -Force
try { Copy-Item -LiteralPath $exe -Destination (Join-Path $projectRoot 'CodeTabs.exe') -Force }
catch [IO.IOException] { Write-Output 'The running root CodeTabs.exe was left untouched; the ZIP contains the newly built release.' }
