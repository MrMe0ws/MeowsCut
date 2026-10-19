namespace MeowsCut.Core.Configuration;

/// <summary>
/// Размеры окна и его панелей, которые пользователь выставил сам. Без этого
/// каждый запуск начинался с заводской раскладки, и доску, колонку свойств
/// и окно приходилось растягивать заново.
/// </summary>
/// <param name="BoardHeight">Высота доски монтажа вместе с её панелью инструментов.</param>
/// <param name="SideWidth">Ширина колонки файлов и свойств справа от кадра.</param>
/// <param name="SourcesShare">Доля списка файлов в высоте этой колонки, от 0 до 1.</param>
/// <param name="WindowWidth">Ширина окна в обычном, не развёрнутом состоянии.</param>
/// <param name="WindowHeight">Высота окна в обычном состоянии.</param>
/// <param name="Maximized">Окно было развёрнуто на весь экран.</param>
public sealed record PanelLayout(
    double BoardHeight,
    double SideWidth,
    double SourcesShare,
    double WindowWidth,
    double WindowHeight,
    bool Maximized);
