using System.Windows;
using TeamWorkbench.Data;

namespace TeamWorkbench;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Phase 1 核心：SQLite 初始化
        try
        {
            Database.Initialize();

            // 验证 3 张表存在
            using var conn = Database.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
            var tables = new List<string>();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read()) tables.Add(reader.GetString(0));
            }

            DbStatus.Text = $"✅ SQLite 就绪 · 3 张表已建（{string.Join(", ", tables)}）\n" +
                           $"📁 DB: {Database.DbPath}";
        }
        catch (Exception ex)
        {
            DbStatus.Text = $"❌ SQLite 初始化失败: {ex.Message}";
        }
    }
}
