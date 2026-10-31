# Hook 模板清单

> Hook = 你定的自动闹钟，AI 碰到特定事件自动干活
> 配置方式：Trae 设置 → Hooks → 添加

## P0 必配

| Hook | 触发条件 | 动作 | 作用域 | 效果 |
|---|---|---|---|---|
| 对话自动加载 | 每次对话开始前 | 加载 user_rules | 全域 | 1号指令已配好，不用动 |
| 写文件自动登记 | AI 调用 write/edit 后 | 调 ledger-keeper | 全域 | 写完自动写台账 |
| Shell 退出码检查 | AI 调用 shell 后且 exit code ≠ 0 | 自动输出错误摘要 | 全域 | 命令挂了 AI 自己会说 |

## P1 推荐

| Hook | 触发条件 | 动作 | 作用域 | 效果 |
|---|---|---|---|---|
| git push 走推演 | shell 包含 `git push` 或 `git commit` 前 | 调 drize-engineer | 全域 | 推送前自动推演风险 |
| 拦截 .env 写入 | 文件路径包含 `.env` 前 | 拦截 + 警告 | 全域 | 防止写敏感文件 |
| 新项目自动架构 | 关键词触发"新项目/从零搭建" | 调 arch-designer | 全域 | 新项目不直接 npm create |

## P2 可选

| Hook | 触发条件 | 动作 |
|---|---|---|
| 发版自动安全检查 | 关键词"发版/部署/release" | 调 safety-auditor |
| 测试失败自动 RCA | shell 输出包含 FAIL/error/exit 1 | 调 rca-analyst |
| 对话结束自动 DC 卡 | 对话结束后 | 调 report-writer |

## 注意

- Hook 可以拦截操作（防炸），也可以追加动作（自动干活）
- 有风险的 Hook 先配项目级，确认没问题再升全域
- 跑 Shell 命令的 Hook 要小心，别让 AI 自动跑危险命令
