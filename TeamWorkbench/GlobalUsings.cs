// UseWindowsForms + UseWPF 共存时的类型冲突统一解
// WPF 版设为默认，WinForms 版用全限定名 System.Windows.Forms.XXX
global using Application = System.Windows.Application;
global using Point = System.Windows.Point;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
global using MessageBox = System.Windows.MessageBox;
global using UserControl = System.Windows.Controls.UserControl;
