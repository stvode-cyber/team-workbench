using System;
using System.IO;
using TeamWorkbench.Data;
using TeamWorkbench.Services;

namespace TeamWorkbench.Tests;

/// <summary>
/// 每个测试类的临时 DB fixture
/// 用临时文件（而非 :memory:），因为 Microsoft.Data.Sqlite :memory:
/// 每条 SqliteConnection 是独立空库，GetConnection() 每次 new 连接后
/// AgentRepository.GetAll() 永远拿不到 Seed 数据
/// </summary>
public class TempDbFixture : IDisposable
{
    public readonly string TempPath;

    public TempDbFixture()
    {
        TempPath = Path.Combine(
            Path.GetTempPath(),
            $"tw_test_{Guid.NewGuid():N}.db");
        Database.Initialize(TempPath);
        SeedService.SeedIfEmpty();
    }

    public void Dispose()
    {
        try { if (File.Exists(TempPath)) File.Delete(TempPath); }
        catch { /* 文件可能被占用，忽略 */ }
    }
}
