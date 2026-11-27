using Microsoft.Extensions.DependencyInjection;
using StackR.App.ViewModels;
using StackR.App.Views;
using StackR.Core;
using StackR.Core.Services;

namespace StackR.Screens;

/// <summary>Données factices injectées dans les modèles de vue (états de services, bases…) pour les captures.</summary>
internal static class Preview
{
    public static void Apply(IServiceProvider provider, StackREngine engine)
    {
        _ = engine;
        var now = DateTimeOffset.Now;
        var dashboard = provider.GetRequiredService<DashboardViewModel>();
        dashboard.IsPreview = true;
        var database = provider.GetRequiredService<DatabaseViewModel>();
        database.IsPreview = true;
        database.RefreshTools();
        foreach (var card in dashboard.Cards)
        {
            switch (card.Id)
            {
                case "apache":
                    card.ApplyPreview(ServiceStatus.Running, new ProcessSnapshot(14820, now.AddMinutes(-134), 48_300_000, 2));
                    break;
                case "mysql":
                    card.ApplyPreview(ServiceStatus.Running, new ProcessSnapshot(7788, now.AddHours(-5).AddMinutes(-12), 431_000_000, 1));
                    break;
                default:
                    card.ApplyPreview(ServiceStatus.Stopped, null);
                    break;
            }
        }

        dashboard.ShowPortTip(80, null);
        var main = provider.GetRequiredService<MainWindowViewModel>();
        foreach (var item in main.Services)
        {
            item.Tone = item.Name == "Mailpit" ? "Neutral" : "Success";
        }
    }

    public static void ApplyNewSite(NewSiteDialog dialog) => _ = dialog;
}
