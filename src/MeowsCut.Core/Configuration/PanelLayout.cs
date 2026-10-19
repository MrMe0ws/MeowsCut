namespace MeowsCut.Core.Configuration;

/// <summary>
/// Размеры окна и его панелей, которые пользователь выставил сам. Без этого
/// каждый запуск начинался с заводской раскладки, и доску, колонку свойств
/// и окно приходилось растягивать заново.
/// </summary>
/// <param name="BoardHeight">Высота доски монтажа вместе с её панелью инструментов.</param>
/// <param name="SideWidth">Ширина колонки свойств справа от кадра.</param>
/// <param name="SourcesShare">
/// Доля списка файлов в высоте правой колонки, от 0 до 1. С версии 2.0 файлы живут
/// в своей панели слева, и поле только переносится как было — для отката на 1.x.
/// </param>
/// <param name="WindowWidth">Ширина окна в обычном, не развёрнутом состоянии.</param>
/// <param name="WindowHeight">Высота окна в обычном состоянии.</param>
/// <param name="Maximized">Окно было развёрнуто на весь экран.</param>
/// <param name="MediaWidth">
/// Ширина панели медиа слева от кадра (версия 2.0). 0 — заводская: в настройках
/// от прошлых версий этого поля нет.
/// </param>
/// <param name="SoundListHeight">
/// Высота раздела «Звук» в панели медиа. 0 — заводская: раздел растёт по списку.
/// </param>
public sealed record PanelLayout(
    double BoardHeight,
    double SideWidth,
    double SourcesShare,
    double WindowWidth,
    double WindowHeight,
    bool Maximized,
    double MediaWidth = 0,
    double SoundListHeight = 0);
