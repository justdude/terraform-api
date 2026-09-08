using System.ComponentModel;
using TerraformMerge.Engine;

namespace TerraformMerge.Ui;

/// <summary>
/// The three-pane merge window: Original | Diff | Target, each a list of API
/// methods with its own file path at the bottom. Merge mode aligns two
/// Terraform configs; API mode aligns a Terraform Original against an OpenAPI
/// Target. Saving rewrites the Original file from the operations currently in
/// its list.
/// </summary>
public sealed class MainForm : Form
{
    private readonly OperationSourceLoader _loader = new();
    private readonly MergeEngine _merge = new();
    private readonly OriginalWriter _writer = new();

    private readonly System.ComponentModel.IContainer _components = new System.ComponentModel.Container();

    private readonly BindingList<OperationNode> _original = [];
    private readonly BindingList<DiffEntry> _diff = [];
    private readonly BindingList<OperationNode> _target = [];

    private LoadedOperations _originalLoaded = LoadedOperations.Empty;
    private LoadedOperations _targetLoaded = LoadedOperations.Empty;

    /// <summary>Path the Original was loaded from; Save is rebuilt from that document.</summary>
    private string? _originalLoadedPath;

    private ListBox _lstOriginal = null!;
    private ListBox _lstDiff = null!;
    private ListBox _lstTarget = null!;
    private GroupBox _grpOriginal = null!;
    private GroupBox _grpDiff = null!;
    private GroupBox _grpTarget = null!;
    private TextBox _pathOriginal = null!;
    private TextBox _pathDiff = null!;
    private TextBox _pathTarget = null!;
    private RadioButton _modeMerge = null!;
    private RadioButton _modeApi = null!;
    private ComboBox _envOriginal = null!;
    private ComboBox _envDiff = null!;
    private ComboBox _envTarget = null!;
    private Label _status = null!;

    private readonly ToolTip _tips = new();

    /// <summary>Environment picker entry meaning "leave the operations' environment alone".</summary>
    private const string KeepEnvironment = "(keep as-is)";

