using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.SystemService;
using System.Threading.Tasks;
using System;

namespace Bakabase.Service.Components;

public class NullSystemService : ISystemService
{
    public UiTheme UiTheme => UiTheme.FollowSystem;
    public string? Language => null;
    public event Func<UiTheme, Task>? OnUiThemeChange;
}