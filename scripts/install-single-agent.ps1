param(
    [Parameter(Mandatory=$true)]
    [string]$AgentName
)

$ErrorActionPreference = "Stop"

# ===== 统一路径变量（ASCII only，避免 PS5 UTF-8 BOM 坑）=====
$targetRoot = "$env:USERPROFILE\.trae-cn"
$agentsDir  = Join-Path $targetRoot "agents"
$skillsDir  = Join-Path $targetRoot "skills"

$projectRoot = Split-Path -Parent $PSScriptRoot

# 自动搜含 agent.md 的目录（不硬编码中文路径）
$agentsSrcDir = $null
Get-ChildItem -Path $projectRoot -Directory -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
    $hasAgent = Test-Path (Join-Path $_.FullName "01_drize-engineer\agent.md")
    if ($hasAgent) { $agentsSrcDir = $_.FullName }
}

if (-not $agentsSrcDir) {
    Write-Host "FAIL: cannot find source directory with agent.md files"
    exit 1
}

Write-Host "AgentName=$AgentName"
Write-Host "agentsSrcDir=$agentsSrcDir"

# 源文件：在 agentsSrcDir 下搜 $AgentName 目录
$agentSub = Get-ChildItem -Path $agentsSrcDir -Directory -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like "*$AgentName" } |
    Select-Object -First 1

if (-not $agentSub) {
    Write-Host "FAIL: source dir not found for $AgentName in $agentsSrcDir"
    exit 1
}

$srcAgentMd = Join-Path $agentSub.FullName "agent.md"
$srcSkillMd = Join-Path $agentSub.FullName "SKILL.md"

if (-not (Test-Path $srcAgentMd) -or -not (Test-Path $srcSkillMd)) {
    Write-Host "FAIL: missing agent.md or SKILL.md in $($agentSub.FullName)"
    exit 1
}

# 目标路径
$dstSkillDir = Join-Path $skillsDir $AgentName

Write-Host "Installing $AgentName..."
Write-Host "  src agent.md: $srcAgentMd"
Write-Host "  src SKILL.md: $srcSkillMd"

# 1. 确保目标目录存在
if (-not (Test-Path $agentsDir)) { New-Item -ItemType Directory -Path $agentsDir -Force | Out-Null }
if (-not (Test-Path $dstSkillDir)) { New-Item -ItemType Directory -Path $dstSkillDir -Force | Out-Null }

# 2. 复制（-Force 覆盖，不删其他文件）
Copy-Item -Path $srcAgentMd -Destination (Join-Path $agentsDir "$AgentName.md") -Force
Write-Host "  agent.md -> $agentsDir\$AgentName.md"

Copy-Item -Path $srcSkillMd -Destination (Join-Path $dstSkillDir "SKILL.md") -Force
Write-Host "  SKILL.md -> $dstSkillDir\SKILL.md"

# 3. 门槛式验证：Test-Path 真测
$ok1 = Test-Path (Join-Path $agentsDir "$AgentName.md")
$ok2 = Test-Path (Join-Path $dstSkillDir "SKILL.md")

if ($ok1 -and $ok2) {
    Write-Host "OK: $AgentName installed and verified"
} else {
    Write-Host "FAIL: post-install verification failed for $AgentName"
    exit 1
}
