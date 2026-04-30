using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Bakabase.Infrastructures.Components.App.Relocation;

namespace Bakabase.Windows;

public partial class RelocationSplashWindow : Window
{
    private TextBlock _titleText = null!;
    private TextBlock _phaseText = null!;
    private TextBlock _currentFileText = null!;
    private ProgressBar _progress = null!;

    private readonly Dictionary<string, string> _strings;

    public RelocationSplashWindow() : this("en-US") { }

    public RelocationSplashWindow(string language)
    {
        InitializeComponent();
        _titleText = this.FindControl<TextBlock>("TitleText")!;
        _phaseText = this.FindControl<TextBlock>("PhaseText")!;
        _currentFileText = this.FindControl<TextBlock>("CurrentFileText")!;
        _progress = this.FindControl<ProgressBar>("Progress")!;

        _strings = SelectLanguage(language);
        _titleText.Text = _strings["title"];
        _phaseText.Text = _strings["starting"];
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public void UpdateProgress(RelocationProgress p)
    {
        var phaseKey = p.Phase switch
        {
            RelocationPhase.Starting => "starting",
            RelocationPhase.Copying => "copying",
            RelocationPhase.Validating => "validating",
            RelocationPhase.Replacing => "replacing",
            RelocationPhase.Finalizing => "finalizing",
            RelocationPhase.Done => "done",
            _ => "starting",
        };

        if (p.Phase == RelocationPhase.Copying && p.TotalFiles > 0)
        {
            _phaseText.Text = string.Format(
                _strings["copyingFmt"], p.ProcessedFiles, p.TotalFiles);
            _progress.Value = p.FilesFraction * 100d;
        }
        else
        {
            _phaseText.Text = _strings[phaseKey];
            _progress.IsIndeterminate = p.Phase != RelocationPhase.Done;
        }

        _currentFileText.Text = p.CurrentFile ?? string.Empty;
    }

    /// <summary>
    /// Splash blocks the close button — only the runner closes it.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
    }

    private static Dictionary<string, string> SelectLanguage(string language)
    {
        return language?.ToLowerInvariant() switch
        {
            "cn" or "zh-cn" or "zh-hans" or "zh-hans-cn" => ZhCn,
            _ => EnUs,
        };
    }

    private static readonly Dictionary<string, string> ZhCn = new()
    {
        ["title"] = "正在迁移数据，请勿关闭窗口",
        ["starting"] = "正在准备...",
        ["copying"] = "正在复制文件...",
        ["copyingFmt"] = "正在复制 {0}/{1} 个文件...",
        ["validating"] = "正在校验...",
        ["replacing"] = "正在替换目标内容...",
        ["finalizing"] = "正在切换路径...",
        ["done"] = "完成",
    };

    private static readonly Dictionary<string, string> EnUs = new()
    {
        ["title"] = "Migrating data — do not close",
        ["starting"] = "Preparing…",
        ["copying"] = "Copying files…",
        ["copyingFmt"] = "Copying {0}/{1} files…",
        ["validating"] = "Validating…",
        ["replacing"] = "Replacing target contents…",
        ["finalizing"] = "Switching path…",
        ["done"] = "Done",
    };
}

