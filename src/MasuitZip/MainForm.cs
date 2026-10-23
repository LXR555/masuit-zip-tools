using System.ComponentModel;
using System.Diagnostics;

namespace MasuitZip;

public sealed class MainForm : Form
{
    private static readonly Color Accent = Color.FromArgb(47, 111, 237);
    private static readonly Color AccentHover = Color.FromArgb(35, 92, 205);

    private readonly ListBox _list = new();
    private readonly TextBox _name = new();
    private readonly TextBox _outDir = new();
    private readonly TextBox _archive = new();
    private readonly TextBox _extractTo = new();
    private readonly ProgressBar _bar = new();
    private readonly Label _status = new();
    private readonly Button _openOut = new();
    private readonly List<Control> _lockWhileBusy = new();

    private string _lastOutput = string.Empty;
    private bool _busy;

    public MainForm()
    {
        BuildUi();
    }

    // ---------------------------------------------------------------- 界面

    private void BuildUi()
    {
        Text = "一键压缩 · 拖进来就行";
        Font = new Font("微软雅黑", 9F);
        ClientSize = new Size(648, 512);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AllowDrop = true;
        BackColor = Color.FromArgb(248, 249, 251);

        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        // ---------- ① 压缩 ----------
        var gbZip = new GroupBox
        {
            Text = "① 压缩",
            Location = new Point(12, 8),
            Size = new Size(624, 300),
            ForeColor = Color.FromArgb(60, 66, 80)
        };

        var lblDrag = new Label
        {
            Text = "把文件 / 文件夹拖进下面的白框（可拖多个），或者用右边的按钮添加",
            Location = new Point(14, 22),
            Size = new Size(440, 18),
            ForeColor = Color.FromArgb(110, 118, 132)
        };

        _list.Location = new Point(14, 44);
        _list.Size = new Size(428, 140);
        _list.AllowDrop = true;
        _list.DragEnter += OnDragEnter;
        _list.DragDrop += OnDragDrop;
        _list.HorizontalScrollbar = true;
        _list.IntegralHeight = false;

        var btnAddFiles = MakeButton("添加文件…", new Point(452, 44), new Size(158, 32));
        btnAddFiles.Click += (_, _) => PickFiles();

        var btnAddFolder = MakeButton("添加文件夹…", new Point(452, 82), new Size(158, 32));
        btnAddFolder.Click += (_, _) => PickFolder();

        var btnRemove = MakeButton("移除选中", new Point(452, 120), new Size(158, 32));
        btnRemove.Click += (_, _) => RemoveSelected();

        var btnClear = MakeButton("清空列表", new Point(452, 158), new Size(158, 32));
        btnClear.Click += (_, _) =>
        {
            _list.Items.Clear();
            _name.Text = string.Empty;
        };

        var lblName = new Label { Text = "压缩包名称", Location = new Point(14, 198), Size = new Size(80, 20) };
        _name.Location = new Point(98, 194);
        _name.Size = new Size(258, 26);

        var lblZipExt = new Label { Text = ".zip", Location = new Point(362, 198), Size = new Size(40, 20) };

        var lblOut = new Label { Text = "保存到", Location = new Point(14, 234), Size = new Size(80, 20) };
        _outDir.Location = new Point(98, 230);
        _outDir.Size = new Size(338, 26);
        _outDir.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        var btnBrowseOut = MakeButton("浏览…", new Point(444, 229), new Size(80, 28));
        btnBrowseOut.Click += (_, _) => PickOutDir();

        var btnZip = MakeButton("开始压缩", new Point(14, 264), new Size(510, 32), primary: true);
        btnZip.Click += OnCompress;

        gbZip.Controls.AddRange(new Control[]
        {
            lblDrag, _list, btnAddFiles, btnAddFolder, btnRemove, btnClear,
            lblName, _name, lblZipExt, lblOut, _outDir, btnBrowseOut, btnZip
        });

        // ---------- ② 解压 ----------
        var gbUnzip = new GroupBox
        {
            Text = "② 解压",
            Location = new Point(12, 316),
            Size = new Size(624, 132),
            ForeColor = Color.FromArgb(60, 66, 80)
        };

        var lblArchive = new Label { Text = "压缩包", Location = new Point(14, 28), Size = new Size(60, 20) };
        _archive.Location = new Point(78, 24);
        _archive.Size = new Size(368, 26);
        _archive.ReadOnly = true;
        _archive.BackColor = Color.White;

        var btnBrowseArchive = MakeButton("选择…", new Point(452, 23), new Size(80, 28));
        btnBrowseArchive.Click += (_, _) => PickArchive();

        var lblTo = new Label { Text = "解压到", Location = new Point(14, 64), Size = new Size(60, 20) };
        _extractTo.Location = new Point(78, 60);
        _extractTo.Size = new Size(368, 26);

        var btnBrowseTo = MakeButton("浏览…", new Point(452, 59), new Size(80, 28));
        btnBrowseTo.Click += (_, _) => PickExtractDir();

        var btnUnzip = MakeButton("开始解压", new Point(366, 94), new Size(166, 32), primary: true);
        btnUnzip.Click += OnExtract;

        var hintUnzip = new Label
        {
            Text = "默认解压到压缩包旁边的同名文件夹\r\n支持 zip / rar / 7z / tar.gz / gz",
            Location = new Point(14, 94),
            Size = new Size(340, 34),
            ForeColor = Color.FromArgb(150, 156, 168)
        };

        gbUnzip.Controls.AddRange(new Control[]
        {
            lblArchive, _archive, btnBrowseArchive, lblTo, _extractTo, btnBrowseTo, btnUnzip, hintUnzip
        });

        // ---------- 底部 ----------
        _bar.Location = new Point(14, 458);
        _bar.Size = new Size(420, 16);
        _bar.Minimum = 0;
        _bar.Maximum = 100;

        _openOut.Text = "打开输出位置";
        _openOut.Location = new Point(508, 452);
        _openOut.Size = new Size(128, 28);
        _openOut.Enabled = false;
        _openOut.FlatStyle = FlatStyle.System;
        _openOut.Click += (_, _) => OpenOutput();

        _status.Location = new Point(14, 482);
        _status.Size = new Size(622, 20);
        _status.ForeColor = Color.FromArgb(110, 118, 132);
        _status.Text = "准备就绪。";

        Controls.AddRange(new Control[] { gbZip, gbUnzip, _bar, _openOut, _status });

        _lockWhileBusy.AddRange(new Control[] { btnAddFiles, btnAddFolder, btnRemove, btnClear, btnZip, btnBrowseOut, btnBrowseArchive, btnBrowseTo, btnUnzip });
    }

