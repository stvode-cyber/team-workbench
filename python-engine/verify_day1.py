"""Day 1 验证脚本：检查 11 张表 + 4 索引 + 3 PRAGMA 是否就位

注意：
  - sqlite3 原生连接不走 SQLAlchemy event listener，需要手动设 PRAGMA
  - PRAGMA synchronous / busy_timeout 是 per-connection 设置
  - PRAGMA journal_mode=WAL 会持久化到数据库文件
"""
import sqlite3
import os

db = os.path.expandvars(r"%APPDATA%\TeamWorkbench\data.db")
print(f"DB: {db}")
print()

conn = sqlite3.connect(db)
# ── 关键：手动设 3 PRAGMA，跟 SQLAlchemy event listener 保持一致 ──
conn.execute("PRAGMA journal_mode=WAL")
conn.execute("PRAGMA synchronous=NORMAL")
conn.execute("PRAGMA busy_timeout=5000")
cur = conn.cursor()

# 表
tables = [r[0] for r in cur.execute(
    "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"
).fetchall()]
print(f"表总数: {len(tables)}")
for t in tables:
    print(f"  {t}")

# PRAGMA（注意 SQLite 可能返回数字而非字符串）
print()
jm = cur.execute("PRAGMA journal_mode").fetchone()[0]
sy = cur.execute("PRAGMA synchronous").fetchone()[0]  # 数字：0=OFF 1=NORMAL 2=FULL 3=EXTRA
bt = cur.execute("PRAGMA busy_timeout").fetchone()[0]
sy_name = {0: "OFF", 1: "NORMAL", 2: "FULL", 3: "EXTRA"}.get(sy, sy)
print(f"PRAGMA journal_mode  = {jm} {'✅' if jm == 'wal' else '❌'}")
print(f"PRAGMA synchronous   = {sy_name} ({sy}) {'✅' if sy == 1 else '❌'}")  # NORMAL = 1
print(f"PRAGMA busy_timeout  = {bt} {'✅' if str(bt) == '5000' else '❌'}")

# 索引
print()
indexes = [r[0] for r in cur.execute(
    "SELECT name FROM sqlite_master WHERE type='index' AND name LIKE 'idx_%' ORDER BY name"
).fetchall()]
print(f"指南针 4 个索引: {len(indexes)}")
for i in indexes:
    print(f"  {i}")

# 验收
expected_tables = {
    "agents", "install_log", "status_events", "schema_version",   # C# 旧表
    "sessions", "agent_runs", "checkpoints", "pipelines",
    "approval_requests", "llm_config", "audit_log",               # Python 新表
}
# sqlite_sequence 是 SQLite 自增主键内部表，正常存在，不算 extra
extra_tables_ok = {"sqlite_sequence"}
actual_extra = (set(tables) - expected_tables) - extra_tables_ok
missing = expected_tables - set(tables)
print()
# 验收条件：jm == 'wal'（字符串）且 sy == 1（数字），不再用 sy == "normal"
if (not missing and not actual_extra
        and jm == "wal" and sy == 1 and str(bt) == "5000"
        and len(indexes) == 4):
    print("✅ Day 1 验收全部通过！")
else:
    if missing: print(f"❌ 缺表: {missing}")
    if actual_extra:   print(f"⚠️  多表: {actual_extra}")
    if len(indexes) != 4: print(f"❌ 索引数不对: {len(indexes)}")

conn.close()

# ─── SQLAlchemy 验证 PRAGMA（走 db.py 的 engine + 监听器）───
print()
print("=== SQLAlchemy engine 验证 PRAGMA（走监听器）===")
import sys; sys.path.insert(0, ".")
from db import engine
from sqlalchemy import text
with engine.connect() as c:
    jm = c.execute(text("PRAGMA journal_mode")).scalar()
    sy = c.execute(text("PRAGMA synchronous")).scalar()
    bt = c.execute(text("PRAGMA busy_timeout")).scalar()
    sy_name = {0: "OFF", 1: "NORMAL", 2: "FULL", 3: "EXTRA"}.get(sy, sy)
    print(f"journal_mode = {jm} {'✅' if jm == 'wal' else '❌'}")
    print(f"synchronous  = {sy_name} ({sy}) {'✅' if sy == 1 else '❌'}")
    print(f"busy_timeout = {bt} {'✅' if str(bt) == '5000' else '❌'}")
    if jm == "wal" and sy == 1 and str(bt) == "5000":
        print("✅ SQLAlchemy 3 PRAGMA 监听器全生效！")
    else:
        print("❌ 有 PRAGMA 没生效")
