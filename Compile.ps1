param([string]$OutputName = 'CodeTabs.exe')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outputPath = if ([IO.Path]::IsPathRooted($OutputName)) { $OutputName } else { Join-Path $PSScriptRoot $OutputName }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$outputPath" "/win32manifest:$PSScriptRoot\src\app.manifest" "/win32icon:$PSScriptRoot\assets\CodeTabs.ico" "/resource:$PSScriptRoot\assets\CodeTabs.ico,CodeTabs.AppIcon" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "$PSScriptRoot\src\AssemblyInfo.cs" "$PSScriptRoot\src\CodeTabs.cs" "$PSScriptRoot\src\WindowHost.cs" "$PSScriptRoot\src\WindowRestore.cs" "$PSScriptRoot\src\CodexState.cs" "$PSScriptRoot\src\BindingStore.cs" "$PSScriptRoot\src\Overview.cs" "$PSScriptRoot\src\LocalProjects.cs" "$PSScriptRoot\src\PortableConfig.cs" "$PSScriptRoot\src\CompletionNotifications.cs" "$PSScriptRoot\src\ConversationNavigation.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
if ($OutputName -eq 'CodeTabs.exe') {
    $version = (Get-Item -LiteralPath $outputPath).VersionInfo.ProductVersion
    $backup = Join-Path $PSScriptRoot "versions\CodeTabs-$version.exe"
    New-Item -ItemType Directory -Path (Split-Path $backup -Parent) -Force | Out-Null
    if (-not (Test-Path -LiteralPath $backup)) { Copy-Item -LiteralPath $outputPath -Destination $backup }
}
