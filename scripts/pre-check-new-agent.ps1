param(
    [Parameter(Mandatory=$true)]
    [string]$AgentName,

    [Parameter(Mandatory=$true)]
    [string]$CnName,

    [Parameter(Mandatory=$true)]
    [ValidateSet("P0", "P1")]
    [string]$Priority
)

$ErrorActionPreference = "Continue"
$pass = $true
$gaps = @()

# ===== paths (ASCII only) =====
$projectRoot = Split-Path -Parent $PSScriptRoot

$agentsSrcDir = $null
Get-ChildItem -Path $projectRoot -Directory -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
    if (Test-Path (Join-Path $_.FullName "01_drize-engineer\agent.md")) { $agentsSrcDir = $_.FullName }
}
if (-not $agentsSrcDir) {
    Write-Host "FAIL: cannot find source directory" -ForegroundColor Red
    exit 1
}

$panelFile = Join-Path $projectRoot "AI专岗员工面板.html"
$scanScript = Join-Path $projectRoot "scripts\scan-install-status.ps1"
$installAllScript = Join-Path $projectRoot "scripts\install-all-agents.ps1"
$agentsMdFile = Join-Path $projectRoot "AGENTS.md"

Write-Host "=== Pre-Check: $CnName ($AgentName, $Priority) ==="
Write-Host "Source dir: $agentsSrcDir"
Write-Host ""

# ===== Check 1: hard conflict (name uniqueness) =====
Write-Host "[1/4] Hard conflict: name uniqueness"

$existingDirs = Get-ChildItem -Path $agentsSrcDir -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like "*$AgentName" }

if ($existingDirs.Count -gt 0) {
    Write-Host "  FAIL: dir conflict -> $($existingDirs.FullName)" -ForegroundColor Red
    $pass = $false; $gaps += "1a-dir-conflict"
} else {
    Write-Host "  OK: no dir with '$AgentName'" -ForegroundColor Green
}

$agentMdFiles = Get-ChildItem -Path $agentsSrcDir -Filter "agent.md" -Recurse -ErrorAction SilentlyContinue
foreach ($f in $agentMdFiles) {
    $content = Get-Content $f.FullName -ErrorAction SilentlyContinue -Raw
    if ($content -match "^\s*name:\s*$AgentName\s*$") {
        Write-Host "  FAIL: agent.md name: conflict in $($f.FullName)" -ForegroundColor Red
        $pass = $false; $gaps += "1b-yaml-conflict"
        break
    }
}

if (Test-Path $panelFile) {
    $panelContent = Get-Content $panelFile -Raw -ErrorAction SilentlyContinue
    if ($panelContent -match "en:\s*`"$AgentName`"") {
        Write-Host "  FAIL: '$AgentName' already in panel JS agents[]" -ForegroundColor Red
        $pass = $false; $gaps += "1c-panel-conflict"
    } else {
        Write-Host "  OK: not in panel JS (will be updated later)" -ForegroundColor Green
    }
}

# ===== Check 4: install pipeline coverage =====
Write-Host ""
Write-Host "[2/4] Install pipeline coverage (manual step 4 will fill)"

if (Test-Path $scanScript) {
    $c = Get-Content $scanScript -Raw
    $hit = $c.Contains("`"$AgentName`"")
    $tag = if ($hit) { "WARN already there" } else { "OK not yet (will add)" }
    Write-Host "  scan-install-status.ps1: $tag" -ForegroundColor $(if($hit){"Yellow"}else{"Green"})
}

if (Test-Path $installAllScript) {
    $c = Get-Content $installAllScript -Raw
    $hit = $c.Contains("`"$AgentName`"")
    $tag = if ($hit) { "WARN already there" } else { "OK not yet (will add)" }
    Write-Host "  install-all-agents.ps1: $tag" -ForegroundColor $(if($hit){"Yellow"}else{"Green"})
}

if (Test-Path $agentsMdFile) {
    $c = Get-Content $agentsMdFile -Raw
    $hit = $c -match "\|\s*\d+\s*\|\s*$AgentName\s*\|"
    $tag = if ($hit) { "WARN already there" } else { "OK not yet (will add)" }
    Write-Host "  AGENTS.md table: $tag" -ForegroundColor $(if($hit){"Yellow"}else{"Green"})
}

# ===== Check 5: priority sanity (info only) =====
Write-Host ""
Write-Host "[3/4] Priority sanity (manual check)"
if ($Priority -eq "P0") {
    Write-Host "  INFO: P0 core -> must satisfy: change >=3 lines / spend money / irreversible" -ForegroundColor Cyan
} else {
    Write-Host "  INFO: P1 support -> optional, does not block main flow" -ForegroundColor Cyan
}
Write-Host "  INFO: Next ID = check existing dirs, append to end" -ForegroundColor Cyan

# ===== Check 2/3: manual =====
Write-Host ""
Write-Host "[4/4] Manual checks (fill agent-proposal-template.md)"
Write-Host "  Manual-2: Soft conflict -> compare core capability vs AGENTS.md table" -ForegroundColor Cyan
Write-Host "  Manual-3: Skill support -> you MUST write SKILL.md with agent.md" -ForegroundColor Cyan

# ===== Summary =====
Write-Host ""
Write-Host "============================================="
if ($pass) {
    Write-Host "RESULT: PASS (automated checks OK)" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:"
    Write-Host "  1. Fill agent-proposal-template.md (soft conflict + skill support)"
    Write-Host "  2. Write agent.md + SKILL.md + README.md in 02_AGENTS/<next_id>_$AgentName\"
    Write-Host "  3. Update panel JS, scan, install-all scripts, AGENTS.md table"
    Write-Host "  4. Run scan-install-status.ps1 -> new agent in JSON"
} else {
    Write-Host "RESULT: FAIL" -ForegroundColor Red
    Write-Host "Gaps: $($gaps -join ', ')" -ForegroundColor Red
    Write-Host "Fix gaps then re-run"
}
Write-Host "============================================="

if (-not $pass) { exit 1 }
