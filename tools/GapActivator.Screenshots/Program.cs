using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GapActivator.App.Services;
using GapActivator.App.ViewModels;
using GapActivator.App.Views;
using GapActivator.Screenshots;

var outDir = Path.GetFullPath("docs/images");
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--out" && i + 1 < args.Length)
    {
        outDir = Path.GetFullPath(args[++i]);
    }
}

Directory.CreateDirectory(outDir);

AppBuilder.Configure<GapActivator.App.App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

(string Name, ThemeVariant Theme, Action<Scene> Prepare)[] shots =
[
    ("dashboard-light", ThemeVariant.Light, s => s.Navigator.Navigate(AppPage.Dashboard)),
    ("dashboard-dark", ThemeVariant.Dark, s => s.Navigator.Navigate(AppPage.Dashboard)),
    ("processes-dark", ThemeVariant.Dark, s => s.Navigator.Navigate(AppPage.Processes)),
    ("schedule-editor-light", ThemeVariant.Light, s =>
    {
        s.Navigator.Navigate(AppPage.Processes);
        Pump(s.Processes.OpenEditorAsync(s.NightlyId));
        var editor = s.Processes.Editor!;
        editor.AddScheduleCommand.Execute(null);
        var schedule = editor.ScheduleEditor!;
        schedule.Mode = GapActivator.App.Scheduling.ScheduleMode.Weekly;
        schedule.Monday = schedule.Tuesday = schedule.Wednesday = schedule.Thursday = schedule.Friday = true;
        schedule.Saturday = schedule.Sunday = false;
        schedule.Time = TimeSpan.FromHours(2);
        // The preview waits 300 ms on the injected clock, so move that clock instead of the wall.
        s.Time.Advance(TimeSpan.FromSeconds(1));
        Pump(schedule.PreviewRefresh);
    }),
    ("history-dark", ThemeVariant.Dark, s => s.Navigator.Navigate(AppPage.History)),
    ("run-detail-light", ThemeVariant.Light, s =>
    {
        s.Navigator.Navigate(AppPage.History);
        var row = s.History.Items.First(r => r.Id == s.FailedRunId);
        Pump(s.History.OpenRunCommand.ExecuteAsync(row));
    }),
    ("settings-light", ThemeVariant.Light, s => s.Navigator.Navigate(AppPage.Settings)),
];

var failures = 0;
foreach (var (name, theme, prepare) in shots)
{
    Avalonia.Application.Current!.RequestedThemeVariant = theme;
    var scene = PumpResult(Scenes.BuildAsync());
    var window = new MainWindow { DataContext = scene.Main, Width = 1100, Height = 720 };
    window.Show();
    prepare(scene);
    Dispatcher.UIThread.RunJobs();

    // The pages scroll, so the two that matter below the fold are scrolled to their card.
    if (name == "schedule-editor-light")
    {
        ScrollTo(window, "ScheduleEditorCard", 130);
    }
    else if (name == "settings-light")
    {
        ScrollTo(window, "EmailCard", 8);
    }

    for (var i = 0; i < 4; i++)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(60);
    }

    var path = Path.Combine(outDir, name + ".png");
    var frame = window.CaptureRenderedFrame();
    if (frame is null)
    {
        Console.Error.WriteLine($"FAILED (null frame): {path}");
        failures++;
    }
    else
    {
        frame.Save(path, new PngBitmapEncoderOptions());
        frame.Dispose();
        Console.WriteLine(path);
    }

    window.Close();
}

return failures == 0 ? 0 : 1;

// The headless session has no running loop, so a task is awaited by draining the dispatcher until it ends.
static T PumpResult<T>(Task<T> task)
{
    Pump((Task)task);
    return task.Result;
}

static void Pump(Task task)
{
    var deadline = DateTime.UtcNow.AddSeconds(20);
    while (!task.IsCompleted)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(5);
        if (DateTime.UtcNow > deadline)
        {
            throw new TimeoutException("A scenario step did not finish.");
        }
    }

    task.GetAwaiter().GetResult();
}

static void ScrollTo(Window window, string name, double margin)
{
    Dispatcher.UIThread.RunJobs();
    var target = window.GetVisualDescendants().OfType<Control>().First(c => c.Name == name);
    var viewer = target.FindAncestorOfType<ScrollViewer>()!;
    var content = (Visual)viewer.Content!;
    viewer.Offset = new Vector(0, target.TranslatePoint(default, content)!.Value.Y - margin);
    Dispatcher.UIThread.RunJobs();
}
