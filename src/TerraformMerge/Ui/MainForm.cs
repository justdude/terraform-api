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
    private Label _status = null!;

    public MainForm()
    {
        Text = "Terraform Merge";
        Width = 1180;
        Height = 760;
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

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

        _grpOriginal = BuildPane("Original", out _lstOriginal, out _pathOriginal, LoadOriginal, moveButtons: false);
        _grpDiff = BuildPane("Diff", out _lstDiff, out _pathDiff, LoadDiff, moveButtons: true);
        _grpTarget = BuildPane("Target", out _lstTarget, out _pathTarget, LoadTarget, moveButtons: true);
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
            Text = "   •   Double-click a row to edit its fields (choose values from either side)",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Padding = new Padding(0, 8, 0, 0)
        });
        return panel;
    }

    private GroupBox BuildPane(string title, out ListBox list, out TextBox path, EventHandler onLoad, bool moveButtons)
    {
        var group = new GroupBox { Text = title, Dock = DockStyle.Fill };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = moveButtons ? 3 : 2,
            Padding = new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // list
        if (moveButtons)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); // move buttons
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
        localList.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right)
                return;
            var row = localList.IndexFromPoint(e.Location);
            if (row != ListBox.NoMatches)
                localList.SelectedIndex = row;
        };

        var menu = new ContextMenuStrip();
        _components.Add(menu); // deterministic disposal with the form
        var editItem = new ToolStripMenuItem("Edit…");
        editItem.Click += (_, _) => EditOperationAt(localList, localList.SelectedIndex);
        menu.Items.Add(editItem);
        localList.ContextMenuStrip = menu;

        layout.Controls.Add(list, 0, 0);

        if (moveButtons)
        {
            var moveBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            var add = new Button { Text = "◄ Add to Original", AutoSize = true };
            add.Click += (_, _) => AddSelectedToOriginal(title);
            moveBar.Controls.Add(add);
            layout.Controls.Add(moveBar, 0, 1);
        }

        var pathBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));

        path = new TextBox { Dock = DockStyle.Fill };
        var localPath = path;
        var browse = new Button { Text = "…", Dock = DockStyle.Fill };
        browse.Click += (_, _) => BrowseInto(localPath);
        var load = new Button { Text = "Load", Dock = DockStyle.Fill };
        load.Click += onLoad;

        pathBar.Controls.Add(path, 0, 0);
        pathBar.Controls.Add(browse, 1, 0);
        pathBar.Controls.Add(load, 2, 0);
        layout.Controls.Add(pathBar, 0, moveButtons ? 2 : 1);

        return group;
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
    /// from either side. operation_id is fixed.
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
            using var dialog = new OperationEditorDialog(subject, catalog);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            if (OperationEditor.Apply(subject, dialog.Edits))
            {
                // The same node instance may appear in more than one pane
                // (Diff entries wrap Target nodes), so refresh all three.
                _original.ResetBindings();
                _diff.ResetBindings();
                _target.ResetBindings();
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
            Replace(_original, _originalLoaded.Nodes);
            UpdateStatus($"Loaded {_original.Count} operation(s) from Original.");
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
            UpdateStatus($"Loaded {_target.Count} operation(s) from Target.");
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

    private void AddSelectedToOriginal(string paneTitle)
    {
        RunGuarded(() =>
        {
            var incoming = CollectSelectedNodes(paneTitle);
            var added = 0;
            foreach (var node in incoming)
            {
                if (_original.Any(existing => MergeEngine.AreEquivalent(existing, node)))
                    continue;
                _original.Add(node);
                added++;
            }
            UpdateStatus($"Added {added} operation(s) to Original ({incoming.Count - added} already present).");
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
