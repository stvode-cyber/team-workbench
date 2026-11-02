---
name: handoff-sanity-check
description: 接手者视角完整项目检查 · 确保零对话历史下也能 30 秒接棒。Use when asked to check handoff, verify project state for new AI, or ensure docs are coherent. 含 6 步检查 + 3 类高频坑修复 SOP。
---

# Handoff Sanity Check · 接手者视角项目检查 SOP

**本项目：TeamWorkbench（AI 专岗员工可视化工作台 · C# WPF + Python 融合）**

> **触发条件**：用户说「检查项目」「接手确认」「handoff 检查」「确保交接链完整」「别人能看懂吗」
> 
> **核心原则**：模拟一个新 AI Agent **零对话历史接手**——只靠磁盘文件链，读 3 秒必须拿到完整项目状态。
> 任何需要看聊天记录才能理解的信息，都是文档链的缺陷。

---

## 一、6 步接棒检查（每次必跑，不能跳过）

### Step 1 · Git 快照（10 秒）

```powershell
# 1.1 当前 HEAD tag + branch
$env:GIT_PAGER = "cat"
git describe --tags --always           # v3.0.1 → commit hash
git branch -vv                         # * master 0289ef0 [origin/master]

# 1.2 远端状态
git remote -v                          # origin URL
git log HEAD..origin/master --oneline  # 本地有没有领先远端
git log origin/master..HEAD --oneline  # 远端有没有领先本地

# 1.3 tag 完整列表（按创建时间倒序）
git tag -l --sort=-creatordate | Select-Object -First 10

# 1.4 最近 20 个 commit（理解历史演进）
git log --oneline -20
```

### Step 2 · 文件结构快照（5 秒）

```powershell
# 2.1 顶层目录（排除 bin/obj）
Get-ChildItem -Directory | Where-Object { $_.Name -notin @("bin","obj","TestResults") } | Select-Object Name

# 2.2 核心文件清单（必须存在）
$required = @("AGENTS.md", "v3.1.0-融合指南针.md")
foreach ($f in $required) { Write-Output "$f : $(Test-Path $f)" }

# 2.3 .gitignore 关键项
Select-String -Path ".gitignore" -Pattern "bin/|obj/|TestResults/"
```

### Step 3 · AGENTS.md frontmatter 一致性检查（10 秒）

```powershell
# 3.1 读 frontmatter 前 8 行
Get-Content AGENTS.md -TotalCount 8

# 3.2 对比 frontmatter 里的 tag 和 git describe
git describe --tags --always
# → 如果 frontmatter git_tag 和实际 tag 不一样 → 版本 Lag（高频坑）

# 3.3 对比 frontmatter stage 和实际代码
# → stage 写"v3.1.0 融合设计阶段"但 python-engine/ 不存在 → 正确
# → stage 写"v3.1.0 Phase 2"但 Phase 1 还没动 → 过时

# 3.4 版本号更新（铁律！每次发版后必须同步）
# frontmatter version: "3.0" → "3.0.1" → "3.1.0" 必须和 tag 对齐
```

### Step 4 · 文档链一致性检查（20 秒）

**核心问题**：多个文档（AGENTS.md / HANDOFF.md / 指南针 / 历史 handoff）能不能互相印证？

```powershell
# 4.1 所有 .md 顶层文件列表
Get-ChildItem -Filter "*.md" | Select-Object Name, Length

# 4.2 HANDOFF.md 检查（高频坑！）
# → 如果 HANDOFF.md 描述的架构和 AGENTS.md ⑧ D1 决策不一致 → 过时
# → HANDOFF.md 顶部必须有 DEPRECATED 大注释（格式见本文档二）
Select-String -Path "HANDOFF.md" -Pattern "DEPRECATED|已废弃" -SimpleMatch

# 4.3 AGENTS.md 旧决策标注检查（高频坑！）
# 每个被替换的决策必须显式标注：
#   "✅ 废弃 → 实际是 xxx（tag xxx）"
# 如果旧决策没有任何标注 → 接手者会被误导
```