    private static Button MakeButton(string text, Point location, Size size, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            Location = location,
            Size = size,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = !primary,
            Cursor = Cursors.Hand
        };

        if (primary)
        {
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = Accent;
            b.ForeColor = Color.White;
            b.Font = new Font("微软雅黑", 9.5F, FontStyle.Bold);
            b.FlatAppearance.MouseOverBackColor = AccentHover;
            b.FlatAppearance.MouseDownBackColor = AccentHover;
        }
        else
        {
            b.FlatAppearance.BorderColor = Color.FromArgb(206, 212, 222);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 242, 250);
        }

        return b;
    }

    // ---------------------------------------------------------------- 拖放

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effect = DragDropEffects.Copy;
        }
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (_busy) return;
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths) return;

        AddPaths(paths);

        // 如果拖进来的是单个压缩包，顺手把解压区也填上
        if (paths.Length == 1 && File.Exists(paths[0]) && IsArchive(paths[0]) && string.IsNullOrWhiteSpace(_archive.Text))
        {
            _archive.Text = paths[0];
            _extractTo.Text = DefaultExtractDir(paths[0]);
        }
    }

    private static bool IsArchive(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".tgz" or ".bz2" or ".xz" or ".iso";
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var existing = _list.Items.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            if (!File.Exists(p) && !Directory.Exists(p)) continue;
            if (existing.Add(p)) _list.Items.Add(p);
        }

        if (string.IsNullOrWhiteSpace(_name.Text) && _list.Items.Count > 0)
        {
            _name.Text = SuggestName(_list.Items[0] as string ?? string.Empty);
        }

        _status.Text = $"已选 {_list.Items.Count} 项。";
    }

    private static string SuggestName(string path)
    {
        if (Directory.Exists(path)) return new DirectoryInfo(path).Name;
        return Path.GetFileNameWithoutExtension(path);
    }

    // ---------------------------------------------------------------- 选择

    private void PickFiles()
    {
        using var dlg = new OpenFileDialog { Multiselect = true, Title = "选择要压缩的文件" };
        if (dlg.ShowDialog(this) == DialogResult.OK) AddPaths(dlg.FileNames);
    }

    private void PickFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "选择要压缩的文件夹", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
        {
            AddPaths(new[] { dlg.SelectedPath });
        }
    }

    private void PickOutDir()
    {
        using var dlg = new FolderBrowserDialog { Description = "压缩包保存到哪个文件夹", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
        {
            _outDir.Text = dlg.SelectedPath;
        }
    }

    private void PickArchive()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "选择要解压的压缩包",
            Filter = "压缩包|*.zip;*.rar;*.7z;*.tar;*.gz;*.tgz;*.bz2;*.xz|所有文件|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _archive.Text = dlg.FileName;
        _extractTo.Text = DefaultExtractDir(dlg.FileName);
    }

    private void PickExtractDir()
    {
        using var dlg = new FolderBrowserDialog { Description = "解压到哪个文件夹", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
        {
            _extractTo.Text = dlg.SelectedPath;
        }
    }

    private static string DefaultExtractDir(string archive)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(archive)) ?? Environment.CurrentDirectory;
        return Path.Combine(parent, ZipEngine.BaseName(archive));
    }

    private void RemoveSelected()
    {
        var selected = _list.SelectedItems.Cast<object>().ToList();
        foreach (var item in selected) _list.Items.Remove(item);
    }

    // ---------------------------------------------------------------- 压缩

    private async void OnCompress(object? sender, EventArgs e)
    {
        if (_busy) return;

        if (_list.Items.Count == 0)
        {
            MessageBox.Show(this, "先添加要压缩的文件或文件夹（也可以直接把文件拖进窗口）。", "还没有内容",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var name = _name.Text.Trim();
        if (name.Length == 0) name = SuggestName(_list.Items[0] as string ?? "压缩包");
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) name = name[..^4];

        foreach (var bad in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(bad, '_');
        }

        if (name.Length == 0) name = "压缩包";

        var dir = _outDir.Text.Trim();
        if (dir.Length == 0) dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        var zipPath = Path.Combine(dir, name + ".zip");
        if (File.Exists(zipPath) &&
            MessageBox.Show(this, $"已存在同名文件，覆盖吗？\n{zipPath}", "确认覆盖",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var sources = _list.Items.Cast<string>().ToList();
        var progress = new Progress<ProgressInfo>(UpdateProgress);

        BeginWork($"正在压缩到 {name}.zip …");
        try
        {
            var result = await Task.Run(() => ZipEngine.Compress(sources, zipPath, progress));

            _bar.Style = ProgressBarStyle.Continuous;
            _bar.Value = 100;
            _lastOutput = Path.GetDirectoryName(zipPath) ?? dir;
            _openOut.Enabled = true;
            _status.Text = $"压缩完成：{result.files} 个文件 → {Path.GetFileName(zipPath)}（{ZipEngine.Size(new FileInfo(zipPath).Length)}，用时 {result.elapsed.TotalSeconds:F1} 秒）";

            MessageBox.Show(this,
                $"压缩完成\n\n压缩包：{Path.GetFileName(zipPath)}\n文件数：{result.files} 个\n大小：{ZipEngine.Size(new FileInfo(zipPath).Length)}\n用时：{result.elapsed.TotalSeconds:F1} 秒\n位置：{_lastOutput}",
                "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            EndWork();
        }
    }

    // ---------------------------------------------------------------- 解压

    private async void OnExtract(object? sender, EventArgs e)
    {
        if (_busy) return;

        var archive = _archive.Text.Trim();
        if (archive.Length == 0)
        {
            MessageBox.Show(this, "先选择要解压的压缩包。", "还没有压缩包",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!File.Exists(archive))
        {
            MessageBox.Show(this, $"找不到这个文件：\n{archive}", "文件不存在",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var outDir = _extractTo.Text.Trim();
        if (outDir.Length == 0) outDir = DefaultExtractDir(archive);

        var progress = new Progress<ProgressInfo>(UpdateProgress);

        BeginWork($"正在解压 {Path.GetFileName(archive)} …");
        try
        {
            var result = await Task.Run(() => ZipEngine.Extract(archive, outDir, progress));

            _bar.Style = ProgressBarStyle.Continuous;
            _bar.Value = 100;
            _lastOutput = outDir;
            _openOut.Enabled = true;
            _status.Text = $"解压完成：{result.files} 个文件 → {outDir}（用时 {result.elapsed.TotalSeconds:F1} 秒）";

            MessageBox.Show(this,
                $"解压完成\n\n文件数：{result.files} 个\n用时：{result.elapsed.TotalSeconds:F1} 秒\n位置：{outDir}",
                "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            EndWork();
        }
    }

    // ---------------------------------------------------------------- 状态

    private void UpdateProgress(ProgressInfo p)
    {
        if (p.Percent < 0)
        {
            _bar.Style = ProgressBarStyle.Marquee;
            _bar.MarqueeAnimationSpeed = 30;
        }
        else
        {
            _bar.Style = ProgressBarStyle.Continuous;
            _bar.Value = Math.Clamp(p.Percent, 0, 100);
        }

        _status.Text = p.Message;
    }

    private void BeginWork(string message)
    {
        _busy = true;
        foreach (var c in _lockWhileBusy) c.Enabled = false;
        _openOut.Enabled = false;
        _bar.Style = ProgressBarStyle.Continuous;
        _bar.Value = 0;
        _status.ForeColor = Color.FromArgb(110, 118, 132);
        _status.Text = message;
        Cursor = Cursors.WaitCursor;
    }

    private void EndWork()
    {
        _busy = false;
        foreach (var c in _lockWhileBusy) c.Enabled = true;
        Cursor = Cursors.Default;
    }

    private void Fail(Exception ex)
    {
        _bar.Style = ProgressBarStyle.Continuous;
        _bar.Value = 0;
        _status.ForeColor = Color.FromArgb(200, 40, 40);
        _status.Text = $"失败：{ex.Message}";
        MessageBox.Show(this,
            $"操作失败：{ex.Message}\n\n常见原因：文件被占用、没有写入权限、压缩包损坏或有密码。",
            "出错了", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void OpenOutput()
    {
        if (string.IsNullOrEmpty(_lastOutput) || !Directory.Exists(_lastOutput)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_lastOutput}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"打不开文件夹：{ex.Message}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy)
        {
            var r = MessageBox.Show(this, "正在处理中，确定要强行退出吗？", "确认退出",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) e.Cancel = true;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var c in _lockWhileBusy) c.Dispose();
            _list.Dispose();
            _bar.Dispose();
            _openOut.Dispose();
            _status.Dispose();
        }

        base.Dispose(disposing);
    }
}
