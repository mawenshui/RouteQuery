using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RouteQuery.App.Mvvm;

/// <summary>最小的可通知基类。手写而不再引一个 MVVM 包——这个应用需要的只有"属性变了通知一下"。</summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>一次改动牵连多个派生属性时用这个，避免逐个手写 Raise 漏掉一个界面就不动。</summary>
    protected void Raise(params string[] names)
    {
        foreach (var name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}
