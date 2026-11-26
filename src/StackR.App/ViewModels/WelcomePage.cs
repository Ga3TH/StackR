namespace StackR.App.ViewModels;

/// <summary>Page d'accueil du modèle « Vide » (public/index.php).</summary>
internal static class WelcomePage
{
    public const string Html = """
        <?php
        $host = htmlspecialchars($_SERVER['HTTP_HOST'] ?? 'localhost', ENT_QUOTES, 'UTF-8');
        $php = htmlspecialchars(PHP_VERSION, ENT_QUOTES, 'UTF-8');
        $root = htmlspecialchars(__DIR__, ENT_QUOTES, 'UTF-8');
        ?>
        <!doctype html>
        <html lang="fr">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title><?= $host ?></title>
            <style>
                :root { color-scheme: light dark; --accent: #7c3aed; --accent2: #06b6d4; }
                * { box-sizing: border-box; }
                body { margin: 0; min-height: 100vh; display: grid; place-items: center;
                       font-family: "Segoe UI", system-ui, sans-serif; background: Canvas; color: CanvasText; }
                main { max-width: 34rem; padding: 2rem; text-align: center; }
                .mark { width: 4.5rem; height: 4.5rem; margin: 0 auto 1.5rem; border-radius: 1.1rem;
                        background: linear-gradient(135deg, var(--accent), var(--accent2)); }
                h1 { margin: 0 0 .5rem; font-size: 2rem; }
                p { margin: .5rem 0; opacity: .8; line-height: 1.5; }
                code { padding: .1rem .4rem; border-radius: .4rem; background: color-mix(in srgb, CanvasText 10%, transparent); }
                .meta { margin-top: 1.5rem; font-size: .9rem; opacity: .65; }
            </style>
        </head>
        <body>
            <main>
                <div class="mark"></div>
                <h1>Votre site est prêt</h1>
                <p><strong><?= $host ?></strong> répond bien, servi par StackR.</p>
                <p>Modifiez <code>public/index.php</code> pour commencer.</p>
                <p class="meta">PHP <?= $php ?> &middot; <?= $root ?></p>
            </main>
        </body>
        </html>
        """;
}