#### 🔥 指南针关联逻辑检查（Step 4.4-4.7 · 本项目独有的文档架构）

> **设计原则**：AGENTS.md 是总纲（稳定层，尽量少改），指南针是版本设计书（活跃层，每个大版本一本）。
> 两者通过**明确的指针链**关联，接手者沿着指针能完整理解项目。
> **如果指针断了 → 接手者迷路 → 必须修复。**

**指针链 5 层完整性（必须逐层验证）**：

```
Layer 1: AGENTS.md frontmatter.stage
    │  内容: "[>] v3.1.0 融合设计阶段"
    │  必须和最新版本对齐（git describe --tags）
    │  ↓ 指向
Layer 2: AGENTS.md ⑨ 板块（v3.1.0 融合路线图）
    │  内容: "完整设计文档独立存放：v3.1.0-融合指南针.md"
    │  必须有明确的文件名指针 + 板块序号
    │  ↓ 指向
Layer 3: v3.1.0-融合指南针.md 完整设计书
    │  内部结构: ①-⑬ 共 13 板块，覆盖 SQL/API/架构/工期/检查清单
    │  必须有 frontmatter（version / stage / status）
    │  必须有 ⑫ 自身变更追踪
    │  ↓ 回指
Layer 4: 指南针 → AGENTS.md 双向引用
    │  指南针必须写："本文档是 AGENTS.md ⑧ 的下一级指针"
    │  AGENTS.md ⑨ 必须写："唯一设计依据"
    │  两边互相印证
    │  ↓ 汇总
Layer 5: AGENTS.md ⑦ 变更追踪日志
       记录每次指南针迭代（REG-V31-DESIGN-xxx）
       记录每次指南针关联变动（指针增删）
```

**每层自动检测命令**：

```powershell
# Layer 1: frontmatter.stage 和 git tag 对齐
$fmStage = (Select-String -Path "AGENTS.md" -Pattern "^stage:" | ForEach-Object { $_.Line })
$tag = git describe --tags --always
Write-Output "frontmatter.stage: $fmStage"
Write-Output "git describe:       $tag"
# 语义判断（3 种情况）：
#   ✅ 正常：stage 写"v3.1.0 设计阶段" + tag 是 v3.0.1（设计阶段超前于 tag）
#   ✅ 正常：发版后 stage 和 tag 完全一致
#   ⚠️ Lag：stage 还写"v3.0 MVP"但 tag 已经 v3.0.1 或更高 → 需要更新 frontmatter
# 判断方法：
$fmVer = ($fmStage | Select-String -Pattern "v(\d+\.\d+\.\d+)" | ForEach-Object { $_.Matches.Groups[1].Value })
$tagVer = ($tag | Select-String -Pattern "v(\d+\.\d+\.\d+)" | ForEach-Object { $_.Matches.Groups[1].Value })
if ([version]$fmVer -ge [version]$tagVer) { Write-Output "Layer 1: ✅ stage($fmVer) >= tag($tagVer) 正常" }
else { Write-Output "Layer 1: ⚠️ stage($fmVer) < tag($tagVer) Lag！修复：改 AGENTS.md frontmatter" }

# Layer 2: AGENTS.md ⑨ 板块存在 + 含指南针文件名
# 注意：-SimpleMatch 会把 ^ 变成字面字符，所以不用它，保持纯正则让 ^ 行首锚定生效
$hasNine = Select-String -Path "AGENTS.md" -Pattern "^## ⑨"
$pointsToCompass = Select-String -Path "AGENTS.md" -Pattern "指南针" -SimpleMatch
Write-Output "⑨ 板块存在: $($null -ne $hasNine)"
Write-Output "指向指南针: $($null -ne $pointsToCompass)"
# → 缺失 → 指针断了 → 修复：在 AGENTS.md 末尾加 ⑨ 板块

# Layer 3: 指南针文件存在 + 有 frontmatter + 有变更追踪
$compassName = (Select-String -Path "AGENTS.md" -Pattern "v\d+\.\d+\.\d+-融合指南针\.md" | ForEach-Object { $_.Matches.Value })
$compassExists = Test-Path $compassName
$compassFrontmatter = (Get-Content $compassName -TotalCount 5) -match "^---"
$compassHasLog = Select-String -Path $compassName -Pattern "变更追踪" -SimpleMatch
Write-Output "指南针存在: $compassExists"
Write-Output "指南针有 frontmatter: $($compassFrontmatter.Count -gt 0)"
Write-Output "指南针有变更追踪: $($null -ne $compassHasLog)"
# → 任何一项 False → 指南针不完整

# Layer 4: 双向引用验证
# 指南针里必须有 "AGENTS.md" 关键词
$compassRefAgents = Select-String -Path $compassName -Pattern "AGENTS\.md" -SimpleMatch
Write-Output "指南针回指 AGENTS.md: $($null -ne $compassRefAgents)"
# AGENTS.md ⑨ 里必须有 "唯一设计依据"
$agentsRefCompass = Select-String -Path "AGENTS.md" -Pattern "唯一设计依据" -SimpleMatch
Write-Output "AGENTS.md 指向指南针: $($null -ne $agentsRefCompass)"
# → 任何一端缺 → 双向引用断了

# Layer 5: AGENTS.md ⑦ 变更日志里有指南针记录
$changes = Select-String -Path "AGENTS.md" -Pattern "v3\.1\.0|指南针|REG-V31" -SimpleMatch
Write-Output "AGENTS.md 有指南针变更记录: $($changes.Count -gt 0)"
# → 没有 → 变更日志和指南针不同步
```

