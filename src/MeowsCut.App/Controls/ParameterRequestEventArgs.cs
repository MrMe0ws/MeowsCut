using System.Windows;
using MeowsCut.App.ViewModels;

namespace MeowsCut.App.Controls;

/// <summary>Просьба открыть чип параметра у куска: какой параметр и где кусок на доске.</summary>
public sealed class ParameterRequestEventArgs(ClipParameter parameter, ContextTargetKind kind, Rect anchor) : EventArgs
{
    public ClipParameter Parameter { get; } = parameter;

    /// <summary>Видео, звук или надпись — от этого зависит, чьи свойства правит чип.</summary>
    public ContextTargetKind Kind { get; } = kind;

    /// <summary>Видимая часть куска в координатах доски: над ней встаёт чип.</summary>
    public Rect Anchor { get; } = anchor;
}
