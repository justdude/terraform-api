using TerraformMerge.Cli;
using TerraformMerge.Ui;

namespace TerraformMerge;

internal static class Program
{
    /// <summary>
    /// Entry point. With arguments it runs the headless CLI (convert / merge);
    /// with none it opens the graphical three-pane merge window.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
            return CliRunner.Run(args);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.Run(new MainForm());
        return 0;
    }
}
