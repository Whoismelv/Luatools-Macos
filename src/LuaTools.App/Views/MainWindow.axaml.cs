using Avalonia.Controls;
using LuaTools.Core;

namespace LuaTools.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // A machine-checkable signal that the GUI really came up. CI launches the
        // bundle and waits for this line, which is the only honest proof that the
        // window server + Avalonia native backend initialised.
        Opened += (_, _) =>
        {
            Console.WriteLine("LUATOOLS_WINDOW_OPENED");
            Console.Out.Flush();
            Log.Info("main window opened");
        };
    }
}
