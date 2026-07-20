using QuotaOrb.Core.Settings;
using QuotaOrb.Core.Domain;
using QuotaOrb.Windows.ViewModels;
using QuotaOrb.Windows.Windows;

namespace QuotaOrb.Windows.Tests.Windows;

public sealed class DetailFlyoutWindowTests
{
    [Fact]
    public void Constructor_WithErrorState_UsesRoseStatusBadge()
    {
        RunInSta(() =>
        {
            var viewModel = new OrbViewModel();
            viewModel.Apply(new QuotaViewState(
                null,
                QuotaRisk.Error,
                null,
                null,
                DateTimeOffset.Parse("2026-07-13T14:00:00+08:00"),
                new QuotaReadError("timeout", "Codex request timed out.")));
            var window = new DetailFlyoutWindow(viewModel);
            var root = Assert.IsAssignableFrom<System.Windows.FrameworkElement>(window.Content);
            root.Measure(new System.Windows.Size(292, 276));
            root.Arrange(new System.Windows.Rect(0, 0, 292, 276));
            root.UpdateLayout();

            var badge = Assert.IsType<System.Windows.Controls.Border>(
                window.FindName("StatusBadge"));
            var badgeText = Assert.IsType<System.Windows.Controls.TextBlock>(
                window.FindName("StatusBadgeText"));

            Assert.Equal(
                System.Windows.Media.Color.FromRgb(0xFF, 0xE8, 0xEA),
                Assert.IsType<System.Windows.Media.SolidColorBrush>(badge.Background).Color);
            Assert.Equal(
                System.Windows.Media.Color.FromRgb(0xA1, 0x4D, 0x58),
                Assert.IsType<System.Windows.Media.SolidColorBrush>(badgeText.Foreground).Color);
        });
    }

    [Fact]
    public void Constructor_BuildsGlassConsoleAtApprovedSize()
    {
        RunInSta(() =>
        {
            var viewModel = new OrbViewModel();
            var window = new DetailFlyoutWindow(viewModel);

            Assert.Equal(326d, window.Width);
            Assert.Equal(198d, window.Height);
            Assert.NotNull(window.FindName("GlassConsoleBorder"));
            Assert.NotNull(window.FindName("CurrentMetricTile"));
            Assert.NotNull(window.FindName("WeeklyMetricTile"));
            Assert.NotNull(window.FindName("CurrentMetricLabel"));
            Assert.NotNull(window.FindName("WeeklyMetricLabel"));
        });
    }

    [Fact]
    public void Constructor_WithOpenRouterBalance_ShowsOnlyBalanceAndProviderMetrics()
    {
        RunInSta(() =>
        {
            var now = DateTimeOffset.Parse("2026-07-14T12:00:00+08:00");
            var viewModel = new OrbViewModel();
            viewModel.Apply(new QuotaViewState(
                null,
                QuotaRisk.Safe,
                null,
                null,
                now,
                null)
            {
                Balance = new BalanceSnapshot(
                    "openrouter",
                    "OpenRouter",
                    BalanceKind.ApiKeyLimitRemaining,
                    new[] { new BalanceAmount(12.34m, "USD") },
                    now),
                SourceName = "CC Switch · OpenRouter"
            });
            var window = new DetailFlyoutWindow(viewModel);
            var root = Assert.IsAssignableFrom<System.Windows.FrameworkElement>(window.Content);
            root.Measure(new System.Windows.Size(292, 230));
            root.Arrange(new System.Windows.Rect(0, 0, 292, 230));
            root.UpdateLayout();
            var balanceLabel = Assert.IsType<System.Windows.Controls.TextBlock>(
                window.FindName("CurrentMetricLabel"));
            var balanceValue = Assert.IsType<System.Windows.Controls.TextBlock>(
                window.FindName("CurrentMetricValue"));
            var providerLabel = Assert.IsType<System.Windows.Controls.TextBlock>(
                window.FindName("WeeklyMetricLabel"));
            var providerValue = Assert.IsType<System.Windows.Controls.TextBlock>(
                window.FindName("WeeklyMetricValue"));
            var balanceTile = Assert.IsType<System.Windows.Controls.Border>(
                window.FindName("CurrentMetricTile"));
            var providerTile = Assert.IsType<System.Windows.Controls.Border>(
                window.FindName("WeeklyMetricTile"));
            var agentBadge = Assert.IsType<System.Windows.Controls.Border>(
                window.FindName("AgentBadge"));
            var agentText = Assert.IsType<System.Windows.Controls.TextBlock>(agentBadge.Child);
            var balanceTag = Assert.IsType<System.Windows.Controls.TextBlock>(
                window.FindName("CurrentMetricTag"));
            var providerTag = Assert.IsType<System.Windows.Controls.TextBlock>(
                window.FindName("WeeklyMetricTag"));

            Assert.Equal("当前余额", balanceLabel.Text);
            Assert.Equal("$12.34", balanceValue.Text);
            Assert.Equal("当前供应商", providerLabel.Text);
            Assert.Equal("OpenRouter", providerValue.Text);
            Assert.Equal(24d, balanceValue.FontSize);
            Assert.Equal(19d, providerValue.FontSize);
            Assert.Equal(balanceTile.ActualHeight, providerTile.ActualHeight, precision: 2);
            Assert.Equal("软件 · Codex", agentText.Text);
            Assert.Equal("额度", balanceTag.Text);
            Assert.Equal("API", providerTag.Text);
        });
    }

    [Fact]
    public void ShowAndHideAnimated_WithAnimationsDisabled_DoesNotThrow()
    {
        RunInSta(() =>
        {
            var viewModel = new OrbViewModel();
            viewModel.ApplySettings(new AppSettings { AnimationsEnabled = false });
            var window = new DetailFlyoutWindow(viewModel);

            window.ShowAnimated();
            Assert.True(window.IsVisible);
            window.HideAnimated();
            Assert.False(window.IsVisible);
            window.Close();
        });
    }

    private static void RunInSta(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                captured = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(captured);
    }

}
