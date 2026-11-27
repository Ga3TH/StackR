using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace StackR.App.Views;

/// <summary>Bouton Fluent avec, dessous, une courte phrase qui dit ce qu'il fait (reprise en info-bulle).</summary>
public partial class CaptionedButton : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(object), typeof(CaptionedButton));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(IconElement), typeof(CaptionedButton));

    public static readonly DependencyProperty AppearanceProperty = DependencyProperty.Register(
        nameof(Appearance), typeof(ControlAppearance), typeof(CaptionedButton), new PropertyMetadata(ControlAppearance.Secondary));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(CaptionedButton));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(CaptionedButton));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(CaptionedButton));

    public static readonly DependencyProperty CaptionWidthProperty = DependencyProperty.Register(
        nameof(CaptionWidth), typeof(double), typeof(CaptionedButton), new PropertyMetadata(220.0));

    public static readonly DependencyProperty IsDefaultProperty = DependencyProperty.Register(
        nameof(IsDefault), typeof(bool), typeof(CaptionedButton));

    static CaptionedButton()
    {
        HorizontalAlignmentProperty.OverrideMetadata(typeof(CaptionedButton), new FrameworkPropertyMetadata(HorizontalAlignment.Left));
        VerticalAlignmentProperty.OverrideMetadata(typeof(CaptionedButton), new FrameworkPropertyMetadata(VerticalAlignment.Top));
    }

    public CaptionedButton() => InitializeComponent();

    /// <summary>Contenu du bouton (texte ou panneau icône + texte).</summary>
    public object? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public IconElement? Icon
    {
        get => (IconElement?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public ControlAppearance Appearance
    {
        get => (ControlAppearance)GetValue(AppearanceProperty);
        set => SetValue(AppearanceProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public string? Caption
    {
        get => (string?)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    /// <summary>Bouton activé par la touche Entrée.</summary>
    public bool IsDefault
    {
        get => (bool)GetValue(IsDefaultProperty);
        set => SetValue(IsDefaultProperty, value);
    }

    /// <summary>Largeur maximale de la légende (elle passe à la ligne au-delà).</summary>
    public double CaptionWidth
    {
        get => (double)GetValue(CaptionWidthProperty);
        set => SetValue(CaptionWidthProperty, value);
    }
}
