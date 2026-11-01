global using Xunit;
global using Xunit.Abstractions;

// 禁用 xUnit 并行，因为 Database.DbPath 是 static 字段
// 多个 TempDbFixture 并行构造会互相覆盖 DbPath，导致 GetConnection()
// 打开错误的临时文件（Insert 写 A 文件，GetByName 读 B 空文件）
[assembly: CollectionBehavior(DisableTestParallelization = true)]
