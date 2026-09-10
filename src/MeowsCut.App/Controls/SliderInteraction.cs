using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MeowsCut.App.Controls;

/// <summary>
/// Сообщает модели, что ползунок отпустили.
/// </summary>
/// <remarks>
/// Правки, идущие подряд, склеиваются в одну запись истории — иначе Ctrl+Z отматывал бы
/// движение мыши по пикселю. Но склейка обязана где-то заканчиваться, иначе вся работа
/// со звуком за сеанс превращается в одну запись, а отмена возвращает клип к исходному
/// состоянию. Конец — момент, когда пользователь отпустил ползунок.
///
/// Ловится и перетаскивание бегунка, и щелчок по дорожке: значение меняется и так, и так.
/// </remarks>
public static class SliderInteraction
{
    public static readonly DependencyProperty EndCommandProperty =
        DependencyProperty.RegisterAttached(
            "EndCommand",
            typeof(ICommand),
            typeof(SliderInteraction),
            new PropertyMetadata(null, OnEndCommandChanged));

    public static void SetEndCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(EndCommandProperty, value);

    public static ICommand? GetEndCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(EndCommandProperty);

    private static void OnEndCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Slider slider)
        {
            return;
        }

        slider.RemoveHandler(Thumb.DragCompletedEvent, (DragCompletedEventHandler)OnDragCompleted);
        slider.PreviewMouseUp -= OnMouseUp;
        slider.PreviewKeyUp -= OnKeyUp;

        if (e.NewValue is not ICommand)
        {
            return;
        }

        slider.AddHandler(Thumb.DragCompletedEvent, (DragCompletedEventHandler)OnDragCompleted);
        slider.PreviewMouseUp += OnMouseUp;
        slider.PreviewKeyUp += OnKeyUp;
    }

    private static void OnDragCompleted(object sender, DragCompletedEventArgs e) => Fire(sender);

    private static void OnMouseUp(object sender, MouseButtonEventArgs e) => Fire(sender);

    private static void OnKeyUp(object sender, KeyEventArgs e) => Fire(sender);

    private static void Fire(object sender)
    {
        if (sender is DependencyObject element &&
            GetEndCommand(element) is { } command &&
            command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
