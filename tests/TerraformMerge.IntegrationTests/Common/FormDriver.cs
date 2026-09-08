using System.Reflection;
using System.Runtime.Versioning;
using System.Windows.Forms;
using TerraformMerge.Ui;

namespace TerraformMerge.IntegrationTests.Common;

/// <summary>
/// Drives a real <see cref="MainForm"/> on an STA thread through its own private
/// handler methods and fields (via reflection), so UI features are exercised
/// through the exact code path the buttons invoke — without changing the app's
/// visibility for tests. Only non-modal handlers are driven; anything that opens
/// a dialog (Edit, Convert, Browse) or a MessageBox (error/guard paths) would
/// block a headless thread and is covered elsewhere.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class FormDriver
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>Runs <paramref name="body"/> on an STA thread; rethrows any failure.</summary>
    public static void RunSta(Action body)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { captured = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (captured is not null)
            throw new Exception("STA test failed: " + captured.Message, captured);
    }

    /// <summary>Runs <paramref name="body"/> against a fresh MainForm on an STA thread; rethrows failures.</summary>
    public static void Run(Action<MainForm> body) => RunSta(() =>
    {
        using var form = new MainForm();
        _ = form.Handle; // realize the control tree so list binding/selection work
        body(form);
    });

    public static void Invoke(MainForm form, string method, params object?[] args) =>
        typeof(MainForm).GetMethod(method, Flags)!.Invoke(form, args);

    public static T Field<T>(MainForm form, string name) =>
        (T)typeof(MainForm).GetField(name, Flags)!.GetValue(form)!;

    public static void SetText(MainForm form, string fieldName, string value) =>
        Field<TextBox>(form, fieldName).Text = value;
}
