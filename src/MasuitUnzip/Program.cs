using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Masuit.Tools.Files;

namespace MasuitUnzip;

/// <summary>
/// 命令行小工具，核心解压能力来自 Masuit.Tools(码数吐司库)：
///   Masuit.Tools.Files.SevenZipCompressor.Decompress(压缩包, 输出目录)
/// 内部走 SharpCompress，自动识别 zip / rar / 7z / tar / gz / bz2 / xz 等格式。
/// 列表和压缩用 .NET 自带的 System.IO.Compression（保证 Windows 资源管理器能打开）。
/// </summary>
internal static class Program
{
    private const string Version = "1.0";
    private static readonly HttpClient Http = new();

    private static int Main(string[] rawArgs)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* 个别宿主不允许改编码，忽略 */ }
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { /* GBK 兜底用，失败不影响主流程 */ }

        if (rawArgs.Length == 0)
        {
            return Interactive();
        }

        var noPause = rawArgs.Any(a => a is "--no-pause" or "-n");
        var args = rawArgs.Where(a => a is not ("--no-pause" or "-n")).ToArray();

        int code;
        try
        {
            code = Run(args);
        }
        catch (Exception ex)
        {
            Fail(ex);
            code = 1;
        }

        if (!noPause)
        {
            Console.WriteLine();
            Console.WriteLine("按回车键退出...");
            Console.ReadLine();
        }

        return code;
    }

    /// <summary>无参数时进入交互模式，方便把压缩包拖进窗口</summary>
    private static int Interactive()
    {
        Console.WriteLine($"MasuitUnzip v{Version}  —— 基于 Masuit.Tools(码数吐司库)");
        Console.WriteLine("解压支持：zip / rar / 7z / tar / tar.gz / gz / bz2 / xz 等（自动识别）");
        Console.WriteLine("不支持带密码的压缩包。");
        Console.WriteLine();

        while (true)
        {
            Console.Write("把压缩包拖进本窗口（或粘贴路径），直接回车退出：");
            var line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) return 0;

            line = line.Trim().Trim('"');
            try
            {
                Run(line.StartsWith('-') ? line.Split(' ', StringSplitOptions.RemoveEmptyEntries) : new[] { line });
            }
            catch (Exception ex)
            {
                Fail(ex);
            }

            Console.WriteLine(new string('-', 64));
        }
    }

    private static int Run(string[] args)
    {
        switch (args[0].ToLowerInvariant())
        {
            case "-h":
            case "--help":
            case "help":
                Usage();
                return 0;

            case "-l":
            case "--list":
                return ShowList(NeedArg(args, 1));

            case "-z":
            case "--zip":
                if (args.Length < 3)
                {
                    Console.WriteLine("[错误] 压缩用法：-z <输出.zip> <文件或文件夹> [更多...]");
                    return 2;
                }
                return CreateZip(args[1], args.Skip(2));
        }

        string? outDir = null;
        var here = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] is "-o" or "--out") outDir = NeedArg(args, i + 1);
            else if (args[i] is "--here") here = true;
        }

        return Decompress(args[0], here ? Path.GetDirectoryName(FullPath(args[0])) : outDir);
    }

    // ---------------------------------------------------------------- 解压

    private static int Decompress(string pathArg, string? outDir)
    {
        var file = FullPath(pathArg);
        if (!File.Exists(file))
        {
            Console.WriteLine($"[错误] 找不到文件：{file}");
            return 2;
        }

        Console.WriteLine($"压缩包：{file}");
        Console.WriteLine($"大  小：{Human(new FileInfo(file).Length)}");

        outDir ??= Path.Combine(Path.GetDirectoryName(file)!, BaseName(file));
        outDir = Path.GetFullPath(outDir);
        Directory.CreateDirectory(outDir);
        Console.WriteLine($"解压到：{outDir}");

        var sw = Stopwatch.StartNew();
        try
        {
            Extract(file, outDir);
        }
        catch
        {
            CleanupEmptyDir(outDir);
            throw;
        }
        sw.Stop();

        var files = Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories).Count();
        Console.WriteLine($"[完成] 用时 {sw.Elapsed.TotalSeconds:F1} 秒，目录内现有 {files} 个文件。");
        return 0;
    }

    private static void Extract(string file, string outDir)
    {
        if (IsTarLike(file))
        {
            // tar / tar.gz：SharpCompress 0.50 对双层压缩包的侦测失效，改用 .NET 自带的 System.Formats.Tar
            ExtractTar(file, outDir);
        }
        else if (IsGbkZip(file, out var zipEntries))
        {
            Console.WriteLine($"[提示] 这是 Windows 中文系统打的 zip（GBK 文件名，共 {zipEntries} 项），按 GBK 解码。");
            ExtractGbkZip(file, outDir);
        }
        else
        {
            // ★ 核心：Masuit.Tools 的文件解压封装，自动识别压缩包类型（zip / rar / 7z ...）
            new SevenZipCompressor(Http).Decompress(file, outDir, true);
        }
    }

    /// <summary>解压失败时不要留下一个空文件夹</summary>
    private static void CleanupEmptyDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }
        catch
        {
            // 删不掉就算了，不影响原始报错
        }
    }

    private static bool IsZip(string file) =>
        file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    private static bool IsTarLike(string file) =>
        file.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ||
        file.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
        file.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

    private static void ExtractTar(string file, string dir)
    {
        using var fs = File.OpenRead(file);
        if (file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gz, dir, true);
        }
        else
        {
            TarFile.ExtractToDirectory(fs, dir, true);
        }
    }

    /// <summary>去掉 .tar.gz / .tgz 这类复合扩展名，得到输出文件夹名</summary>
    private static string BaseName(string file)
    {
        var name = Path.GetFileName(file);
        foreach (var suffix in new[] { ".tar.gz", ".tar.bz2", ".tar.xz", ".tgz" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^suffix.Length];
            }
        }

        return Path.GetFileNameWithoutExtension(name);
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

    /// <summary>
    /// 判断是不是 GBK 命名的 zip：做法是拿同一份文件名分别按默认编码和 GBK 各解一遍，
    /// 默认解不出汉字、GBK 能解出汉字，就认定需要 GBK（Windows 中文系统打包的 zip 就是这样）。
    /// </summary>
    private static bool IsGbkZip(string file, out int count)
    {
        count = 0;
        if (!IsZip(file)) return false;

        try
        {
            using (var zip = ZipFile.Open(file, ZipArchiveMode.Read, null))
            {
                foreach (var entry in zip.Entries)
                {
                    count++;
                    if (HasCjk(entry.FullName)) return false;   // 默认就是正常中文，不用切
                }
            }

            using var gbkZip = ZipFile.Open(file, ZipArchiveMode.Read, Encoding.GetEncoding("GBK"));
            foreach (var entry in gbkZip.Entries)
            {
                if (HasCjk(entry.FullName)) return true;        // 只有按 GBK 解才有汉字
            }
        }
        catch
        {
            // 不是合法 zip（比如扩展名骗人），让主流程去处理
        }

        return false;
    }

    private static void ExtractGbkZip(string file, string dir)
    {
        using var zip = ZipFile.Open(file, ZipArchiveMode.Read, Encoding.GetEncoding("GBK"));
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;   // 目录项
            var dest = Path.GetFullPath(Path.Combine(dir, entry.FullName));
            if (!dest.StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[跳过] 越界路径：{entry.FullName}");
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, true);
        }
    }

    // ---------------------------------------------------------------- 列表

    private static int ShowList(string pathArg)
    {
        var file = FullPath(pathArg);
        if (!File.Exists(file))
        {
            Console.WriteLine($"[错误] 找不到文件：{file}");
            return 2;
        }

        if (!IsZip(file))
        {
            Console.WriteLine("[提示] 列表功能只支持 zip；rar/7z 等直接解压即可（解压会自动识别格式）。");
            return 0;
        }

        using var zip = ZipFile.Open(file, ZipArchiveMode.Read, IsGbkZip(file, out _) ? Encoding.GetEncoding("GBK") : null);
        Console.WriteLine($"{"大小",10}  路径");
        foreach (var entry in zip.Entries)
        {
            var size = string.IsNullOrEmpty(entry.Name) ? "<目录>" : Human(entry.Length);
            Console.WriteLine($"{size,10}  {entry.FullName}");
        }
        Console.WriteLine($"共 {zip.Entries.Count} 项。");
        return 0;
    }

    // ---------------------------------------------------------------- 压缩

    private static int CreateZip(string zipArg, IEnumerable<string> sources)
    {
        var target = FullPath(zipArg);
        if (!target.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) target += ".zip";

        var list = sources.Select(FullPath).Where(s => File.Exists(s) || Directory.Exists(s)).ToList();
        if (list.Count == 0)
        {
            Console.WriteLine("[错误] 没有找到要压缩的文件或文件夹。");
            return 2;
        }

        if (File.Exists(target))
        {
            Console.WriteLine($"[提示] 目标已存在，将覆盖：{target}");
            File.Delete(target);
        }

        var n = 0;
        var sw = Stopwatch.StartNew();
        using (var fs = File.Create(target))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var src in list)
            {
                if (Directory.Exists(src))
                {
                    var root = Path.GetDirectoryName(src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))!;
                    foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                    {
                        AddEntry(zip, Path.GetRelativePath(root, f), f);
                        n++;
                    }
                }
                else
                {
                    AddEntry(zip, Path.GetFileName(src), src);
                    n++;
                }
            }
        }
        sw.Stop();

        Console.WriteLine($"[完成] 已压缩 {n} 个文件 -> {target}（{Human(new FileInfo(target).Length)}，用时 {sw.Elapsed.TotalSeconds:F1} 秒）");
        return 0;
    }

    private static void AddEntry(ZipArchive zip, string entryName, string file)
    {
        var entry = zip.CreateEntry(entryName.Replace('\\', '/'), CompressionLevel.Optimal);
        using var dst = entry.Open();
        using var src = File.OpenRead(file);
        src.CopyTo(dst);
    }

    // ---------------------------------------------------------------- 杂项

    private static void Usage()
    {
        Console.WriteLine($"MasuitUnzip v{Version}  —— 基于 Masuit.Tools(码数吐司库) 的压缩包工具");
        Console.WriteLine();
        Console.WriteLine("用法：");
        Console.WriteLine("  MasuitUnzip.exe <压缩包>                       解压到 同名文件夹");
        Console.WriteLine("  MasuitUnzip.exe <压缩包> --here                解压到压缩包所在目录（不建子文件夹）");
        Console.WriteLine("  MasuitUnzip.exe <压缩包> -o <目录>              解压到指定目录");
        Console.WriteLine("  MasuitUnzip.exe -l <压缩包>                    只看内容（仅 zip）");
        Console.WriteLine("  MasuitUnzip.exe -z <输出.zip> <文件/文件夹...>   压缩");
        Console.WriteLine("  MasuitUnzip.exe -h                             看这份帮助");
        Console.WriteLine();
        Console.WriteLine("把压缩包直接拖到 exe 图标上，等同第一条用法。");
        Console.WriteLine("加 --no-pause（或 -n）不等待回车，方便脚本调用。");
    }

    private static string NeedArg(string[] args, int index)
    {
        if (index >= args.Length) throw new ArgumentException("参数不完整，用 -h 看用法。");
        return args[index];
    }

    private static string FullPath(string p) => Path.GetFullPath(p.Trim().Trim('"').Trim());

    private static void Fail(Exception ex)
    {
        var saved = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[失败] {ex.GetType().Name}: {ex.Message}");
        Console.ForegroundColor = saved;
        Console.WriteLine("常见原因：压缩包损坏 / 文件被占用 / 无写权限 / 压缩包有密码（本工具不支持密码）。");
    }

    private static string Human(long bytes)
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
