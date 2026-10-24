param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$files = @(git -C $projectRoot -c core.quotePath=false ls-files --cached)
if ($LASTEXITCODE -ne 0 -or $files.Count -eq 0) { throw 'No tracked/staged files to audit. Stage the intended public files first.' }
$patterns = @{
    'private key' = '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
    'GitHub token' = '(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})'
    'API token' = '\bsk-(?:proj-)?[A-Za-z0-9_-]{30,}'
    'AWS access key' = '\b(?:AKIA|ASIA)[A-Z0-9]{16}\b'
    'personal home path' = '(?i)[A-Z]:[\\/]Users[\\/][a-z0-9_.-]+[\\/]'
}
$findings = @()
foreach ($file in $files) {
    if ($file -match '^(config|\.codex|\.agents|\.local|versions|releases)/' -or $file -match '\.(exe|dll|zip|pdb|pem|key|pfx|p12|bak|log)$' -or $file -match '(^|/)\.env($|\.)') {
        $findings += "Forbidden public path: $file"
        continue
    }
    # Read the staged blob: unstaged edits must not conceal a staged secret.
    if ($file -match '\.(ico|png)$') { continue }
    $body = (git -C $projectRoot show ":$file") -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "Cannot inspect staged file: $file" }
    foreach ($rule in $patterns.GetEnumerator()) {
        if ($body -match $rule.Value) { $findings += "$($rule.Key) in $file" }
    }
}
if ($findings.Count -gt 0) { throw ($findings -join "`n") }
Write-Output "Public-file audit passed for $($files.Count) staged/tracked files. Pattern checks are not a guarantee that every secret is detected."