**🔥 一键完整检测函数（复制粘贴直接跑）**：

```powershell
function Test-CompassChain {
    $env:GIT_PAGER = "cat"
    Write-Output "========== 指南针指针链 5 层完整检测 =========="
    
    # Layer 1: stage vs tag
    $fmStage = (Select-String -Path "AGENTS.md" -Pattern "^stage:" | ForEach-Object { $_.Line })
    $tag = git describe --tags --always
    $fmVer = ($fmStage | Select-String -Pattern "v(\d+\.\d+\.\d+)" | ForEach-Object { $_.Matches.Groups[1].Value })
    $tagVer = ($tag | Select-String -Pattern "v(\d+\.\d+\.\d+)" | ForEach-Object { $_.Matches.Groups[1].Value })
    $l1 = if ([version]$fmVer -ge [version]$tagVer) { "✅" } else { "⚠️ Lag" }
    Write-Output "L1 stage/tag:  $l1 ($fmVer ≥ $tagVer)"
    
    # Layer 2: ⑨ 板块
    $l2a = $null -ne (Select-String -Path "AGENTS.md" -Pattern "^## ⑨")
    $l2b = $null -ne (Select-String -Path "AGENTS.md" -Pattern "指南针" -SimpleMatch)
    Write-Output "L2 ⑨/指针:     $(if($l2a){'✅'}else{'❌'}) ⑨板块 | $(if($l2b){'✅'}else{'❌'}) 指针"
    
    # Layer 3: 指南针文件
    $compassName = (Select-String -Path "AGENTS.md" -Pattern "v\d+\.\d+\.\d+-融合指南针\.md" | ForEach-Object { $_.Matches.Value } | Select-Object -First 1)
    $l3a = Test-Path $compassName
    $l3b = (Get-Content $compassName -TotalCount 1 | Select-String "^---").Count -gt 0
    Write-Output "L3 指南针:     $(if($l3a){'✅'}else{'❌'}) 存在 | $(if($l3b){'✅'}else{'❌'}) frontmatter"
    
    # Layer 4: 双向引用
    $l4a = (Select-String -Path $compassName -Pattern "AGENTS" -SimpleMatch).Count -gt 0
    $l4b = $null -ne (Select-String -Path "AGENTS.md" -Pattern "唯一设计依据" -SimpleMatch)
    Write-Output "L4 双向引用:   $(if($l4a){'✅'}else{'❌'}) 指南针→AGENTS | $(if($l4b){'✅'}else{'❌'}) AGENTS→指南针"
    
    # Layer 5: 变更日志
    $l5 = (Select-String -Path "AGENTS.md" -Pattern "REG-V3" -SimpleMatch).Count
    Write-Output "L5 变更日志:   $(if($l5 -gt 0){'✅'}else{'❌'}) $l5 条 REG 记录"
    
    Write-Output "========== 完成 =========="
    # 全部 ✅ 才算通过
    $pass = ($l1 -eq "✅") -and $l2a -and $l2b -and $l3a -and $l3b -and $l4a -and $l4b -and ($l5 -gt 0)
    Write-Output "总体: $(if($pass){'✅ 5/5 全通'}else{'⚠️ 有断链，用下方修复模板修'})"
}

# 使用：直接跑
Test-CompassChain
```