    public MainForm()
    {
        Text = "Terraform Merge";
        Width = 1180;
        Height = 760;
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        _components.Add(_tips);

        BuildLayout();
        UpdateStatus("Ready. Load an Original and a Target, then Compute Diff.");
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));   // mode bar
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // panes
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));   // action bar
        Controls.Add(root);

        root.Controls.Add(BuildModeBar(), 0, 0);

        var panes = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1
        };
        panes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        panes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        panes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        root.Controls.Add(panes, 0, 1);

        _grpOriginal = BuildPane("Original", out _lstOriginal, out _pathOriginal, out _envOriginal, LoadOriginal, moveButtons: false);
        _grpDiff = BuildPane("Diff", out _lstDiff, out _pathDiff, out _envDiff, LoadDiff, moveButtons: true);
        _grpTarget = BuildPane("Target", out _lstTarget, out _pathTarget, out _envTarget, LoadTarget, moveButtons: true);
        panes.Controls.Add(_grpOriginal, 0, 0);
        panes.Controls.Add(_grpDiff, 1, 0);
        panes.Controls.Add(_grpTarget, 2, 0);

        _lstOriginal.DataSource = _original;
        _lstOriginal.DisplayMember = nameof(OperationNode.Display);
        _lstDiff.DataSource = _diff;
        _lstDiff.DisplayMember = nameof(DiffEntry.Display);
        _lstTarget.DataSource = _target;
        _lstTarget.DisplayMember = nameof(OperationNode.Display);

        root.Controls.Add(BuildActionBar(), 0, 2);
    }

    private Control BuildModeBar()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        panel.Controls.Add(new Label { Text = "Mode:", AutoSize = true, Padding = new Padding(0, 8, 6, 0) });

        _modeMerge = new RadioButton { Text = "Merge  (Terraform ↔ Terraform)", AutoSize = true, Checked = true, Padding = new Padding(0, 4, 12, 0) };
        _modeApi = new RadioButton { Text = "API  (Terraform ↔ OpenAPI)", AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
        _modeMerge.CheckedChanged += (_, _) => OnModeChanged();
        panel.Controls.Add(_modeMerge);
        panel.Controls.Add(_modeApi);

        panel.Controls.Add(new Label
        {
            Text = "   •   Double-click a row to edit it   •   Env: moves the selected rows",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Padding = new Padding(0, 8, 0, 0)
        });
        return panel;
    }

    private GroupBox BuildPane(
        string title,
        out ListBox list,
        out TextBox path,
        out ComboBox environment,
        EventHandler onLoad,
        bool moveButtons)
    {
        var group = new GroupBox { Text = title, Dock = DockStyle.Fill };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = moveButtons ? 4 : 3,
            Padding = new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // list
        if (moveButtons)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // move buttons
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // environment bar
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));  // path controls
        group.Controls.Add(layout);

        list = new ListBox
        {
            Dock = DockStyle.Fill,
            SelectionMode = SelectionMode.MultiExtended,
            IntegralHeight = false,
            // Monospaced so the padded Method / URL / (id) columns line up.
            Font = new Font("Consolas", 9.5f)
        };
        var localList = list;
        localList.MouseDoubleClick += (_, e) => EditFromDoubleClick(localList, e);

        // A right-click does not move the ListBox selection on its own, so
        // select the row under the cursor before the context menu opens —
        // otherwise Edit… would act on the previously-selected (wrong) row.
        // A row already part of the selection is left alone, so "Set
        // environment" can act on a whole multi-row selection.
        localList.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right)
                return;
            var row = localList.IndexFromPoint(e.Location);
            if (row != ListBox.NoMatches && !localList.SelectedIndices.Contains(row))
                localList.SelectedIndex = row;
        };

        localList.ContextMenuStrip = BuildRowMenu(localList);

        layout.Controls.Add(list, 0, 0);

        var paneRow = 1;

        if (moveButtons)
        {
            var moveBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            var add = new Button
            {
                Text = "◄ Add to Original",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(6, 0, 10, 0) // room so the caption is never clipped
            };
            add.Click += (_, _) => AddSelectedToOriginal(title);
            _tips.SetToolTip(add, "Copy the selected operations into Original, re-stamped for the Original pane's environment.");
            moveBar.Controls.Add(add);
            layout.Controls.Add(moveBar, 0, paneRow);
            paneRow++;
        }

        layout.Controls.Add(BuildEnvironmentBar(localList, isOriginal: !moveButtons, out environment), 0, paneRow);
        paneRow++;

        // The browse/Load columns auto-size to their captions so the text is
        // never clipped (a fixed 60px "Load" column rendered as "Loa").
        var pathBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        path = new TextBox { Dock = DockStyle.Fill };
        var localPath = path;
        var browse = new Button { Text = "…", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 0, 6, 0) };
        browse.Click += (_, _) => BrowseInto(localPath);
        var load = new Button { Text = "Load", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 0, 10, 0) };
        load.Click += onLoad;

        pathBar.Controls.Add(path, 0, 0);
        pathBar.Controls.Add(browse, 1, 0);
        pathBar.Controls.Add(load, 2, 0);
        layout.Controls.Add(pathBar, 0, paneRow);

        return group;
    }

    /// <summary>
    /// The pane's environment row: pick an environment (or type one no loaded
    /// file uses yet) and stamp it on the rows selected in that pane. On the
    /// Original pane the same picker doubles as the <i>destination</i>
    /// environment — what operations added from Diff/Target are re-stamped as.
    /// </summary>
    private Control BuildEnvironmentBar(ListBox list, bool isOriginal, out ComboBox environment)
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        bar.Controls.Add(new Label { Text = "Env:", AutoSize = true, Padding = new Padding(0, 6, 2, 0) });

        var combo = new ComboBox
        {
            Width = 118,
            DropDownStyle = ComboBoxStyle.DropDown, // editable — an environment no file uses yet can be typed
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems,
            Margin = new Padding(0, 2, 6, 0)
        };
        combo.Items.Add(KeepEnvironment);
        combo.Text = KeepEnvironment;
        bar.Controls.Add(combo);

        var apply = new Button
        {
            Text = "Set selected",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6, 0, 10, 0)
        };
        apply.Click += (_, _) => ApplyEnvironmentToSelection(list, combo.Text);
        bar.Controls.Add(apply);

        _tips.SetToolTip(combo, isOriginal
            ? "The Original file's environment: operations added from Diff/Target are re-stamped as this."
            : "The environment to move the selected rows to.");
        _tips.SetToolTip(apply,
            "Rewrite the selected operations for that environment: resource group, APIM instance, api name, operation_id, display name, description.");

        environment = combo;
        return bar;
    }

    /// <summary>
    /// One pane's row menu: edit the row, or move the selected rows to another
    /// environment. The environment list is rebuilt every time the menu opens,
    /// because which environments exist depends on the files currently loaded.
    /// </summary>
    private ContextMenuStrip BuildRowMenu(ListBox list)
    {
        var menu = new ContextMenuStrip();
        _components.Add(menu); // deterministic disposal with the form

        var editItem = new ToolStripMenuItem("Edit…");
        editItem.Click += (_, _) => EditOperationAt(list, list.SelectedIndex);
        menu.Items.Add(editItem);

        var environmentItem = new ToolStripMenuItem("Set environment");
        menu.Items.Add(environmentItem);

        menu.Opening += (_, _) =>
        {
            // Disposing an item removes it from the collection, so the items
            // are copied out first — disposing while iterating throws.
            foreach (var stale in environmentItem.DropDownItems.Cast<ToolStripItem>().ToArray())
                stale.Dispose();
            environmentItem.DropDownItems.Clear();

            foreach (var name in BuildEnvironmentCatalog().Choices())
            {
                var choice = name;
                var item = new ToolStripMenuItem(choice);
                item.Click += (_, _) => ApplyEnvironmentToSelection(list, choice);
                environmentItem.DropDownItems.Add(item);
            }

            environmentItem.Enabled = environmentItem.DropDownItems.Count > 0;
        };

        return menu;
    }

    private Control BuildActionBar()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };

        var computeDiff = new Button { Text = "Compute Diff", AutoSize = true, Height = 30 };
        computeDiff.Click += (_, _) => ComputeDiff();

        var removeFromOriginal = new Button { Text = "Remove from Original", AutoSize = true, Height = 30 };
        removeFromOriginal.Click += (_, _) => RemoveSelectedFromOriginal();

        var save = new Button { Text = "Save Original", AutoSize = true, Height = 30 };
        save.Click += (_, _) => SaveOriginal();

        var convert = new Button { Text = "Convert OpenAPI…", AutoSize = true, Height = 30 };
        convert.Click += (_, _) => ConvertDialog();

        panel.Controls.Add(computeDiff);
        panel.Controls.Add(removeFromOriginal);
        panel.Controls.Add(save);
        panel.Controls.Add(convert);

        _status = new Label { AutoSize = true, Padding = new Padding(12, 8, 0, 0), ForeColor = Color.DimGray };
        panel.Controls.Add(_status);

        return panel;
    }

    // ---------------- editing ----------------

    private void EditFromDoubleClick(ListBox list, MouseEventArgs e)
    {
        var index = list.IndexFromPoint(e.Location);
        if (index == ListBox.NoMatches)
            index = list.SelectedIndex;
        EditOperationAt(list, index);
    }

    /// <summary>
    /// Opens the field editor for one operation. Combo choices are the values
    /// each field takes across both loaded sides, so any value can be accepted
    /// from either side. operation_id is fixed, except when the dialog's
    /// environment picker moves the operation to another environment — that
    /// rewrites the id's environment token.
    /// </summary>
    private void EditOperationAt(ListBox list, int index)
    {
        RunGuarded(() =>
        {
            if (index < 0)
                return;

            var subject =
                ReferenceEquals(list, _lstDiff) ? _diff[index].Operation :
                ReferenceEquals(list, _lstOriginal) ? _original[index] :
                _target[index];

            var catalog = MergeFieldCatalog.Build(_original, _target);
            using var dialog = new OperationEditorDialog(subject, catalog, BuildEnvironmentCatalog());
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            var changed = OperationEditor.Apply(subject, dialog.Edits);
            if (dialog.OperationIdEdit is { } operationId)
                changed |= OperationEditor.ApplyOperationId(subject, operationId);

            if (changed)
            {
                RefreshLists();
                RefreshEnvironmentChoices();
                UpdateStatus($"Edited {subject.Method} {subject.UrlTemplate}. Save Original to persist changes.");
            }
            else
            {
                UpdateStatus("No changes made.");
            }
        });
    }

    // ---------------- actions ----------------

    private void OnModeChanged()
    {
        var api = _modeApi.Checked;
        _grpTarget.Text = api ? "Target (OpenAPI)" : "Target";
        UpdateStatus(api
            ? "API mode: Original is Terraform, Target is an OpenAPI document."
            : "Merge mode: both Original and Target are Terraform configs.");
    }

    private void LoadOriginal(object? sender, EventArgs e)
    {
        RunGuarded(() =>
        {
            var text = ReadPath(_pathOriginal);
            _originalLoaded = _loader.LoadTerraform(text, OperationSource.OriginalTerraform);
            _originalLoadedPath = _pathOriginal.Text;
            Replace(_original, _originalLoaded.Nodes);
            RefreshEnvironmentChoices();
            SuggestEnvironment(_envOriginal, _original);
            UpdateStatus($"Loaded {_original.Count} operation(s) from Original"
                + (EnvironmentCatalog.Dominant(_original) is { } environment ? $" ({environment})." : "."));
        });
    }

    private void LoadTarget(object? sender, EventArgs e)
    {
        RunGuarded(() =>
        {
            var text = ReadPath(_pathTarget);
            _targetLoaded = _modeApi.Checked
                ? _loader.LoadOpenApi(text, OperationSource.TargetOpenApi, _pathTarget.Text)
                : _loader.Load(text, OperationSource.TargetTerraform, OperationSource.TargetOpenApi, _pathTarget.Text);
            Replace(_target, _targetLoaded.Nodes);
            RefreshEnvironmentChoices();
            SuggestEnvironment(_envTarget, _target);
            UpdateStatus($"Loaded {_target.Count} operation(s) from Target"
                + (EnvironmentCatalog.Dominant(_target) is { } environment ? $" ({environment})." : "."));
        });
    }

    private void LoadDiff(object? sender, EventArgs e)
    {
        RunGuarded(() =>
        {
            var text = ReadPath(_pathDiff);
            var loaded = _loader.Load(text, OperationSource.DiffTerraform, OperationSource.TargetOpenApi, _pathDiff.Text);
            _diff.Clear();
            foreach (var node in loaded.Nodes)
                _diff.Add(new DiffEntry(node, DiffKind.Added, null, 0));
            RefreshEnvironmentChoices();
            SuggestEnvironment(_envDiff, loaded.Nodes);
            UpdateStatus($"Loaded {_diff.Count} operation(s) into Diff.");
        });
    }

    private void ComputeDiff()
    {
        RunGuarded(() =>
        {
            var entries = _merge.ComputeDiff(_original.ToList(), _target.ToList());
            _diff.Clear();
            foreach (var entry in entries)
                _diff.Add(entry);
            var added = entries.Count(x => x.Kind == DiffKind.Added);
            var changed = entries.Count(x => x.Kind == DiffKind.Changed);
            UpdateStatus($"Diff: {added} to add, {changed} changed.");
        });
    }

    /// <summary>
    /// Copies the operations selected in Diff/Target into Original, skipping the
    /// ones already present. Every copy is re-stamped for the environment chosen
    /// on the Original pane — the "give qa the operations it is missing from
    /// dev" step, so what lands in the list is a qa operation, id included.
    /// </summary>
    private void AddSelectedToOriginal(string paneTitle)
    {
        RunGuarded(() =>
        {
            var incoming = CollectSelectedNodes(paneTitle);
            var destination = Chosen(_envOriginal);
            var catalog = destination is null ? null : BuildEnvironmentCatalog();

            var added = 0;
            var restamped = 0;
            foreach (var node in incoming)
            {
                if (_original.Any(existing => MergeEngine.AreEquivalent(existing, node)))
                    continue;

                // A copy, so re-stamping it for the Original's environment does
                // not rewrite the row in the pane it was taken from.
                var copy = node.Copy();
                if (destination is not null && EnvironmentRetargeter.Retarget(copy, destination, catalog))
                    restamped++;

                _original.Add(copy);
                added++;
            }

            RefreshEnvironmentChoices();
            var note = restamped > 0 ? $" as {destination} ({restamped} re-stamped)" : "";
            UpdateStatus($"Added {added} operation(s) to Original{note} ({incoming.Count - added} already present).");
        });
    }

    private List<OperationNode> CollectSelectedNodes(string paneTitle)
    {
        if (paneTitle.StartsWith("Diff", StringComparison.Ordinal))
            return _lstDiff.SelectedItems.Cast<DiffEntry>().Select(d => d.Operation).ToList();
        return _lstTarget.SelectedItems.Cast<OperationNode>().ToList();
    }

    private void RemoveSelectedFromOriginal()
    {
        var selected = _lstOriginal.SelectedItems.Cast<OperationNode>().ToList();
        foreach (var node in selected)
            _original.Remove(node);
        UpdateStatus($"Removed {selected.Count} operation(s) from Original.");
    }

    private void SaveOriginal()
    {
        RunGuarded(() =>
        {
            if (_originalLoaded.TerraformDocument is null)
                throw new InvalidOperationException("Load an Original Terraform file first.");
            if (string.IsNullOrWhiteSpace(_pathOriginal.Text))
                throw new InvalidOperationException("Original path is empty.");

            // The output is rebuilt from the LOADED document (its AST and source
            // byte-slices), not from whatever the path box points at now. If the
            // path changed after loading, writing there would overwrite an
            // unrelated file with content spliced from a different document.
            if (!string.Equals(_pathOriginal.Text, _originalLoadedPath, StringComparison.OrdinalIgnoreCase))
            {
                var proceed = MessageBox.Show(this,
                    $"The path changed since loading.\n\nThe saved content is rebuilt from the loaded file " +
                    $"({_originalLoadedPath}), not from the current path. Overwrite\n\n{_pathOriginal.Text}\n\nwith it?",
                    "Save Original", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (proceed != DialogResult.Yes)
                {
                    UpdateStatus("Save cancelled. Load the file you want to edit, or restore its path.");
                    return;
                }
            }

            var text = _writer.Rewrite(_originalLoaded, _original.ToList());
            File.WriteAllText(_pathOriginal.Text, text);
            UpdateStatus($"Saved {_original.Count} operation(s) to {_pathOriginal.Text}");
        });
    }

    private void ConvertDialog()
    {
        RunGuarded(() =>
        {
            using var open = new OpenFileDialog { Title = "Select OpenAPI JSON", Filter = "OpenAPI JSON (*.json)|*.json|All files (*.*)|*.*" };
            if (open.ShowDialog(this) != DialogResult.OK)
                return;

            using var save = new SaveFileDialog { Title = "Save Terraform as", Filter = "Terraform (*.tf)|*.tf|All files (*.*)|*.*", FileName = "apim.tf" };
            if (save.ShowDialog(this) != DialogResult.OK)
                return;

            var facade = TerraformApi.Application.TerraformApiFacade.Create();
            var result = facade.ConvertOpenApiToTerraform(File.ReadAllText(open.FileName), new TerraformApi.Domain.Models.ConversionSettings());
            if (!result.Success)
                throw new InvalidOperationException("Conversion failed: " + string.Join("; ", result.Errors));

            File.WriteAllText(save.FileName, result.TerraformConfig);
            UpdateStatus($"Converted {open.FileName} -> {save.FileName}");
        });
    }

    // ---------------- environments ----------------

    /// <summary>
    /// Moves every operation selected in one pane to <paramref name="environment"/>:
    /// its resource group, APIM instance, api name, operation id, display name
    /// and description are re-stamped (see <see cref="EnvironmentRetargeter"/>).
    /// Nothing else — method, URL and status code are the route, not the
    /// environment — and interpolated values are left variable.
    /// </summary>
    private void ApplyEnvironmentToSelection(ListBox list, string? environment)
    {
        RunGuarded(() =>
        {
            var destination = Chosen(environment);
            if (destination is null)
            {
                UpdateStatus("Pick an environment first — or type one the loaded files do not use yet.");
                return;
            }

            var selected = SelectedNodes(list);
            if (selected.Count == 0)
            {
                UpdateStatus("Select the operation(s) whose environment you want to set.");
                return;
            }

            var catalog = BuildEnvironmentCatalog();
            var moved = selected.Count(node => EnvironmentRetargeter.Retarget(node, destination, catalog));

            RefreshLists();
            RefreshEnvironmentChoices();
            var tail = moved > 0 && ReferenceEquals(list, _lstOriginal) ? " Save Original to persist." : "";
            UpdateStatus($"Moved {moved} of {selected.Count} selected operation(s) to {destination}.{tail}");
        });
    }

    /// <summary>The environments across both sides — what the pickers offer and what a retarget copies values from.</summary>
    private EnvironmentCatalog BuildEnvironmentCatalog() =>
        EnvironmentCatalog.Build(_original, _target.Concat(_diff.Select(entry => entry.Operation)));

    /// <summary>Repopulates every picker from the loaded files, keeping each one's current choice.</summary>
    private void RefreshEnvironmentChoices()
    {
        var choices = BuildEnvironmentCatalog().Choices();

        foreach (var combo in new[] { _envOriginal, _envDiff, _envTarget })
        {
            var current = combo.Text;
            combo.BeginUpdate();
            combo.Items.Clear();
            combo.Items.Add(KeepEnvironment);
            foreach (var choice in choices)
                combo.Items.Add(choice);
            combo.EndUpdate();
            combo.Text = string.IsNullOrWhiteSpace(current) ? KeepEnvironment : current; // Items.Clear() blanks Text
        }
    }

    /// <summary>Preselects the environment a freshly loaded pane is actually in.</summary>
    private static void SuggestEnvironment(ComboBox combo, IEnumerable<OperationNode> nodes) =>
        combo.Text = EnvironmentCatalog.Dominant(nodes) ?? KeepEnvironment;

    /// <summary>The picker's value as an environment name, or null for "(keep as-is)" / blank.</summary>
    private static string? Chosen(ComboBox combo) => Chosen(combo.Text);

    private static string? Chosen(string? text)
    {
        var value = (text ?? "").Trim();
        return value.Length == 0 || value == KeepEnvironment ? null : value;
    }

    /// <summary>The operations selected in one pane (a Diff row wraps its operation).</summary>
    private List<OperationNode> SelectedNodes(ListBox list) =>
        ReferenceEquals(list, _lstDiff)
            ? _lstDiff.SelectedItems.Cast<DiffEntry>().Select(entry => entry.Operation).ToList()
            : list.SelectedItems.Cast<OperationNode>().ToList();

    /// <summary>
    /// Re-reads every list's labels: the same node can appear in more than one
    /// pane (a Diff entry wraps a Target node), so all three are refreshed.
    /// </summary>
    private void RefreshLists()
    {
        _original.ResetBindings();
        _diff.ResetBindings();
        _target.ResetBindings();
    }

    // ---------------- helpers ----------------

    private static void BrowseInto(TextBox target)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select file",
            Filter = "Terraform / OpenAPI (*.tf;*.json)|*.tf;*.json|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == DialogResult.OK)
            target.Text = dialog.FileName;
    }

    private static string ReadPath(TextBox box)
    {
        if (string.IsNullOrWhiteSpace(box.Text))
            throw new InvalidOperationException("Path is empty.");
        if (!File.Exists(box.Text))
            throw new FileNotFoundException("File not found: " + box.Text);
        return File.ReadAllText(box.Text);
    }

    private static void Replace(BindingList<OperationNode> list, IReadOnlyList<OperationNode> nodes)
    {
        list.Clear();
        foreach (var node in nodes)
            list.Add(node);
    }

    private void RunGuarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Terraform Merge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            UpdateStatus("Error: " + ex.Message);
        }
    }

    private void UpdateStatus(string message) => _status.Text = message;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _components.Dispose(); // disposes the per-pane context menus + items
        base.Dispose(disposing);
    }
}
