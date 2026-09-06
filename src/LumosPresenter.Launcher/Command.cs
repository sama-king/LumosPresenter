using System.Windows.Input;

namespace LumosPresenter.Launcher;

/// <summary>
/// Minimal ICommand for menu items. The launcher has no view models and no MVVM
/// framework, so a delegate wrapper is all the tray menu needs.
/// </summary>
public sealed class Command(Action execute) : ICommand
{
    // Nothing here ever changes its executability; the event exists to satisfy ICommand.
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}
