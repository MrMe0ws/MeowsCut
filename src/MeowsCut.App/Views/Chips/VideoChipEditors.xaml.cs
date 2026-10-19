using System.Windows;
using System.Windows.Controls;
using MeowsCut.App.Controls;

namespace MeowsCut.App.Views.Chips;

/// <summary>Редакторы параметров для чипа на доске; виден тот, что выбрали в меню.</summary>
public partial class VideoChipEditors : UserControl
{
    public static readonly DependencyProperty ParameterProperty = DependencyProperty.Register(
        nameof(Parameter), typeof(ClipParameter), typeof(VideoChipEditors), new PropertyMetadata(ClipParameter.Speed));

    public VideoChipEditors() => InitializeComponent();

    public ClipParameter Parameter
    {
        get => (ClipParameter)GetValue(ParameterProperty);
        set => SetValue(ParameterProperty, value);
    }
}
