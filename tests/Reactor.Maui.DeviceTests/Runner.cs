using System.Diagnostics;
using Microsoft.Maui.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Maui.Handlers;
using Microsoft.UI.Reactor.Maui.Hosting;

namespace Reactor.Maui.DeviceTests;

public sealed record Fixture(string Name, Func<FixtureContext, Task> Run);

/// <summary>A failed check inside a fixture.</summary>
public sealed class CheckFailedException(string message) : Exception(message);

/// <summary>A fixture that doesn't apply on this platform; reported as TAP's <c># SKIP</c>.</summary>
public sealed class SkipException(string reason) : Exception(reason);

/// <summary>
/// Mounts one fixture's tree into the test window, through the real MAUI handlers of the platform
/// the app runs on, and gives the fixture ways to wait for and inspect the result.
/// </summary>
public sealed class FixtureContext(Window window)
{
    private ReactorRoot? _root;
    private readonly ContentPage _viewHost = new();
    private WindowPageSetter? _pages;

    /// <summary>The window-page setter ReactorWindow uses, so fixtures exercise its Android workaround.</summary>
    internal WindowPageSetter Pages => _pages ??= new(window, UnhandledErrors.Add);

    public Window Window => window;
    public List<Exception> UnhandledErrors { get; } = [];

    public void Show(Element element, ElementHandlerRegistry? registry = null)
    {
        _root = new ReactorRoot(registry ?? MauiHandlers.CreateRegistry(), new MauiRenderScheduler(window.Dispatcher), SetNative);
        _root.UnhandledException += UnhandledErrors.Add;
        Render(element);
    }

    public void Render(Element element) => _root!.Render(MauiContexts.Window.Provide(window, element));

    /// <summary>Shows a plain MAUI view, without Reactor, for characterizing MAUI itself.</summary>
    public void ShowNative(View view)
    {
        _viewHost.Content = view;
        Pages.Set(_viewHost);
    }

    private bool _keepWindowPage;

    /// <summary>Unmounts the tree. <paramref name="keepWindowPage"/> leaves its page in the window.</summary>
    public void Unmount(bool keepWindowPage = false)
    {
        _keepWindowPage = keepWindowPage;
        _root?.Unmount();
        _root = null;
    }

    private void SetNative(object? native)
    {
        if (native is null && _keepWindowPage) return;
        if (native is Page page)
        {
            Pages.Set(page);
            return;
        }
        _viewHost.Content = native is null ? null : Props.AsView(native);
        Pages.Set(_viewHost);
    }

    /// <summary>
    /// Waits until the work Reactor has queued on the UI thread has run: a render a state change
    /// scheduled, the reports and restores that render posted, and one more round of each. Reactor queues
    /// through the window's dispatcher, so a barrier dispatched the same way runs after everything queued
    /// before it. A plain delay doesn't order with that queue: on iOS and Mac Catalyst a
    /// <c>Task.Delay</c> continuation or <c>MainThread.InvokeOnMainThreadAsync</c> goes through the run
    /// loop, the dispatcher through the main GCD queue, and under load the first can run while a render
    /// is still waiting in the second.
    /// </summary>
    public async Task Drain(int rounds = 3)
    {
        for (var i = 0; i < rounds; i++)
            await window.Dispatcher.DispatchAsync(() => { });
    }

    /// <summary>Waits, letting the UI thread run, until <paramref name="condition"/> holds.</summary>
    public async Task WaitFor(Func<bool> condition, string what, int timeoutMs = 5000)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.ElapsedMilliseconds > timeoutMs) throw new CheckFailedException($"timed out after {timeoutMs} ms waiting for {what}");
            await Task.Delay(16);
        }
    }

    /// <summary>Lets animations and posted work finish, so a later check sees the settled state.</summary>
    public Task Settle(int milliseconds = 400) => Task.Delay(milliseconds);

    public T Find<T>(Func<T, bool>? predicate = null) where T : class =>
        FindAll(predicate).FirstOrDefault() ?? throw new CheckFailedException($"no {typeof(T).Name} found on screen");

    public T? TryFind<T>(Func<T, bool>? predicate = null) where T : class => FindAll(predicate).FirstOrDefault();

    public T ById<T>(string automationId) where T : class, IView =>
        Find<T>(v => v.AutomationId == automationId);

    public IEnumerable<T> FindAll<T>(Func<T, bool>? predicate = null) where T : class
    {
        var pending = new Stack<IVisualTreeElement>();
        pending.Push(window);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node is T match && (predicate is null || predicate(match))) yield return match;
            var children = node.GetVisualChildren();
            for (var i = children.Count - 1; i >= 0; i--) pending.Push(children[i]);
        }
    }

    public static void Check(bool condition, string message)
    {
        if (!condition) throw new CheckFailedException(message);
    }

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new CheckFailedException($"{what}: expected <{expected}>, got <{actual}>");
    }

    public static void Close(double expected, double actual, string what, double tolerance = 1)
    {
        if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
            throw new CheckFailedException($"{what}: expected {expected:0.##} ± {tolerance}, got {actual:0.##}");
    }
}

