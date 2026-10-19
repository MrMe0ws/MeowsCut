using System.Windows;
using System.Windows.Controls;
using MeowsCut.App.Controls;

namespace MeowsCut.App.Views.Chips;

/// <summary>Редакторы параметров для чипа на доске; виден тот, что выбрали в меню.</summary>
public partial class TitleChipEditors : UserControl
{
    public static readonly DependencyProperty ParameterProperty = DependencyProperty.Register(
        nameof(Parameter), typeof(ClipParameter), typeof(TitleChipEditors), new PropertyMetadata(ClipParameter.Speed));

    public TitleChipEditors() => InitializeComponent();

    public ClipParameter Parameter
    {
        get => (ClipParameter)GetValue(ParameterProperty);
        set => SetValue(ParameterProperty, value);
    }
}
