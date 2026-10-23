using System.Drawing.Imaging;

namespace MasuitZip;

/// <summary>开发用：把主界面离屏渲染成 PNG，方便检查布局（不弹窗、不依赖桌面）</summary>
internal static class UiShot
{
    public static void Run(string path)
    {
        using var form = new MainForm();
        form.Show();
        Application.DoEvents();
        Application.DoEvents();

        using var bmp = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
        bmp.Save(path, ImageFormat.Png);

        form.Close();
    }
}
