using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AppStart.Setup.Views;

namespace AppStart.Setup.Tests.Wizard;

/// <summary>
/// One STA thread with a running dispatcher and the setup App (Fluent theme, Styles.xaml) for all UI tests.
/// WPF allows only one Application per process, so every UI test runs here.
/// </summary>
internal static class WpfHost
{
    private static readonly Lazy<Dispatcher> Host = new(Start);

    private static Dispatcher Start()
    {
        var ready = new TaskCompletionSource<Dispatcher>();
        var thread = new Thread(() =>
        {
            try
            {
                WizardLauncher.EnsureApplication();
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                ready.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "WPF test host",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }

    public static Task RunAsync(Func<Task> action) => Host.Value.InvokeAsync(action).Task.Unwrap();

    /// <summary>Lets bindings, layout and rendering settle.</summary>
    public static async Task SettleAsync()
    {
        for (var i = 0; i < 3; i++)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>Yields the dispatcher until <paramref name="condition"/> holds, e.g. after work started with Task.Run.</summary>
    public static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException($"Condition not met within {timeoutMs} ms.");
            }

            await Task.Delay(20);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }

        await SettleAsync();
    }

    public static string OutputDirectory
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppStartTests");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Renders an element (already laid out in a window) to a PNG in %USERPROFILE%\AppStartTests.</summary>
    public static string SavePng(FrameworkElement element, string fileName)
    {
        var width = (int)Math.Ceiling(element.ActualWidth);
        var height = (int)Math.Ceiling(element.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Absolute viewbox: content that overflows (e.g. template parts) must not rescale the picture.
            var brush = new VisualBrush(element)
            {
                Viewbox = new Rect(0, 0, width, height),
                ViewboxUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            };
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
        }

        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(OutputDirectory, fileName);
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }
}
