using System.Windows;
using System.Runtime.CompilerServices;

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]

// 测试项目可以访问 FileInstaller 的 internal override 字段
[assembly: InternalsVisibleTo("TeamWorkbench.Tests")]