**指针断链修复模板**：

```markdown
# === Layer 1 断链修复 ===
# AGENTS.md frontmatter version/stage 滞后
# 直接改 frontmatter：
---
version: "3.1.0-fusion-design"        ← 和最新 tag 对齐
stage: "[>] v3.1.0 融合设计阶段"       ← 和实际阶段对齐
---

# === Layer 2 断链修复 ===
# AGENTS.md 缺 ⑨ 板块
# 在 ⑧ 板块之后、⑦ 板块之后（注意顺序，⑦ 在前面）加：
## ⑨ [版本号] [路线图标题]

> ⚠️ **完整设计文档独立存放**：`[指南针文件名]`
> 本板块只放指针 + 核心决策 + 与上一版的关系。
> 所有开发、测试、发版都以 `[指南针文件名]` 为唯一设计依据。

# === Layer 3 断链修复 ===
# 指南针文件不存在或不完整
# 新建指南针，必须包含 13 板块结构（参照上一版指南针）
# frontmatter 必须有 version / stage / status / parent
# 必须有 ⑫ 自身变更追踪板块

# === Layer 4 断链修复 ===
# 指南针缺回指
# 在指南针开头加：
> **定位**：v3.1.0 版本设计书。AGENTS.md ⑨ 板块为指针入口。
# AGENTS.md ⑨ 缺指向
# 在 AGENTS.md ⑨ 加：
> 所有开发、测试、发版都以 `v3.1.0-融合指南针.md` 为唯一设计依据。

# === Layer 5 断链修复 ===
# AGENTS.md ⑦ 缺指南针变更记录
# 在变更日志表末尾追加：
| YYYY-MM-DD | REG-V31-DESIGN-NNN ✅ | 指南针新增/修改内容 | 原因 | 影响范围 |
```

**指针链检查频率**：
- 每次新建版本指南针时（v3.1.0 → v3.2.0 → ...）
- 每次发版后（tag vX.Y.Z）
- 每次 AGENTS.md frontmatter 变动时

### Step 5 · 环境可运行性验证（30 秒）

```powershell
# 5.1 现有测试（最重要，必须全绿）
dotnet test TeamWorkbench.Tests/TeamWorkbench.Tests.csproj --no-restore -v q
# → 58/58 green, 0 failed（失败=接手阻塞）

# 5.2 Release build
dotnet build TeamWorkbench/TeamWorkbench.csproj -c Release --no-restore -v q
# → 0 warnings, 0 errors（警告=质量红线）

# 5.3 关键文件存在性
Test-Path "TeamWorkbench/TeamWorkbench.csproj"   # C# 项目
Test-Path "python-engine/"                       # Phase 1 未开工 → False（正确）
Test-Path "dist/v3.1.0-singlefile/"              # Release 产物 → 不存在（正确）
```

