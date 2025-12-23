using System;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.SystemService;

namespace Bakabase.Tests.Implementations;

public class TestSystemService : ISystemService
{
    public UiTheme UiTheme => UiTheme.Light;
    public string? Language => "en-US";
    public event Func<UiTheme, Task>? OnUiThemeChange;
}
