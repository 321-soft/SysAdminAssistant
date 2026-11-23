using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.Desktop;

public partial class MainWindow
{
    private bool productUpdateChecksStarted;
    private bool productUpdateCheckPending;

    private void StartProductUpdateChecks()
    {
        if (productUpdateChecksStarted) return;
        productUpdateChecksStarted = true;
        AutomationProperties.SetName(ProductUpdatesButton, ProductText.Get("Обновления"));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromHours(2) };
        timer.Tick += async (_, _) => await CheckProductUpdatesAsync();
        Closed += (_, _) => timer.Stop();
        timer.Start();
        _ = CheckProductUpdatesAsync();
    }

    private async Task CheckProductUpdatesAsync()
    {
        if (productUpdateCheckPending || lifetime.IsCancellationRequested) return;
        productUpdateCheckPending = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            var clientCheck = HasClientUpdateAsync(timeout.Token);
            var serverCheck = remote.CurrentUser.Administrator && !remote.RequiresSignIn
                ? HasServerUpdateAsync(timeout.Token) : Task.FromResult(false);
            var client = await clientCheck;
            var server = await serverCheck;
            if (lifetime.IsCancellationRequested) return;

            var caption = ProductText.Get((client, server) switch
            {
                (true, true) => "Доступны обновления клиента и сервера",
                (true, false) => "Доступно обновление клиента",
                (false, true) => "Доступно обновление сервера",
                _ => "Обновления"
            });
            ProductUpdatesDot.Visibility = client || server ? Visibility.Visible : Visibility.Hidden;
            ProductUpdatesButton.ToolTip = caption;
            AutomationProperties.SetHelpText(ProductUpdatesButton, caption);
        }
        finally { productUpdateCheckPending = false; }
    }

    private async Task<bool> HasClientUpdateAsync(CancellationToken token)
    {
        try
        {
            var source = remote.Settings.ProductUpdates;
            var manifest = await ProductReleases.ManifestAsync(source.Primary, source.Mirror, token);
            var release = ProductReleases.Verify(manifest, ProductReleases.PublicKey, DateTime.UtcNow);
            return Version.Parse(release.Client.Version) > typeof(MainWindow).Assembly.GetName().Version;
        }
        catch (Exception e) when (IsUpdateCheckFailure(e)) { return false; }
    }

    private async Task<bool> HasServerUpdateAsync(CancellationToken token)
    {
        try
        {
            var response = await remote.CheckServerProductUpdateAsync(token);
            var release = ProductReleases.Verify(response.Manifest, ProductReleases.PublicKey, DateTime.UtcNow);
            return Version.TryParse(response.ServerVersion, out var installed) && Version.Parse(release.Server.Version) > installed;
        }
        catch (Exception e) when (IsUpdateCheckFailure(e)) { return false; }
    }

    private static bool IsUpdateCheckFailure(Exception e) =>
        e is AggregateException or HttpRequestException or OperationCanceledException or InvalidOperationException
            or System.IO.IOException or JsonException or CryptographicException;
}
