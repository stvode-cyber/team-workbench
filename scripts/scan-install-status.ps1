$ErrorActionPreference = "Stop"

$targetRoot = "$env:USERPROFILE\.trae-cn"
$agentsDir  = Join-Path $targetRoot "agents"
$skillsDir  = Join-Path $targetRoot "skills"

$projectRoot = Split-Path -Parent $PSScriptRoot

$agentsSrcDir = $null
Get-ChildItem -Path $projectRoot -Directory -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
    if (Test-Path (Join-Path $_.FullName "01_drize-engineer\agent.md")) { $agentsSrcDir = $_.FullName }
}
if (-not $agentsSrcDir) { $agentsSrcDir = "(auto-detect failed)" }
$outFile = Join-Path $projectRoot "agent-install-status.json"

# v3.0 backend (optional - skip if server not running)
$backendUrl = "http://localhost:3001"

$agentNames = @(
    "drize-engineer",
    "arch-designer",
    "project-taker",
    "ledger-keeper",
    "rca-analyst",
    "competitor-surveyor",
    "backend-expert",
    "safety-auditor",
    "report-writer",
    "agents-builder",
    "mcp-finder"
)

$result = @{}

foreach ($name in $agentNames) {
    $dstAgentMd = Join-Path $agentsDir "$name.md"
    $dstSkillMd = Join-Path $skillsDir "$name\SKILL.md"

    $agentMdExists = Test-Path $dstAgentMd
    $skillMdExists = Test-Path $dstSkillMd

    if ($agentMdExists -and $skillMdExists) {
        $status = "installed"
    }
    elseif ($agentMdExists -or $skillMdExists) {
        $status = "partial"
    }
    else {
        $status = "not-installed"
    }

    $installTime = $null
    if ($agentMdExists) {
        $installTime = (Get-Item $dstAgentMd).LastWriteTime.ToString("yyyy-MM-dd")
    }

    $result[$name] = @{
        status         = $status
        agentMdExists  = $agentMdExists
        skillMdExists  = $skillMdExists
        installDate    = $installTime
        targetPath     = $dstAgentMd
    }

    # v3.0: push to backend DB (best-effort, skip silently if server down)
    try {
        $body = @{ install_status = $status; install_date = $installTime } | ConvertTo-Json
        Invoke-RestMethod -Uri "$backendUrl/api/agents/$name/scan" `
            -Method Post -ContentType "application/json" -Body $body `
            -UseBasicParsing -TimeoutSec 3 | Out-Null
    } catch {
        # v2.0 JSON fallback still works without backend
    }
}

$summary = @{
    scannedAt    = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    agentsDir    = $agentsDir
    skillsDir    = $skillsDir
    installed    = @($result.GetEnumerator() | Where-Object { $_.Value.status -eq "installed" }).Count
    partial      = @($result.GetEnumerator() | Where-Object { $_.Value.status -eq "partial" }).Count
    missing      = @($result.GetEnumerator() | Where-Object { $_.Value.status -eq "not-installed" }).Count
}

$output = @{ summary = $summary; agents = $result } | ConvertTo-Json -Depth 4
Set-Content -Path $outFile -Value $output -Encoding UTF8

Write-Host "Scan done. Output: $outFile"
Write-Host "Installed: $($summary.installed) | Partial: $($summary.partial) | Missing: $($summary.missing)"
