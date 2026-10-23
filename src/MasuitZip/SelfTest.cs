using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace MasuitZip;

/// <summary>无人值守自检：跑一遍压缩/解压，把结果写到 exe 同目录的 selftest-report.txt</summary>
internal static class SelfTest
{
    public static void Run()
    {
        var log = new StringBuilder();
        var root = Path.Combine(Path.GetTempPath(), "MasuitZipTest_" + Guid.NewGuid().ToString("N")[..8]);
        var ok = true;

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "资料", "子目录"));
            File.WriteAllText(Path.Combine(root, "资料", "客户清单.txt"), "张三\n李四\n");
            File.WriteAllText(Path.Combine(root, "资料", "子目录", "报价单.txt"), "价格 100\n");

            // 1) 压缩
            var zip = Path.Combine(root, "测试.zip");
            var r1 = ZipEngine.Compress(new[] { Path.Combine(root, "资料") }, zip);
            log.AppendLine($"[压缩] 文件 {r1.files} 个，源数据 {ZipEngine.Size(r1.bytes)}，压缩包 {ZipEngine.Size(new FileInfo(zip).Length)}，用时 {r1.elapsed.TotalMilliseconds:F0} ms");

            // 2) 解压 zip
            var out1 = Path.Combine(root, "解压1");
            var r2 = ZipEngine.Extract(zip, out1);
            var f1 = Path.Combine(out1, "资料", "子目录", "报价单.txt");
            var pass1 = File.Exists(f1) && File.ReadAllText(f1).Contains("价格");
            ok &= pass1;
            log.AppendLine($"[解压 zip] 文件 {r2.files} 个，用时 {r2.elapsed.TotalMilliseconds:F0} ms，中文文件名与目录结构：{(pass1 ? "正确" : "错误")}");

            // 3) tar.gz
            var tgz = Path.Combine(root, "测试.tar.gz");
            using (var fs = File.Create(tgz))
            using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
            {
                TarFile.CreateFromDirectory(Path.Combine(root, "资料"), gz, false);
            }

            var out2 = Path.Combine(root, "解压2");
            var r3 = ZipEngine.Extract(tgz, out2);
            var pass2 = File.Exists(Path.Combine(out2, "子目录", "报价单.txt"));
            ok &= pass2;
            log.AppendLine($"[解压 tar.gz] 文件 {r3.files} 个，用时 {r3.elapsed.TotalMilliseconds:F0} ms，内容：{(pass2 ? "正确" : "错误")}");

            // 4) 压缩包可读性
            using (var z = ZipFile.OpenRead(zip))
            {
                log.AppendLine($"[校验] 压缩包条目 {z.Entries.Count} 个，可正常打开：是");
            }
        }
        catch (Exception ex)
        {
            ok = false;
            log.AppendLine($"[异常] {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { /* 清理失败无所谓 */ }
        }

        log.Insert(0, $"自检结果：{(ok ? "全部通过" : "存在失败")}{Environment.NewLine}{Environment.NewLine}");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "selftest-report.txt"), log.ToString(), Encoding.UTF8);
    }
}
