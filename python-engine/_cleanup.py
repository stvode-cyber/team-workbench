"""清理 checkpoint 脏数据（历史 STARTED 未 COMMITTED）"""
from db import engine
from sqlalchemy import text

with engine.begin() as conn:
    conn.execute(text("DELETE FROM checkpoints WHERE status != 'COMMITTED'"))

with engine.connect() as conn:
    c = conn.execute(text("SELECT COUNT(*) FROM checkpoints")).scalar()
    print(f"清理后 checkpoints 总数: {c}")
