using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DustyBytes.App;
using DustyBytes.App.Services;
using DustyBytes.App.ViewModels;

namespace DustyBytes.Tests;

public class KabukStandardiTests
{
    static readonly string[] Screens = ["Genel bakış", "Öneriler", "Harita", "Programlar", "Temizlik", "Karantina"];

    static void Pump()
    {
        for (var i = 0; i < 30; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static async Task Settle()
    {
        for (var i = 0; i < 10; i++)
        {
            Pump();
            await Task.Delay(5);
        }
        Pump();
    }

    static MainWindow Open()
    {
        var window = new MainWindow();
        window.Show();
        Pump();
        return window;
    }

    static IEnumerable<T> All<T>(Visual root) where T : Visual =>
        root.GetVisualDescendants().OfType<T>().Where(v => v.IsEffectivelyVisible);

    static bool InScrollBar(Visual v) => v.GetVisualAncestors().Any(a => a is ScrollBar);

    static async IAsyncEnumerable<(string Name, MainWindow Window)> EachScreen()
    {
        var window = Open();
        var vm = (MainViewModel)window.DataContext!;
        foreach (var item in vm.NavItems)
        {
            vm.SelectedNav = item;
            await Settle();
            yield return (item.Label, window);
        }
        await vm.StartTourAsync();
        await Settle();
        if (vm.Navigation.Current is TourViewModel tour)
        {
            yield return ("Tur", window);
            tour.EndCommand.Execute(null);
            await Settle();
            yield return ("Tur özeti", window);
        }
        vm.GoTo(vm.Programs);
        await Settle();
        vm.Programs.Selected = vm.Programs.Rows.FirstOrDefault();
        if (vm.Programs.UninstallCommand.CanExecute(null))
        {
            vm.Programs.UninstallCommand.Execute(null);
            await Settle();
            yield return ("Kaldırma", window);
        }
        vm.GoTo(vm.Programs);
        await Settle();
        if (vm.Programs.Rows.Count > 0)
        {
            vm.Navigation.Push(new BulkUninstallViewModel(vm, vm.Programs, [.. vm.Programs.Rows.Take(2)]));
            await Settle();
            yield return ("Toplu kaldırma", window);
        }
        window.Close();
    }

    [AvaloniaFact]
    public void Window_Uses_Custom_Chrome()
    {
        var window = Open();
        Assert.True(window.ExtendClientAreaToDecorationsHint);
        Assert.Equal(Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome, window.ExtendClientAreaChromeHints);
        Assert.Equal(-1, window.ExtendClientAreaTitleBarHeightHint);
        var bar = window.FindControl<DustyBytes.App.Kabuk.TitleBar>("TitleBar")!;
        Assert.True(bar.Bounds.Height <= 40);
        Assert.Equal(Win32Properties.Win32HitTestValue.MaxButton,
            Win32Properties.GetNonClientHitTestResult(bar.FindControl<Button>("BuyutDugmesi")!));
        window.Close();
    }

    [AvaloniaFact]
    public void Signature_Sits_Left_Of_Minimize()
    {
        var window = Open();
        var bar = window.FindControl<DustyBytes.App.Kabuk.TitleBar>("TitleBar")!;
        var signature = bar.FindControl<Control>("ImzaDugmesi")!;
        var minimize = bar.FindControl<Button>("KucultDugmesi")!;
        var sigRight = signature.TranslatePoint(new Point(signature.Bounds.Width, 0), window)!.Value.X;
        var minLeft = minimize.TranslatePoint(new Point(0, 0), window)!.Value.X;
        Assert.True(signature.IsEffectivelyVisible);
        Assert.True(sigRight <= minLeft, $"imza sağ kenarı {sigRight}, küçült sol kenarı {minLeft}");
        window.Close();
    }

    [AvaloniaFact]
    public void Min_Width_Holds_Sidebar_And_Modal()
    {
        var window = Open();
        Assert.True(window.MinWidth >= 240 + 560, window.MinWidth.ToString());
        Assert.True(window.MinHeight >= 600);
        window.Close();
    }

    [AvaloniaFact]
    public void Sidebar_Has_Six_Screens_With_Roving_Focus()
    {
        var window = Open();
        var vm = (MainViewModel)window.DataContext!;
        Assert.Equal(Screens, vm.NavItems.Select(n => n.Label));
        var nav = window.FindControl<ListBox>("Nav")!;
        Assert.Equal(KeyboardNavigationMode.Once, KeyboardNavigation.GetTabNavigation(nav));
        Assert.Equal(6, nav.GetRealizedContainers().Count());
        window.Close();
    }

    [AvaloniaFact]
    public async Task Targets_Are_At_Least_24px_On_Every_Screen()
    {
        var failures = new List<string>();
        await foreach (var (name, window) in EachScreen())
            foreach (var target in All<Control>(window).Where(c => c is Button or CheckBox or ListBoxItem or TextBox && !InScrollBar(c)))
                if (target.Bounds.Width < 24 || target.Bounds.Height < 24)
                    failures.Add($"{name}: {target.GetType().Name} {target.Name} {target.Bounds.Width}x{target.Bounds.Height}");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [AvaloniaFact]
    public async Task No_Watermark_And_Named_Buttons()
    {
        var failures = new List<string>();
        await foreach (var (name, window) in EachScreen())
        {
            foreach (var box in All<TextBox>(window))
                if (!string.IsNullOrEmpty(box.Watermark))
                    failures.Add($"{name}: watermark {box.Name}");
            foreach (var button in All<Button>(window))
                if (button.Content is null or "" && string.IsNullOrEmpty(Avalonia.Automation.AutomationProperties.GetName(button)))
                    failures.Add($"{name}: adsız düğme");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [AvaloniaFact]
    public async Task Transitions_Touch_Only_Opacity_And_Transform()
    {
        AvaloniaProperty[] allowed = [Visual.OpacityProperty, Visual.RenderTransformProperty];
        var failures = new List<string>();
        await foreach (var (name, window) in EachScreen())
            foreach (var visual in new Visual[] { window }.Concat(window.GetVisualDescendants()))
                if (visual is Animatable { Transitions: { } transitions } && visual is not ScrollBar && !InScrollBar(visual))
                    foreach (var t in transitions)
                        if (t.GetType().GetProperty("Property")?.GetValue(t) is not AvaloniaProperty p || !allowed.Contains(p))
                            failures.Add($"{name}: {visual.GetType().Name} {t.GetType().Name}");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [AvaloniaFact]
    public async Task At_Most_One_Visible_Primary_Per_Screen()
    {
        var failures = new List<string>();
        await foreach (var (name, window) in EachScreen())
        {
            var primaries = All<Button>(window).Where(b => b.Classes.Contains("primary") || b.Classes.Contains("danger")).ToList();
            if (primaries.Count > 1)
                failures.Add($"{name}: {string.Join(", ", primaries.Select(p => p.Content))}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [AvaloniaFact]
    public async Task Every_Screen_Renders_Content()
    {
        var names = new List<string>();
        await foreach (var (name, window) in EachScreen())
        {
            var pages = window.FindControl<TransitioningContentControl>("Pages")!;
            Assert.NotNull(pages.Content);
            Assert.NotEmpty(All<TextBlock>(pages));
            names.Add(name);
        }
        Assert.Contains("Kaldırma", names);
        Assert.Contains("Tur", names);
        Assert.Contains("Tur özeti", names);
    }

    [AvaloniaFact]
    public void Motion_Follows_Reduced_Motion_Setting()
    {
        var before = Environment.GetEnvironmentVariable("DUSTYBYTES_REDUCED_MOTION");
        try
        {
            Environment.SetEnvironmentVariable("DUSTYBYTES_REDUCED_MOTION", "0");
            var moving = Open();
            Assert.Contains("anim", moving.Classes);
            Assert.IsType<CrossFade>(moving.FindControl<TransitioningContentControl>("Pages")!.PageTransition);
            moving.Close();

            Environment.SetEnvironmentVariable("DUSTYBYTES_REDUCED_MOTION", "1");
            var still = Open();
            Assert.DoesNotContain("anim", still.Classes);
            Assert.Null(still.FindControl<TransitioningContentControl>("Pages")!.PageTransition);
            still.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DUSTYBYTES_REDUCED_MOTION", before);
        }
    }

    [AvaloniaFact]
    public void Numbers_Use_Tabular_Figures()
    {
        var window = Open();
        var text = All<TextBlock>(window).First();
        Assert.NotNull(text.FontFeatures);
        Assert.Contains(text.FontFeatures!, f => f.Tag == "tnum" && f.Value == 1);
        window.Close();
    }

    [AvaloniaFact]
    public void Confirm_Dialog_Shows_Danger_Button_Only_For_Irreversible()
    {
        var window = Open();
        var vm = (MainViewModel)window.DataContext!;
        _ = vm.ConfirmAsync("Silinsin mi?", "Geri alınamaz.", "Sil");
        Pump();
        Assert.True(window.FindControl<Button>("ConfirmDanger")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<Button>("ConfirmAccept")!.IsEffectivelyVisible);
        vm.Confirm!.DeclineCommand.Execute(null);
        Pump();
        Assert.False(window.FindControl<Panel>("ConfirmLayer")!.IsVisible);
        window.Close();
    }

    static readonly string[] ProperWords =
    [
        "DustyBytes", "Windows", "Winapp2", "CC-BY-SA-4.0", "Teknesyum", "Destek", "Chrome", "Edge", "DISM", "GitHub", "OneDrive",
    ];

    [Fact]
    public void Ui_Strings_Use_Sentence_Case()
    {
        var root = Path.Combine(Solution(), "src", "DustyBytes.App");
        var files = Directory.EnumerateFiles(Path.Combine(root, "Views"), "*.axaml")
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "ViewModels"), "*.cs"));
        var literal = new Regex("(?:Text|Content|ToolTip\\.Tip|Name)=\"([^\"{]+)\"|\"([A-ZÇĞİÖŞÜ][^\"{}]*\\s[^\"{}]+)\"");
        var failures = new List<string>();
        foreach (var file in files)
            foreach (Match m in literal.Matches(File.ReadAllText(file)))
            {
                var value = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                if (value.Contains('>') || !value.Contains(' ') || value.StartsWith("M", StringComparison.Ordinal) && Regex.IsMatch(value, "^M[\\d ]"))
                    continue;
                var all = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (var w = 1; w < all.Length; w++)
                {
                    var word = all[w];
                    if (all[w - 1].EndsWith('.') || all[w - 1].EndsWith('?') || all[w - 1].EndsWith('!'))
                        continue;
                    var clean = word.Trim('(', ')', ',', '.', ':', ';');
                    if (clean.Length > 1 && char.IsUpper(clean[0]) && !char.IsUpper(clean[1]) && !ProperWords.Contains(clean)
                        && !value.Contains(": ", StringComparison.Ordinal))
                        failures.Add($"{Path.GetFileName(file)}: \"{value}\"");
                }
            }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Distinct()));
    }

    static readonly string[] States = ["", ":pointerover", ":pressed", ":focus-visible"];

    static IEnumerable<string> Overflows(Button button, string state)
    {
        var pseudo = (IPseudoClasses)button.Classes;
        if (state.Length > 0)
            pseudo.Add(state);
        if (state == ":focus-visible")
            pseudo.Add(":focus");
        Pump();
        var found = new List<string>();
        foreach (var part in button.GetSelfAndVisualDescendants().Where(v => v.IsEffectivelyVisible))
        {
            if (part.GetTransformedBounds() is not { } tb)
                continue;
            var rest = new Rect(tb.Bounds.Size).TransformToAABB(tb.Transform);
            if (!tb.Clip.Inflate(0.5).Contains(rest.Deflate(Math.Max(rest.Width, rest.Height) * 0.02 + 1)))
                continue;
            var drawn = tb.Bounds.TransformToAABB(tb.Transform);
            if (part is Border { BoxShadow.Count: > 0 } shadowed)
                drawn = Inflate(drawn, shadowed.BoxShadow);
            if (!tb.Clip.Inflate(0.5).Contains(drawn))
                found.Add($"{button.Name ?? button.Content?.ToString()}{state} {part.GetType().Name}: {drawn} kırpılıyor, kırpma {tb.Clip} [{string.Join(",", part.GetVisualAncestors().Where(v => v.ClipToBounds).Select(v => v.GetType().Name + "#" + (v as Control)?.Name))}]");
        }
        pseudo.Remove(state);
        pseudo.Remove(":focus");
        Pump();
        return found;
    }

    [AvaloniaTheory]
    [InlineData(":pointerover")]
    [InlineData(":pressed")]
    [InlineData(":focus-visible")]
    public void TitleBar_States_Draw_Inside_Their_Clip(string state)
    {
        var window = new MainWindow { Width = 1200, Height = 780 };
        ((MainViewModel)window.DataContext!).Update.State = UpdateState.Available;
        window.Show();
        Pump();

        var bar = window.FindControl<DustyBytes.App.Kabuk.TitleBar>("TitleBar")!;
        var failures = new List<string>();
        foreach (var button in All<Button>(bar))
        {
            failures.AddRange(Overflows(button, state));
            ((IPseudoClasses)button.Classes).Add(state);
            Pump();
            window.CaptureRenderedFrame()?.Save(System.IO.Path.Combine(Shots(), $"ustcubuk-{button.Name}-{state.TrimStart(':')}.png"));
            ((IPseudoClasses)button.Classes).Remove(state);
            Pump();
        }
        window.Close();
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Distinct()));
    }

    [AvaloniaFact]
    public async Task Every_Button_State_Draws_Whole_Glow_And_Border()
    {
        var failures = new List<string>();
        await foreach (var (name, window) in EachScreen())
            foreach (var button in All<Button>(window).Where(b => !InScrollBar(b)).ToList())
                foreach (var state in States)
                    failures.AddRange(Overflows(button, state).Select(f => $"{name}: {f}"));
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Distinct()));
    }

    static Rect Inflate(Rect rect, BoxShadows shadows)
    {
        var result = rect;
        foreach (var shadow in shadows)
            result = result.Union(rect.Translate(new Vector(shadow.OffsetX, shadow.OffsetY)).Inflate(shadow.Blur + shadow.Spread));
        return result;
    }

    static string Shots()
    {
        var dir = System.IO.Path.Combine(Solution(), "tmp", "ui-onarim", "ustcubuk");
        Directory.CreateDirectory(dir);
        return dir;
    }

    static string Solution()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !d.EnumerateFiles("*.slnx").Any())
            d = d.Parent;
        return d!.FullName;
    }
}
