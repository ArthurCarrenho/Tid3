using System;
using System.Windows.Input;

namespace TidalUi3.Services;

public sealed class RelayCommand(Action execute) : ICommand
{
    // Always executable, so there is never a change to announce.
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