/// <summary>Runs the fixtures one by one and reports TAP on stdout and in a file.</summary>
public static class Runner
{
    public const string DoneMarker = "# REACTOR-DEVICE-TESTS-DONE";

    public static async Task RunAsync(Window window, string? filter)
    {
        var fixtures = Fixtures.All
            // "a|b" runs the fixtures matching either substring, in their usual order.
            .Where(f => string.IsNullOrEmpty(filter)
                || filter.Split('|').Any(part => part.Length > 0 && f.Name.Contains(part, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var report = new List<string>();
        void Emit(string line)
        {
            report.Add(line);
            Console.WriteLine(line);
        }

        Emit($"# Reactor MAUI device tests on {DeviceInfo.Platform} {DeviceInfo.VersionString} ({DeviceInfo.Idiom}, {DeviceInfo.DeviceType})");
        Emit($"1..{fixtures.Count}");
        var failures = 0;
        var skips = 0;
        for (var i = 0; i < fixtures.Count; i++)
        {
            var fixture = fixtures[i];
            var context = new FixtureContext(window);
            var clock = Stopwatch.StartNew();
            string? failure = null;
            string? skipped = null;
            try
            {
                var run = fixture.Run(context);
                if (await Task.WhenAny(run, Task.Delay(30_000)) != run) failure = "timed out after 30 s";
                else await run;
            }
            catch (SkipException skip)
            {
                skipped = skip.Message;
            }
            catch (Exception ex)
            {
                failure = ex is CheckFailedException ? ex.Message : ex.ToString();
            }
            finally
            {
                // A dialog left on screen would hold back every later fixture's dialogs: close it first.
                if (NativeDialogs.Count(window) > 0 && !await NativeDialogs.CloseAll(window))
                    failure ??= $"teardown: a native dialog could not be closed: {NativeDialogs.Describe(window)}";
                try
                {
                    context.Unmount();
                }
                catch (Exception ex)
                {
                    failure ??= "unmount failed: " + ex;
                }
                // Through the fixture's setter, which waits for a navigation the fixture left running
                // (replacing the page under it crashes MAUI on Android); a swap still waiting after
                // that is dropped, so it can't land on the next fixture's page.
                var blank = new ContentPage();
                context.Pages.Set(blank);
                var settle = Stopwatch.StartNew();
                while (!ReferenceEquals(window.Page, blank) && settle.ElapsedMilliseconds < 10_000) await Task.Delay(16);
                if (!ReferenceEquals(window.Page, blank))
                {
                    failure ??= "teardown: the window page was not replaced within 10 s";
                    context.Pages.Cancel();
                    window.Page = blank;
                }
                await Task.Delay(100);
            }
            // Checked after teardown: unmounting runs effect cleanups, which can fail too.
            if (failure is null && context.UnhandledErrors.Count > 0) failure = "unhandled render error: " + context.UnhandledErrors[0];

            if (failure is null && skipped is not null)
            {
                skips++;
                Emit($"ok {i + 1} - {fixture.Name} # SKIP {skipped}");
            }
            else if (failure is null)
            {
                Emit($"ok {i + 1} - {fixture.Name} # {clock.ElapsedMilliseconds} ms");
            }
            else
            {
                failures++;
                Emit($"not ok {i + 1} - {fixture.Name}");
                foreach (var line in failure.Split('\n')) Emit("#   " + line.TrimEnd());
            }
        }

        if (fixtures.Count == 0)
        {
            // A filter that matches nothing must not read as a pass.
            failures++;
            Emit($"not ok 1 - no fixture matches the filter '{filter}'");
        }

        Emit($"# passed {fixtures.Count - failures - skips}, skipped {skips}, failed {failures}");
        Emit($"{DoneMarker} exit={(failures == 0 ? 0 : 1)}");
        try
        {
            File.WriteAllLines(Path.Combine(FileSystem.AppDataDirectory, "device-tests.tap"), report);
        }
        catch (IOException)
        {
        }

        if (Environment.GetEnvironmentVariable("REACTOR_DEVICE_TESTS_STAY") != "1")
        {
            await Task.Delay(200);
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
