# install-all-agents.ps1
# 一键安装全部 11 个 Agent
# 循环调 install-single-agent.ps1，每个装完真验证
# 运行：powershell -File .\scripts\install-all-agents.ps1

$ErrorActionPreference = "Continue"   # 一个装坏不影响其他

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$installOne = Join-Path $scriptDir "install-single-agent.ps1"

$agentNames = @(
    "drize-engineer", "arch-designer", "project-taker", "ledger-keeper",
    "rca-analyst", "competitor-surveyor", "backend-expert", "safety-auditor",
    "report-writer", "agents-builder", "mcp-finder"
)

$ok = 0; $fail = 0; $errors = @()

foreach ($name in $agentNames) {
    Write-Host ""
    $result = & powershell -File $installOne -AgentName $name 2>&1
    $exitCode = $LASTEXITCODE

    if ($exitCode -eq 0) {
        $ok++
        Write-Host "✅ $name OK" -ForegroundColor Green
    } else {
        $fail++
        $errors += "$name (exit=$exitCode)"
        Write-Host "❌ $name FAILED (exit=$exitCode)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=== 汇总 ==="
Write-Host "成功: $ok  失败: $fail" -ForegroundColor $(if($fail -eq 0){"Green"}else{"Red"})
if ($errors.Count -gt 0) {
    Write-Host "失败列表:"
    $errors | ForEach-Object { Write-Host "  - $_" }
}
Write-Host "提示：装完重启 Trae 或开新对话，AI 自动识别新 Agent"
