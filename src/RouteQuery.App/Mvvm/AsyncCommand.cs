using System.Windows.Input;

namespace RouteQuery.App.Mvvm;

/// <summary>
/// 异步命令。三条来自 SPEC-004 四节的要求：<b>可取消</b>、<b>执行期间不可重复触发</b>、
/// <b>不吞异常</b>（异常上抛由界面统一分类成文案，而不是静默失败）。
/// </summary>
public sealed class AsyncCommand(Func<CancellationToken, Task> execute) : ICommand
{
    private readonly Func<CancellationToken, Task> _execute = execute;
    private CancellationTokenSource? _cts;
    private bool _running;

    public event EventHandler? CanExecuteChanged;

    public bool IsRunning
    {
        get => _running;
        private set { _running = value; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }

    public bool CanExecute(object? parameter) => !_running;

    public async void Execute(object? parameter)
    {
        if (_running) return;

        _cts = new CancellationTokenSource();
        IsRunning = true;
        try
        {
            await _execute(_cts.Token);
        }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    /// <summary>由"取消"按钮调用。取消后 <see cref="Execute"/> 走 finally 复位，界面上的加载态也随之消失。</summary>
    public void Cancel() => _cts?.Cancel();
}
