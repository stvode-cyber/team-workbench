## 一句话简介

安全巡视员专岗：提交代码前 / 发版前扫版本对齐 + 硬编码敏感数据 + 别碰文件 + 注入风险。

## 什么时候触发

- 提交代码前 / PR review 前
- 发版前 / 部署前
- 你说"检查一下"、"安全审计"、"版本对齐"、"扫一遍"
- 改动涉及敏感路径（密码/鉴权/配置文件）

## 输出长啥样（示例）

```
═══ 安全巡检卡 ═══
├─ 巡检对象：用户中心 v1.3.0
├─ 巡检时点：发版前
│
├─ ① 版本对齐：
│   ├─ 当前版本：v1.2.5 → 目标版本：v1.3.0 ✅ 递增正确
│   ├─ CHANGELOG 对齐：✅ 记录了新功能 + 修了 3 个 bug
│   └─ monorepo 子包版本：全部对齐 ✅
│
├─ ② 别碰文件检查：
│   ├─ .gitignore 排除：.env / secrets/ / *.pem
│   ├─ AGENTS.md 安全红线：config/production/ 不许碰
│   └─ 本次改动触碰清单：✅ 无
│
├─ ③ 硬编码扫描：
│   ├─ ✅ 干净，未发现敏感数据
│   └─ 测试用假数据：tests/fixtures/keys.json（标低风险）
│
├─ ④ 注入风险：
│   ├─ SQL 拼接：✅ 无（全部用 Prisma）
│   ├─ Shell 注入：✅ 无
│   └─ 命令注入：✅ 无
│
├─ ⑤ 发版前额外：
│   ├─ 构建：✅ 通过（耗时 42s）
│   ├─ 环境变量：✅ 干净（无 localhost 残留）
│   ├─ DB 迁移回滚：✅ down 脚本已写
│   ├─ npm audit：✅ 0 Critical / 1 Low（可忽略）
│   └─ 发版说明：✅ 写了
│
└─ 严重级别汇总：全部 🟢 OK，可以发版
```

## 安装步骤

```bash
# 第 1 步：复制 agent.md
cp 08_safety-auditor/agent.md ~/.trae/agents/

# 第 2 步：复制 SKILL.md（先建目录）
mkdir -p ~/.trae/skills/safety-auditor
cp 08_safety-auditor/SKILL.md ~/.trae/skills/safety-auditor/

# 第 3 步：重启 Trae
```

## 常见问题

**Q：版本对齐铁律具体管啥？**
A：管版本号方向绝对不能反。比如用户的 app 当前是 v1.2.5，新版本是 v1.3.0——必须能正确判断"需要更新"。如果比较逻辑写反了，用户装了新包还会弹"需要更新"，那就是严重 bug。SKILL.md 里有 4 个检查点。

**Q：npm audit fix --force 能直接跑吗？**
A：不能。`--force` 会强制大版本升级，可能 Breaking Change。先 `npm audit fix`（只修小版本），再看剩下的漏洞是不是真的影响你——很多漏洞在你的项目里根本用不到（比如你没在用那个有漏洞的功能）。

**Q：硬编码扫描扫到了测试 fixture 里的假数据怎么办？**
A：SKILL.md 里明确——要区分真数据 vs 假数据。真数据（能跑通生产）立刻改环境变量。假数据（测试用 fixture 里的、带 test/mock 前缀的）也要列出来但标"低风险"。不能混在一起报。

**Q：别碰文件清单从哪来？**
A：= .gitignore 排除的 + AGENTS.md 安全红线列的 + 项目约定的敏感路径。SKILL.md 里有具体的查找路径。如果触碰了别碰文件，必须登记 IMP-xx + 说明为什么触碰 + 改完再扫一遍。
