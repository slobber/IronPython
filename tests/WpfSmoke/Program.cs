using System;
using System.IO;
using IronPython.Hosting;

namespace IronPython.WpfSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var stdlibRoot = FindStdLibRoot(args);
        var engine = Python.CreateEngine();
        engine.SetSearchPaths([Path.Combine(stdlibRoot, "lib")]);
        engine.Execute("""
            import clr
            clr.AddReference('IronPython.Wpf')
            import wpf
            from System.IO import StringReader
            from System.Windows.Controls import Button

            root = Button()
            root.Content = 'WPF smoke'
            assert wpf.__name__ == 'wpf'
            assert root.Content == 'WPF smoke'

            xaml = '<Button xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Content="WPF XAML smoke"/>'
            loaded = wpf.LoadComponent(Button(), StringReader(xaml))
            assert loaded.Content == 'WPF XAML smoke'
            """);

        Console.WriteLine("WPF_XAML_OK");
        return 0;
    }

    private static string FindStdLibRoot(string[] args)
    {
        string?[] candidates =
        [
            args.Length > 0 ? args[0] : null,
            Environment.GetEnvironmentVariable("CYBERSTONE_IRONPYTHON_STDLIB"),
            Path.Combine(FindRepositoryRoot(), "src", "core", "IronPython.StdLib"),
        ];

        foreach (var candidate in candidates)
        {
            if (candidate is not null && Directory.Exists(Path.Combine(candidate, "lib")))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new DirectoryNotFoundException(
            "Set CYBERSTONE_IRONPYTHON_STDLIB or pass the IronPython.StdLib checkout as the first argument.");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the IronPython repository root.");
    }
}
