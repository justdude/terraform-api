using TerraformMerge.Engine;

namespace TerraformMerge.Ui;

/// <summary>
/// Per-operation field editor. Every editable field is an editable combo box
/// whose drop-down lists the distinct values that field takes across <b>both
/// sides</b> of the merge (from <see cref="MergeFieldCatalog"/>), so a value can
/// be accepted from either side or typed fresh.
///
/// The <b>Environment</b> picker at the top is the shortcut for the whole set:
/// choosing one previews the operation as it would look in that environment
/// (resource group, APIM instance, api name, operation id, display name,
/// description), taking the destination environment's own values from the loaded
/// files where it has them. <c>operation_id</c> is otherwise fixed — it is the
/// operation's identity and is never merged from the other side.
/// </summary>
public sealed class OperationEditorDialog : Form
{
    private readonly Dictionary<OperationField, ComboBox> _editors = new();
    private readonly OperationNode _subject;
    private readonly EnvironmentCatalog _environments;

    private ComboBox _environment = null!;
    private TextBox _operationId = null!;
    private string _plannedOperationId;
    private string _previewedEnvironment;

    /// <summary>Field → chosen value. Populated only when the dialog is accepted.</summary>
    public IReadOnlyDictionary<OperationField, string> Edits { get; private set; } =
        new Dictionary<OperationField, string>();

    /// <summary>
    /// The new operation id when an environment move renamed it, else null.
    /// Populated only when the dialog is accepted.
    /// </summary>
    public string? OperationIdEdit { get; private set; }

    public OperationEditorDialog(
        OperationNode subject,
        MergeFieldCatalog catalog,
        EnvironmentCatalog? environments = null)
    {
        _subject = subject;
        _environments = environments ?? EnvironmentCatalog.Empty;
        _plannedOperationId = subject.OperationId;
        _previewedEnvironment = subject.Environment ?? "";

        Text = "Edit operation";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(560, 500);
        MinimumSize = new Size(460, 420);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));  // header
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // fields
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));  // buttons
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray,
            Text = "Each value can be picked from either side or typed. " +
                   "Picking an environment refills the fields below for that environment " +
                   "— including operation_id, which is otherwise fixed."
        }, 0, 0);

        root.Controls.Add(BuildFields(subject, catalog), 0, 1);
        root.Controls.Add(BuildButtons(), 0, 2);
    }

    private Control BuildFields(OperationNode subject, MergeFieldCatalog catalog)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var row = 0;

        // Environment — moves the operation as a whole; drives every other row.
        grid.Controls.Add(FieldLabel("Environment"), 0, row);
        _environment = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDown, // editable — an environment no file uses yet can be typed
            Margin = new Padding(3, 3, 3, 6),
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems
        };
        foreach (var choice in _environments.Choices())
            _environment.Items.Add(choice);
        _environment.Text = subject.Environment ?? "";
        _environment.SelectedIndexChanged += (_, _) => PreviewEnvironment();
        _environment.Leave += (_, _) => PreviewEnvironment(); // a typed environment applies when focus moves on
        grid.Controls.Add(_environment, 1, row);
        row++;

        // operation_id — identity, read-only; only an environment move rewrites it.
        grid.Controls.Add(FieldLabel("operation_id"), 0, row);
        _operationId = new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Text = subject.OperationId,
            BackColor = SystemColors.Control,
            Margin = new Padding(3, 3, 3, 6)
        };
        grid.Controls.Add(_operationId, 1, row);
        row++;

        foreach (var info in OperationFieldInfo.All)
        {
            grid.Controls.Add(FieldLabel(info.Label), 0, row);

            var current = info.Get(subject);
            var combo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDown, // editable
                Margin = new Padding(3, 3, 3, 6),
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems
            };

            // Choices = both sides' distinct values, with the current value
            // guaranteed present so the field always round-trips unchanged.
            foreach (var value in Choices(catalog.Values(info.Field), current))
                combo.Items.Add(value);
            combo.Text = current;

            _editors[info.Field] = combo;
            grid.Controls.Add(combo, 1, row);
            row++;
        }

        return grid;
    }

    /// <summary>
    /// Shows the operation as the chosen environment would have it. Every field
    /// is refilled from the operation's own value first, so switching the picker
    /// twice previews the second environment instead of compounding onto the
    /// first preview.
    /// </summary>
    private void PreviewEnvironment()
    {
        // Only when the environment actually changed: refilling the fields on
        // every focus change would discard values typed into them by hand.
        var chosen = _environment.Text ?? "";
        if (string.Equals(chosen, _previewedEnvironment, StringComparison.Ordinal))
            return;
        _previewedEnvironment = chosen;

        var plan = EnvironmentRetargeter.Plan(_subject, chosen, _environments);

        foreach (var info in OperationFieldInfo.All)
        {
            _editors[info.Field].Text = plan.Fields.TryGetValue(info.Field, out var value)
                ? value
                : info.Get(_subject);
        }

        _plannedOperationId = plan.OperationId;
        _operationId.Text = plan.OperationId;
    }

    private static IEnumerable<string> Choices(IReadOnlyList<string> catalogValues, string current)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(current) && seen.Add(current))
            yield return current;
        foreach (var value in catalogValues)
            if (seen.Add(value))
                yield return value;
    }

    private Control BuildButtons()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };

        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, Margin = new Padding(6, 6, 0, 0) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Margin = new Padding(6, 6, 6, 0) };
        ok.Click += (_, e) =>
        {
            if (!ValidateStatusCode())
            {
                // Keep the dialog open so the user can fix the value.
                DialogResult = DialogResult.None;
                return;
            }
            CollectEdits();
        };

        bar.Controls.Add(cancel);
        bar.Controls.Add(ok);

        AcceptButton = ok;
        CancelButton = cancel;
        return bar;
    }

    private void CollectEdits()
    {
        var edits = new Dictionary<OperationField, string>();
        foreach (var (field, combo) in _editors)
            edits[field] = combo.Text ?? "";
        Edits = edits;

        OperationIdEdit = string.Equals(_plannedOperationId, _subject.OperationId, StringComparison.Ordinal)
            ? null
            : _plannedOperationId;
    }

    /// <summary>
    /// A non-empty status code that is not an integer would blank the field on
    /// apply; reject it here with feedback instead. Blank (clear) is allowed.
    /// </summary>
    private bool ValidateStatusCode()
    {
        if (!_editors.TryGetValue(OperationField.StatusCode, out var combo))
            return true;

        var text = (combo.Text ?? "").Trim();
        if (text.Length == 0 || int.TryParse(text, out _))
            return true;

        MessageBox.Show(this,
            $"Status code must be a whole number (or left blank). '{text}' is not valid.",
            "Edit operation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        combo.Focus();
        return false;
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0, 3, 6, 6)
    };
}
