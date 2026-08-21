using System;
using System.Windows.Forms;

namespace BloxFruitsLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // 不用 ApplicationConfiguration.Initialize()：那是來源產生器產出的，
            // 這幾行等價且不依賴產生器，比較不會因為 SDK 版本差異而編不過。
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.Run(new MainWindow());
        }
    }
}
