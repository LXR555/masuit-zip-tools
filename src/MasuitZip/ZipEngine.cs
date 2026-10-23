using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Masuit.Tools.Files;

namespace MasuitZip;

/// <summary>进度回调：Percent 为 -1 表示"无法估算百分比"，用滚动条显示</summary>
public readonly record struct ProgressInfo(int Percent, string Message);

/// <summary>
/// 压缩/解压的实际干活类。
/// 压缩：.NET 自带 ZipArchive（Deflate），Windows 资源管理器双击就能打开。
/// 解压：zip 走 .NET（为了能报进度、能处理 GBK 中文名），rar/7z/gz/bz2/xz 交给 Masuit.Tools 自动识别。
/// </summary>
public static class ZipEngine
{
    private static readonly HttpClient Http = new();

    // ------------------------------------------------------------------ 压缩

    public static (int files, long bytes, TimeSpan elapsed) Compress(IEnumerable<string> sources, string zipPath, IProgress<ProgressInfo>? progress = null)
    {
        var list = sources.Select(Path.GetFullPath).Distinct().ToList();
        if (list.Count == 0) throw new InvalidOperationException("没有要压缩的内容。");

        var entries = new List<(string name, string full)>();
        foreach (var src in list)
        {
            if (Directory.Exists(src))
            {
                var root = Path.GetDirectoryName(src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))!;
                foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                {
                    entries.Add((Path.GetRelativePath(root, f).Replace('\\', '/'), f));
                }
            }
            else if (File.Exists(src))
            {
                entries.Add((Path.GetFileName(src), src));
            }
        }

        if (entries.Count == 0) throw new InvalidOperationException("选中的文件夹是空的。");

        var totalBytes = entries.Sum(e => new FileInfo(e.full).Length);
        long done = 0;
        var sw = Stopwatch.StartNew();

        var dir = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using (var fs = File.Create(zipPath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var (name, full) = entries[i];
                progress?.Report(new ProgressInfo((int)((long)i * 100 / entries.Count), $"正在压缩 {i + 1}/{entries.Count}：{Path.GetFileName(full)}"));

                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using (var dst = entry.Open())
                using (var src = File.OpenRead(full))
                {
                    src.CopyTo(dst);
                }

                done += new FileInfo(full).Length;
            }
        }

        sw.Stop();
        progress?.Report(new ProgressInfo(100, "压缩完成"));
        return (entries.Count, done, sw.Elapsed);
    }

    // ------------------------------------------------------------------ 解压

    public static (int files, TimeSpan elapsed) Extract(string archive, string outDir, IProgress<ProgressInfo>? progress = null)
    {
        if (!File.Exists(archive)) throw new FileNotFoundException("找不到压缩包。", archive);

        Directory.CreateDirectory(outDir);
        var sw = Stopwatch.StartNew();
        int count;

        if (IsTarLike(archive))
        {
            count = ExtractTar(archive, outDir, progress);
        }
        else if (IsZipMagic(archive))
        {
            count = ExtractZip(archive, outDir, progress);
        }
        else
        {
            // rar / 7z / gz / bz2 / xz —— 交给 Masuit.Tools 自动识别格式
            progress?.Report(new ProgressInfo(-1, "正在解压（该格式不提供进度，请稍候）…"));
            new SevenZipCompressor(Http).Decompress(archive, outDir, true);
            count = Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories).Count();
        }

        sw.Stop();
        progress?.Report(new ProgressInfo(100, "解压完成"));
        return (count, sw.Elapsed);
    }

    private static int ExtractZip(string archive, string outDir, IProgress<ProgressInfo>? progress)
    {
        var encoding = IsGbkZip(archive) ? Encoding.GetEncoding("GBK") : null;
        using var zip = ZipFile.Open(archive, ZipArchiveMode.Read, encoding);

        var root = Path.GetFullPath(outDir);
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var items = zip.Entries.ToList();
        var files = 0;

        for (var i = 0; i < items.Count; i++)
        {
            var entry = items[i];
            var dest = Path.GetFullPath(Path.Combine(root, entry.FullName));

            // 防目录穿越（zip-slip）
            if (!dest.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) && !string.Equals(dest, root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(dest);   // 目录项
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, true);
                files++;
                progress?.Report(new ProgressInfo((int)((long)(i + 1) * 100 / items.Count), $"正在解压 {i + 1}/{items.Count}：{Path.GetFileName(entry.FullName)}"));
            }
        }

        return files;
    }

    private static int ExtractTar(string archive, string outDir, IProgress<ProgressInfo>? progress)
    {
        progress?.Report(new ProgressInfo(-1, "正在解压 tar 包…"));
        using var fs = File.OpenRead(archive);
        if (archive.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) || archive.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gz, outDir, true);
        }
        else
        {
            TarFile.ExtractToDirectory(fs, outDir, true);
        }

        return Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories).Count();
    }

    // ------------------------------------------------------------------ 判断

    public static bool IsTarLike(string file) =>
        file.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ||
        file.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
        file.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

    /// <summary>看文件头是不是 zip（比扩展名靠谱，扩展名可以骗人）</summary>
    public static bool IsZipMagic(string file)
    {
        try
        {
            using var fs = File.OpenRead(file);
            var buf = new byte[4];
            if (fs.Read(buf, 0, 4) < 4) return false;
            return buf[0] == 0x50 && buf[1] == 0x4B &&
                   ((buf[2] == 0x03 && buf[3] == 0x04) || (buf[2] == 0x05 && buf[3] == 0x06) || (buf[2] == 0x07 && buf[3] == 0x08));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 判断 zip 里的文件名是不是 GBK（Windows 中文系统打包的 zip 就是这样）。
    /// 做法：同一份文件名分别按默认编码和 GBK 各解一遍，默认解不出汉字、GBK 能解出汉字，就认定是 GBK。
    /// </summary>
    public static bool IsGbkZip(string file)
    {
        if (!IsZipMagic(file)) return false;

        try
        {
            using (var zip = ZipFile.Open(file, ZipArchiveMode.Read, null))
            {
                foreach (var entry in zip.Entries)
                {
                    if (HasCjk(entry.FullName)) return false;
                }
            }

            using var gbkZip = ZipFile.Open(file, ZipArchiveMode.Read, Encoding.GetEncoding("GBK"));
            foreach (var entry in gbkZip.Entries)
            {
                if (HasCjk(entry.FullName)) return true;
            }
        }
        catch
        {
            // 打不开就算了，交给上层报错
        }

        return false;
    }

    private static bool HasCjk(string s)
    {
        foreach (var c in s)
        {
            if (c >= '\u4E00' && c <= '\u9FFF') return true;
            if (c >= '\u3400' && c <= '\u4DBF') return true;
        }

        return false;
    }

    /// <summary>去掉 .tar.gz 这类复合扩展名</summary>
    public static string BaseName(string file)
    {
        var name = Path.GetFileName(file);
        foreach (var suffix in new[] { ".tar.gz", ".tar.bz2", ".tar.xz", ".tgz", ".tar" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return name[..^suffix.Length];
        }

        return Path.GetFileNameWithoutExtension(name);
    }

    public static string Size(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1)
        {
            v /= 1024;
            i++;
        }

        return i == 0 ? $"{bytes} B" : $"{v:F1} {units[i]}";
    }
}