### Step 6 · 冻结边界确认（10 秒）

读指南针 ⑪ 板块，明确哪些文件**绝对不能改**：
- v3.0 已有 11 个 agent 的 agent.md + SKILL.md
- v3.0 已有 SQLite agents 表 schema
- v3.0 已有 58 xUnit tests
- v3.1.0 Python 调度引擎开发后：Scheduler 四种模式行为、SQL schema、SSE 事件类型

```powershell
# 交叉验证：指南针 ⑪ 和 AGENTS.md ⑥ 三色边界灯必须一致
# 如果指南针说"agent.md 不许改"但 AGENTS.md ⑥ 说"允许编辑 agent.md" → 矛盾
$compassFreeze = Select-String -Path $compassName -Pattern "冻结边界|不许改|禁止修改" -SimpleMatch
$agentsFreeze = Select-String -Path "AGENTS.md" -Pattern "绝对禁止|🚫|三色边界" -SimpleMatch
Write-Output "指南针冻结边界存在: $($null -ne $compassFreeze)"
Write-Output "AGENTS.md 边界灯存在: $($null -ne $agentsFreeze)"
# → 两边任何一边缺 → 边界定义不完整
```

---

## 二、3 类高频坑修复 SOP

### 坑 1 · HANDOFF.md 过时（严重度 🔴 P0）

**症状**：HANDOFF.md 描述的架构（Node/Express + Vue3）和实际项目（C# WPF）完全不一样。

**检测**：
```powershell
Select-String -Path "HANDOFF.md" -Pattern "Express|Vue|server/|web/" -SimpleMatch
# → 命中但实际项目里这些目录不存在 → 过时
```

**修复**（在文件顶部加）：
```markdown
> ⚠️ **DEPRECATED · 本文件内容已过时**
> 
> 本文件记录的是 **[旧架构名]**（[时间]）。
> 该架构已被 **[新架构名]**（tag [tag] → commit [hash]）完全替换。
> 
> **新接手者请看（按优先级）：**
>   1. AGENTS.md frontmatter + [关键板块号]
>   2. [新设计文档名]
>   3. [实际代码目录]
>   4. 本文件可忽略，仅作历史参考
> 
> 修复日期：[YYYY-MM-DD] · REG-HANDOFF-DEPRECATE-[序号]

---
（然后把旧内容用 --- 分隔保留，标注"历史存档"）
```

### 坑 2 · 旧决策未标注废弃（严重度 🟡 P1）

**症状**：AGENTS.md ⑧ D1 选了 Node/Express，但项目实际是 C# WPF。新接手者读 D1 会困惑。

**检测**：
```powershell
# 方法 1：对比决策描述和 git 历史
git log --all --oneline | Select-String -Pattern "Express|Vue"

# 方法 2：对比决策描述和实际文件
Test-Path "server/"  # False → v3.0 Node/Express 已删
Test-Path "web/"     # False → v3.0 Vue3 已删

# 方法 3：搜索旧决策有没有被新决策覆盖
Select-String -Path "AGENTS.md" -Pattern "D1" -SimpleMatch
# → 两条 D1（⑧ 和 ⑨）→ 旧的必须标注废弃
```

**修复**（在每条旧决策后追加 + 板块尾部加警告）：
```markdown
| D1 | 技术栈 | **B. Node/Express + SQLite** | ✅ 废弃 → 实际是 C# WPF（v3.0-csharp-wpf tag） |

> ⚠️ **以上 D1-D5 为 v3.0 MVP 原型决策（[时间]），已被完全替换**。
> [时间] 新决策见 **[新板块号]**。
> 修复：REG-HANDOFF-DEPRECATE-[序号]
```

