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

    private readonly BindingList<OperationNode> _original = [];
    private readonly BindingList<DiffEntry> _diff = [];
    private readonly BindingList<OperationNode> _target = [];

    private LoadedOperations _originalLoaded = LoadedOperations.Empty;
    private LoadedOperations _targetLoaded = LoadedOperations.Empty;

    private ListBox _lstOriginal = null!;
    private ListBox _lstDiff = null!;
    private ListBox _lstTarget = null!;
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

        panes.Controls.Add(BuildPane("Original", out _lstOriginal, out _pathOriginal, LoadOriginal, moveButtons: false), 0, 0);
        panes.Controls.Add(BuildPane("Diff", out _lstDiff, out _pathDiff, LoadDiff, moveButtons: true), 1, 0);
        panes.Controls.Add(BuildPane("Target", out _lstTarget, out _pathTarget, LoadTarget, moveButtons: true), 2, 0);

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
        return panel;
    }

    private Control BuildPane(string title, out ListBox list, out TextBox path, EventHandler onLoad, bool moveButtons)
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

        list = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, IntegralHeight = false };
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

    // ---------------- actions ----------------

    private void OnModeChanged()
    {
        var api = _modeApi.Checked;
        _lstTarget.Parent!.Text = api ? "Target (OpenAPI)" : "Target";
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
}
