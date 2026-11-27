using System.Collections.ObjectModel;
using System.Windows;
using StackR.App.Resources;
using StackR.Core;
using StackR.Core.Manifests;
using Wpf.Ui.Controls;

namespace StackR.App.ViewModels;

/// <summary>Page « Versions » : une section par composant avec ses versions installables, activables, désinstallables.</summary>
public sealed class VersionsViewModel : PageViewModelBase
{
    public VersionsViewModel(StackREngine engine)
        : base(Strings.NavVersions, Strings.VersionsSubtitle)
    {
        ArgumentNullException.ThrowIfNull(engine);
        Sections =
        [
            new(engine, ComponentKind.Apache, "Apache", Strings.VerDescApache, SymbolRegular.Server24, Confirm),
            new(engine, ComponentKind.Php, "PHP", Strings.VerDescPhp, SymbolRegular.Code24, Confirm),
            new(engine, ComponentKind.MySql, "MySQL", Strings.VerDescMySql, SymbolRegular.Database24, Confirm),
            new(engine, ComponentKind.Mailpit, "Mailpit", Strings.VerDescMailpit, SymbolRegular.Mail24, Confirm),
            new(engine, ComponentKind.Node, "Node.js", Strings.VerDescNode, SymbolRegular.Cube24, Confirm),
            new(engine, ComponentKind.Composer, "Composer", Strings.VerDescComposer, SymbolRegular.Box24, Confirm),
            new(engine, ComponentKind.HeidiSql, "HeidiSQL", Strings.VerDescHeidiSql, SymbolRegular.Window24, Confirm),
            new(engine, ComponentKind.Adminer, "Adminer", Strings.VerDescAdminer, SymbolRegular.DatabaseSearch24, Confirm),
        ];
    }

    public ObservableCollection<ComponentSectionViewModel> Sections { get; }

    /// <summary>Relit l'état des composants (à l'affichage de la page).</summary>
    public void Refresh()
    {
        foreach (var section in Sections)
        {
            section.Refresh();
        }
    }

    private static bool Confirm(string title, string body) =>
        System.Windows.MessageBox.Show(body, title, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;
}
