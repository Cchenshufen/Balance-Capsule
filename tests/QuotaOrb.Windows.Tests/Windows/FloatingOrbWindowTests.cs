using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using QuotaOrb.Core.Domain;
using QuotaOrb.Core.Settings;
using QuotaOrb.Windows.ViewModels;
using QuotaOrb.Windows.Windows;

namespace QuotaOrb.Windows.Tests.Windows;

public sealed class FloatingOrbWindowTests
{
    [Fact]
    public void Constructor_DoesNotRenderCompactWindowBadges()
    {
        RunInSta(() =>
        {
            var window = new FloatingOrbWindow(new OrbViewModel());
            var content = Assert.IsAssignableFrom<DependencyObject>(window.Content);
            var textBindings = EnumerateVisualDescendants<TextBlock>(content)
                .Select(text => BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path.Path)
                .Where(path => path is not null)
                .ToArray();

            Assert.DoesNotContain("WindowBadgeText", textBindings);
            Assert.DoesNotContain("ResetBadgeText", textBindings);
        });
    }

    [Fact]
    public void Constructor_BuildsApprovedGlassLayersAndMotion()
    {
        RunInSta(() =>
        {
            var window = new FloatingOrbWindow(new OrbViewModel());
            var expectedNames = new[]
            {
                "GroundShadow", "AuraRing", "OuterGlassRim", "InnerGlassRim",
                "BackLiquid", "FrontLiquid", "TickRing", "Bubble1", "Bubble2",
                "Bubble3", "SweepHighlight"
            };

            foreach (var name in expectedNames)
            {
                Assert.NotNull(window.FindName(name));
            }

            var storyboard = Assert.IsType<System.Windows.Media.Animation.Storyboard>(
                window.FindResource("AmbientMotionStoryboard"));
            var floatAnimation = Assert.Single(
                storyboard.Children
                    .OfType<System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames>()
                    .Where(animation =>
                        System.Windows.Media.Animation.Storyboard.GetTargetName(animation) ==
                        "OrbTranslateY"));
            Assert.Equal(
                TimeSpan.FromSeconds(4.2),
                floatAnimation.KeyFrames[^1].KeyTime.TimeSpan);
        });
    }

    [Fact]
    public void Constructor_WithBalance_HidesPercentAndShowsBalanceCaption()
    {
        RunInSta(() =>
        {
            var viewModel = new OrbViewModel();
            viewModel.Apply(new QuotaViewState(
                null,
                QuotaRisk.Safe,
                null,
                null,
                DateTimeOffset.Parse("2026-07-14T12:00:00+08:00"),
                null)
            {
                Balance = new BalanceSnapshot(
                    "deepseek",
                    "DeepSeek",
                    BalanceKind.AccountBalance,
                    new[] { new BalanceAmount(28m, "CNY") },
                    DateTimeOffset.Parse("2026-07-14T12:00:00+08:00"))
            });
            var window = new FloatingOrbWindow(viewModel);
            var root = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
            root.Measure(new Size(112, 112));
            root.Arrange(new Rect(0, 0, 112, 112));
            root.UpdateLayout();
            var percent = Assert.IsType<TextBlock>(window.FindName("PercentSuffix"));
            var caption = Assert.IsType<TextBlock>(window.FindName("CompactCaption"));
            var amount = EnumerateVisualDescendants<TextBlock>(root)
                .Single(text => text.Text == "¥28.00");

            Assert.Equal(Visibility.Collapsed, percent.Visibility);
            Assert.Equal("账户余额", caption.Text);
            Assert.Equal(18d, amount.FontSize);
        });
    }

    [Fact]
    public void Constructor_UsesExpandedHostAndOrbOnlyHitTarget()
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new FloatingOrbWindow(new OrbViewModel());
                var hitTarget = Assert.IsAssignableFrom<System.Windows.FrameworkElement>(
                    window.FindName("OrbHitTarget"));
                var shadowLayer = Assert.IsAssignableFrom<System.Windows.FrameworkElement>(
                    window.FindName("ShadowLayer"));

                Assert.Equal(78d, window.Width);
                Assert.Equal(78d, window.Height);
                Assert.Equal(84d, hitTarget.Width);
                Assert.Equal(84d, hitTarget.Height);
                Assert.False(shadowLayer.IsHitTestVisible);
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

    [Fact]
    public void SetSelectedAgent_UpdatesMutuallyExclusiveMenuChecks()
    {
        RunInSta(() =>
        {
            var window = new FloatingOrbWindow(new OrbViewModel());

            window.SetSelectedAgent(AgentSource.Claude);

            var codex = Assert.IsType<MenuItem>(window.FindName("CodexAgentMenuItem"));
            var claude = Assert.IsType<MenuItem>(window.FindName("ClaudeAgentMenuItem"));
            var claudeCode = Assert.IsType<MenuItem>(window.FindName("ClaudeCodeAgentMenuItem"));
            Assert.False(codex.IsChecked);
            Assert.True(claude.IsChecked);
            Assert.False(claudeCode.IsChecked);

            window.SetSelectedAgent(AgentSource.ClaudeCode);

            Assert.False(codex.IsChecked);
            Assert.False(claude.IsChecked);
            Assert.True(claudeCode.IsChecked);
        });
    }

    [Fact]
    public void SetSelectedQuotaWindow_UpdatesMutuallyExclusiveMenuChecks()
    {
        RunInSta(() =>
        {
            var window = new FloatingOrbWindow(new OrbViewModel());

            window.SetSelectedQuotaWindow(QuotaWindowMode.Weekly);

            var fiveHour = Assert.IsType<MenuItem>(window.FindName("FiveHourQuotaMenuItem"));
            var weekly = Assert.IsType<MenuItem>(window.FindName("WeeklyQuotaMenuItem"));
            Assert.False(fiveHour.IsChecked);
            Assert.True(weekly.IsChecked);
        });
    }

    [Fact]
    public void SetQuotaWindowAvailability_DisablesThirdPartyAndMissingFiveHourOptions()
    {
        RunInSta(() =>
        {
            var window = new FloatingOrbWindow(new OrbViewModel());
            var parent = Assert.IsType<MenuItem>(window.FindName("QuotaWindowMenuItem"));
            var fiveHour = Assert.IsType<MenuItem>(window.FindName("FiveHourQuotaMenuItem"));
            var weekly = Assert.IsType<MenuItem>(window.FindName("WeeklyQuotaMenuItem"));

            window.SetQuotaWindowAvailability(false, false, false);
            Assert.False(parent.IsEnabled);

            window.SetQuotaWindowAvailability(true, false, true);
            Assert.True(parent.IsEnabled);
            Assert.False(fiveHour.IsEnabled);
            Assert.True(weekly.IsEnabled);
        });
    }

    [Fact]
    public void Constructor_NestsStartupUnderSettingsMenu()
    {
        RunInSta(() =>
        {
            var window = new FloatingOrbWindow(new OrbViewModel());
            var startup = Assert.IsType<MenuItem>(window.FindName("StartupMenuItem"));
            var settings = Assert.IsType<MenuItem>(startup.Parent);

            Assert.Equal("设置", settings.Header);
        });
    }

    [Fact]
    public void AttachDetailWindow_BeforeOwnerIsShown_DoesNotThrow()
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                var viewModel = new OrbViewModel();
                var orb = new FloatingOrbWindow(viewModel);
                var detail = new DetailFlyoutWindow(viewModel);

                orb.AttachDetailWindow(detail);
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

    private static IEnumerable<T> EnumerateVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in EnumerateVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
