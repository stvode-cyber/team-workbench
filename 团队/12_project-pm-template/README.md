# 项目负责人 · 使用说明

## 一句话
每个新项目复制这个文件夹一份，改名字 + 改占位符 → 完事。

## 和 11 个通用 Agent 的区别
- **这个** = 项目专属，知道这个项目的进度/里程碑/阻塞
- **那 11 个** = 全域共享，不知道任何项目状态，但知道"推演/架构/安全"怎么干
- 两层自动配合：项目负责人调通用专岗来干活

## 安装

```powershell
# 前置：把文件夹改名叫 {项目名}-pm（如 GreenRhino-pm）
# 前置：把 agent.md 和 SKILL.md 里所有 {占位符} 改成项目实际信息

# 第 1 步：复制角色定义到项目级（不放用户级）
$projectRoot = "{项目根路径}"
New-Item "$projectRoot\.trae\agents" -Force | Out-Null
Copy-Item agent.md "$projectRoot\.trae\agents\{项目名}-pm.md"

# 第 2 步：复制专属知识到项目级
New-Item "$projectRoot\.trae\skills\{项目名}-pm" -Force | Out-Null
Copy-Item SKILL.md "$projectRoot\.trae\skills\{项目名}-pm\SKILL.md"

# 第 3 步：重启 Trae
```

## 验证
1. 说「{项目中文名} 现在进度到哪了」→ 它读项目进程.md 汇报
2. 说「加个 XX 功能」→ 改 ≥3 行它先过推演引擎

## 目录结构（正确的样子）

```
每个 Agent 一个文件夹，每个文件夹三件套：

Agent根目录/
├── 01_drize-engineer/
│   ├── agent.md      ← 角色定义（独立）
│   ├── SKILL.md      ← 专属知识（独立）
│   └── README.md     ← 使用说明（独立）
├── 02_arch-designer/
│   ├── agent.md
│   ├── SKILL.md
│   └── README.md
├── ...（11 个通用）
└── 12_project-pm-template/
    ├── agent.md
    ├── SKILL.md
    └── README.md
```

## 为什么这样才对
- 每个 Agent 完全独立，不混在一个大文件里
- SKILL.md 有自己的位置（不是合并在 agent.md 里）
- 改哪个 Agent 只动哪个文件夹，不影响其他
