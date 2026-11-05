using System.Globalization;
using Avalonia.Styling;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Settings;
using GapActivator.App.Tests.Fakes;

namespace GapActivator.App.Tests.Services;

public class ThemeApplierTests(HeadlessFixture fixture)
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static AppSettingsService NewService(TempDirectory dir, AppSettings initial) =>
        new(new AppSettingsStore(Path.Combine(dir.Path, "app-settings.json"), new ListLogger<AppSettingsStore>(), English), initial);

    [Theory]
    [InlineData(AppTheme.System, "Default")]
    [InlineData(AppTheme.Light, "Light")]
    [InlineData(AppTheme.Dark, "Dark")]
    public async Task ThemeApplier_EachTheme_SetsTheVariant(AppTheme theme, string expected)
    {
        using var dir = new TempDirectory();
        var other = theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        var service = NewService(dir, AppSettings.Defaults(English) with { Theme = other });
        var applier = new ThemeApplier(new Localizer(AppLanguage.English), service, new ImmediateUiDispatcher());

        var variant = await fixture.Session.Dispatch(
            () =>
            {
                applier.Start();
                service.Update(s => s with { Theme = theme });
                return Avalonia.Application.Current.ShouldNotBeNull().RequestedThemeVariant;
            },
            TestContext.Current.CancellationToken);

        var expectedVariant = expected switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
        variant.ShouldBe(expectedVariant);
    }

    [Fact]
    public async Task ThemeApplier_LanguageChanged_SwitchesTheLocalizerLive()
    {
        using var dir = new TempDirectory();
        var service = NewService(dir, AppSettings.Defaults(English));
        var localizer = new Localizer(AppLanguage.English);
        var applier = new ThemeApplier(localizer, service, new ImmediateUiDispatcher());
        var refreshed = 0;
        localizer.PropertyChanged += (_, _) => refreshed++;

        await fixture.Session.Dispatch(
            () =>
            {
                applier.Start();
                service.Update(s => s with { Language = AppLanguage.Portuguese });
                return 0;
            },
            TestContext.Current.CancellationToken);

        localizer.Language.ShouldBe(AppLanguage.Portuguese);
        refreshed.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ThemeApplier_ChangeFromAnotherThread_IsAppliedOnlyThroughTheDispatcher()
    {
        using var dir = new TempDirectory();
        var service = NewService(dir, AppSettings.Defaults(English));
        var localizer = new Localizer(AppLanguage.English);
        var queue = new QueueingDispatcher();
        var applier = new ThemeApplier(localizer, service, queue);
        applier.Start();

        await Task.Run(() => service.Update(s => s with { Language = AppLanguage.Portuguese }), TestContext.Current.CancellationToken);

        localizer.Language.ShouldBe(AppLanguage.English);
        await fixture.Session.Dispatch(
            () =>
            {
                queue.Run();
                return 0;
            },
            TestContext.Current.CancellationToken);
        localizer.Language.ShouldBe(AppLanguage.Portuguese);
    }

    private sealed class QueueingDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _pending = new();

        public void Post(Action action)
        {
            lock (_pending)
            {
                _pending.Enqueue(action);
            }
        }

        public void Run()
        {
            lock (_pending)
            {
                while (_pending.Count > 0)
                {
                    _pending.Dequeue()();
                }
            }
        }
    }
}
