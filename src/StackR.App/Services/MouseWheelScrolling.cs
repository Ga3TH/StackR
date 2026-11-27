using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace StackR.App.Services;

/// <summary>
/// Molette de la souris sur toute l'application : le défilement va au <see cref="ScrollViewer"/> le plus proche qui peut
/// encore défiler dans le sens demandé. Une zone imbriquée (journal, liste, texte) arrivée en butée, ou sans rien à
/// défiler, laisse donc la main à la page au lieu d'avaler la molette.
/// </summary>
internal static class MouseWheelScrolling
{
    private const double PixelsPerNotch = 56;
    private const int LinesPerNotch = 3;
    private static bool _registered;

    /// <summary>Enregistre le gestionnaire de classe (une seule fois par processus).</summary>
    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        EventManager.RegisterClassHandler(typeof(UIElement), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewMouseWheel));
    }

    internal static ScrollViewer? FindScroller(DependencyObject? start, int delta)
    {
        for (var node = start; node is not null; node = Parent(node))
        {
            if (node is ScrollViewer sv && CanScroll(sv, delta))
            {
                return sv;
            }
        }

        return null;
    }

    internal static bool CanScroll(ScrollViewer sv, int delta) =>
        sv.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled
        && sv.ScrollableHeight > 0
        && (delta > 0 ? sv.VerticalOffset > 0 : sv.VerticalOffset < sv.ScrollableHeight);

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0 || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (FindScroller(e.OriginalSource as DependencyObject, e.Delta) is not { } scroller)
        {
            return;
        }

        var notches = Math.Max(1, Math.Abs(e.Delta) / 120);
        if (scroller.CanContentScroll)
        {
            // Défilement par éléments (liste virtualisée) : on laisse le contenu choisir son pas.
            for (var i = 0; i < notches * LinesPerNotch; i++)
            {
                if (e.Delta > 0)
                {
                    scroller.LineUp();
                }
                else
                {
                    scroller.LineDown();
                }
            }
        }
        else
        {
            scroller.ScrollToVerticalOffset(scroller.VerticalOffset - Math.Sign(e.Delta) * notches * PixelsPerNotch);
        }

        e.Handled = true;
    }

    private static DependencyObject? Parent(DependencyObject node)
    {
        if (node is Visual or Visual3D)
        {
            return VisualTreeHelper.GetParent(node) ?? (node as FrameworkElement)?.Parent;
        }

        return node is FrameworkContentElement content ? content.Parent : LogicalTreeHelper.GetParent(node);
    }
}