### 坑 3 · frontmatter version 滞后（严重度 🟡 P1）

**症状**：AGENTS.md frontmatter version 写 "3.0"，但 git tag 已经 v3.0.1。

**检测**：
```powershell
$fmVer = (Select-String -Path "AGENTS.md" -Pattern "^version:" | ForEach-Object { $_.Line })
$tagVer = git describe --tags --always
Write-Output "frontmatter: $fmVer"
Write-Output "git tag:     $tagVer"
# → 如果不一致 → 版本 Lag
```

**修复**：
```powershell
# 直接改 frontmatter version
# 必须和最新 tag 对齐
```

---

## 三、30 秒接棒检查清单（最终交付标准）

新接手 AI 必须在读完以下内容后能回答：

| # | 问题 | 答案来源 | 必须精确 |
|---|------|----------|----------|
| Q1 | 项目现在处于哪个阶段？ | AGENTS.md stage | 融合设计阶段 / Phase 1 / Phase 2 |
| Q2 | 当前 HEAD tag 是啥？ | `git describe --tags` | v3.0.1 / v3.1.0-fusion 等 |
| Q3 | v3.1.0 设计依据在哪？ | AGENTS.md ⑨ 板块指针 | `v3.1.0-融合指南针.md` |
| Q3.1 | 指南针文件存在吗？ | `Test-Path` | True（必须） |
| Q3.2 | AGENTS.md ⑨ 板块写了「唯一设计依据」吗？ | Step 4.4 Layer 4 检测 | True（双向引用） |
| Q3.3 | 指南针回指 AGENTS.md 了吗？ | Step 4.4 Layer 4 检测 | True（双向引用） |
| Q3.4 | AGENTS.md ⑦ 变更日志有指南针记录吗？ | Step 4.4 Layer 5 检测 | True（REG-V31-DESIGN-xxx） |
| Q4 | 现有环境能跑吗？ | `dotnet test` + `dotnet build` | 58/58 绿 / 0 警告 |
| Q5 | Phase 1 要建什么目录？ | 指南针 ⑧ 文件树 | python-engine/ 不存在（未开工） |
| Q6 | 哪些文件绝对不能改？ | 指南针 ⑪ 冻结边界 | v3.0 已有 agent.md + agents 表 schema |
| Q7 | 有没有已知坑？ | 指南针 ③ GAP 板块 | GAP-01 Trae 无 AI CLI |
| Q8 | 下一步第一天做什么？ | 指南针 ⑨ 工期 Day 1 | SQLite schema 对齐 + Python 骨架 |

**如果 Q3.1-Q3.4 任何一个答不出来 → 指南针指针链断了 → 用 Step 4.4 修复模板修。**

---

## 四、频率建议

- **发版后必跑**：每次 `git tag vX.Y.Z` 后立即跑 6 步检查（Step 1-6）
- **交接前必跑**：任何 AI Agent 要转交给另一个前
- **Phase 切换时必跑**：Phase 1 → Phase 2 → Phase 3 每次切阶段
- **日常可选**：每周一次快速扫（只跑 Step 1 + Step 3）

---

## 五、实战案例（本项目 2026-11-01）

**输入**：零对话历史，只靠磁盘文件

**发现的 3 个坑**：
1. 🔴 HANDOFF.md 过时（Node/Express/Vue3 架构描述）→ 加 14 行 DEPRECATED 注释
2. 🟡 AGENTS.md ⑧ D1-D5 未标注废弃 → 每条加 ✅ 废弃标注 + 板块尾部警告
3. 🟡 AGENTS.md frontmatter version "3.0" → 改 "3.1.0-fusion-design"

**结果**：修复前评分 8.8/10 → 修复后 9.8/10

**参考 commit/tag**：
- tag v3.0.1（0289ef0）→ 修复后（未 tag，随 Phase 1 一起）
- 指南针：`v3.1.0-融合指南针.md`（777 行 13 板块）
