using System.Text;

namespace MasuitZip;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // GBK 解码需要注册代码页（Windows 中文系统打的 zip）
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { /* 忽略 */ }

        if (args.Contains("--selftest"))
        {
            SelfTest.Run();
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 开发用：把界面离屏渲染成图片，便于检查布局，不影响正常使用
        var shotIndex = Array.IndexOf(args, "--shot");
        if (shotIndex >= 0 && shotIndex + 1 < args.Length)
        {
            UiShot.Run(args[shotIndex + 1]);
            return;
        }

        Application.Run(new MainForm());
    }
}
