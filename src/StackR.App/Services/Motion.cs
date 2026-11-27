using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace StackR.App.Services;

/// <summary>
/// Animations discrètes (apparition en fondu glissé, élévation au survol, pulsation, barre de progression fluide).
/// Toutes sont désactivées si Windows a coupé les animations (<see cref="SystemParameters.ClientAreaAnimation"/>).
/// </summary>
public static class Motion
{
    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    public static readonly DependencyProperty AppearProperty = DependencyProperty.RegisterAttached(
        "Appear", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnAppearChanged));

    public static readonly DependencyProperty HoverLiftProperty = DependencyProperty.RegisterAttached(
        "HoverLift", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnHoverLiftChanged));

    public static readonly DependencyProperty PulseProperty = DependencyProperty.RegisterAttached(
        "Pulse", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnPulseChanged));

    public static readonly DependencyProperty SmoothValueProperty = DependencyProperty.RegisterAttached(
        "SmoothValue", typeof(double), typeof(Motion), new PropertyMetadata(0.0, OnSmoothValueChanged));

    /// <summary>Vrai si Windows autorise les animations.</summary>
    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    public static bool GetAppear(DependencyObject d) => (bool)d.GetValue(AppearProperty);

    public static void SetAppear(DependencyObject d, bool value) => d.SetValue(AppearProperty, value);

    public static bool GetHoverLift(DependencyObject d) => (bool)d.GetValue(HoverLiftProperty);

    public static void SetHoverLift(DependencyObject d, bool value) => d.SetValue(HoverLiftProperty, value);

    public static bool GetPulse(DependencyObject d) => (bool)d.GetValue(PulseProperty);

    public static void SetPulse(DependencyObject d, bool value) => d.SetValue(PulseProperty, value);

    public static double GetSmoothValue(DependencyObject d) => (double)d.GetValue(SmoothValueProperty);

    public static void SetSmoothValue(DependencyObject d, double value) => d.SetValue(SmoothValueProperty, value);

    // ===== Apparition : fondu + léger glissement vers le haut, en cascade dans une liste =====

    private static void OnAppearChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && e.NewValue is true)
        {
            element.Loaded += OnAppearLoaded;
        }
    }

    private static void OnAppearLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        element.Loaded -= OnAppearLoaded;
        if (!Enabled)
        {
            return;
        }

        var index = 0;
        if (ItemsControl.ItemsControlFromItemContainer(FindContainer(element)) is { } owner)
        {
            index = Math.Max(0, owner.ItemContainerGenerator.IndexFromContainer(FindContainer(element)));
        }

        var delay = TimeSpan.FromMilliseconds(Math.Min(index, 8) * 45);
        var translate = new TranslateTransform(0, 10);
        element.RenderTransform = translate;
        element.Opacity = 0;
        var duration = TimeSpan.FromMilliseconds(260);
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { BeginTime = delay, EasingFunction = Ease });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, duration) { BeginTime = delay, EasingFunction = Ease });
    }

    private static DependencyObject FindContainer(DependencyObject element)
    {
        var current = element;
        while (current is not null)
        {
            var parent = VisualTreeHelper.GetParent(current);
            if (parent is ContentPresenter presenter && ItemsControl.ItemsControlFromItemContainer(presenter) is not null)
            {
                return presenter;
            }

            if (parent is null || parent is ItemsControl)
            {
                break;
            }

            current = parent;
        }

        return element;
    }

    // ===== Élévation au survol : ombre plus marquée et léger soulèvement =====

    private static void OnHoverLiftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true)
        {
            return;
        }

        element.MouseEnter += (_, _) => Lift(element, true);
        element.MouseLeave += (_, _) => Lift(element, false);
    }

    private static void Lift(FrameworkElement element, bool up)
    {
        var duration = TimeSpan.FromMilliseconds(Enabled ? 140 : 0);
        if (element.Effect is DropShadowEffect shadow)
        {
            if (shadow.IsFrozen)
            {
                shadow = shadow.Clone();
                element.Effect = shadow;
            }

            shadow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(up ? 0.16 : 0.07, duration) { EasingFunction = Ease });
            shadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(up ? 22 : 12, duration) { EasingFunction = Ease });
        }

        if (element.RenderTransform is not TranslateTransform translate || translate.IsFrozen)
        {
            translate = new TranslateTransform();
            element.RenderTransform = translate;
        }

        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(up ? -2 : 0, duration) { EasingFunction = Ease });
    }

    // ===== Pulsation douce (démarrage / arrêt en cours) =====

    private static void OnPulseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if (e.NewValue is true && Enabled)
        {
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(650))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        }
        else
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
        }
    }

    // ===== Barre de progression fluide =====

    private static void OnSmoothValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.Primitives.RangeBase range)
        {
            return;
        }

        var target = (double)e.NewValue;
        if (!Enabled || target < range.Value)
        {
            range.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
            range.Value = target;
            return;
        }

        range.BeginAnimation(
            System.Windows.Controls.Primitives.RangeBase.ValueProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(300)) { EasingFunction = Ease });
    }
}
