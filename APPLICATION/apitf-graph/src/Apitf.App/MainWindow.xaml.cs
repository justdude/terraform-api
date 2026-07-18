using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Apitf.Core;

namespace Apitf.App;

public partial class MainWindow : Window
{
    private SpecModel? _base;
    private SpecModel? _ours;     // current working spec; also the merge scaffold
    private SpecModel? _theirs;
    private MergeResult? _merge;

    // Catppuccin-ish palette
    private static readonly Brush CUnchanged = Brush("#6C7086");
    private static readonly Brush CAdded     = Brush("#A6E3A1");
    private static readonly Brush CModified  = Brush("#F9E2AF");
    private static readonly Brush CRemoved   = Brush("#FAB387");
    private static readonly Brush CConflict  = Brush("#F38BA8");
    private static readonly Brush CRoot      = Brush("#89B4FA");
    private static readonly Brush CPath      = Brush("#74C7EC");
    private static readonly Brush CText      = Brush("#11111B");
    private static readonly Brush CEdge      = Brush("#45475A");

    public MainWindow()
    {
        InitializeComponent();
        RedrawAll();
    }

    // ---------------- file loading ----------------

    private SpecModel? Open(string title)
    {
        var dlg = new OpenFileDialog
        {
            Title = title,
            Filter = "OpenAPI (*.json;*.yaml;*.yml)|*.json;*.yaml;*.yml|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            try { return SpecModel.Load(dlg.FileName); }
            catch (Exception ex) { MessageBox.Show($"Load failed:\n{ex.Message}", "apitf-graph"); }
        }
        return null;
    }

    private void LoadBase_Click(object sender, RoutedEventArgs e)
    {
        var v = Open("Select BASE spec (common ancestor)");
        if (v != null) { _base = v; _merge = null; Status("Base loaded."); RedrawAll(); }
    }

    private void LoadOurs_Click(object sender, RoutedEventArgs e)
    {
        var v = Open("Select CURRENT (ours) spec");
        if (v != null) { _ours = v; _merge = null; Status("Current (ours) loaded."); RedrawAll(); }
    }

    private void LoadTheirs_Click(object sender, RoutedEventArgs e)
    {
        var v = Open("Select INCOMING (theirs) spec");
        if (v != null) { _theirs = v; _merge = null; Status("Incoming (theirs) loaded."); RedrawAll(); }
    }

    private void NewScratch_Click(object sender, RoutedEventArgs e)
    {
        _ours = SpecEditor.NewSpec("New API");
        _base = null; _theirs = null; _merge = null;
        ConflictPanel.Children.Clear();
        Status("New empty spec created. Use Add method to build it from scratch.");
        RedrawAll();
    }

    // ---------------- editing ----------------

    private void AddMethod_Click(object sender, RoutedEventArgs e)
    {
        _ours ??= SpecEditor.NewSpec("New API");
        var method = SelectedMethod();
        var path = TxtPath.Text.Trim();
        if (path.Length == 0) { Status("Path is required."); return; }
        var summary = TxtSummary.Text.Trim();
        try
        {
            var added = SpecEditor.AddOperation(_ours, method, path,
                summary: string.IsNullOrEmpty(summary) ? null : summary);
            _merge = null;
            Status(added ? $"Added {method} {path}." : $"{method} {path} already exists (no change).");
            RedrawAll();
        }
        catch (Exception ex) { Status(ex.Message); }
    }

    private void RemoveMethod_Click(object sender, RoutedEventArgs e)
    {
        if (_ours == null) { Status("Nothing to remove."); return; }
        var method = SelectedMethod();
        var path = TxtPath.Text.Trim();
        var removed = SpecEditor.RemoveOperation(_ours, method, path);
        _merge = null;
        Status(removed ? $"Removed {method} {path}." : $"{method} {path} not found.");
        RedrawAll();
    }

    private string SelectedMethod() =>
        (CmbMethod.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "GET";

    // ---------------- merge ----------------

    private void Merge_Click(object sender, RoutedEventArgs e)
    {
        if (_base == null || _ours == null || _theirs == null)
        {
            Status("Load Base, Current and Incoming specs before merging.");
            return;
        }
        _merge = ThreeWayMerger.Merge(_base, _ours, _theirs);
        BuildConflictPanel();
        Status($"Merge ready: {_merge.AutoMergedCount} auto-merged, {_merge.ConflictCount} conflict(s). " +
               (_merge.HasConflicts ? "Resolve conflicts, then Apply." : "No conflicts — press Apply."));
        RedrawAll();
    }

    private void ApplyResolutions_Click(object sender, RoutedEventArgs e)
    {
        if (_merge == null) { Status("Run Merge first."); return; }
        if (_merge.HasConflicts) { Status("Resolve all conflicts before applying."); return; }
        try
        {
            var merged = _merge.Build();
            _ours = merged; _base = null; _theirs = null; _merge = null;
            ConflictPanel.Children.Clear();
            Status("Merge applied. The result is now the current spec — Save it or Generate Terraform.");
            RedrawAll();
        }
        catch (Exception ex) { Status("Build failed: " + ex.Message); }
    }

    private void BuildConflictPanel()
    {
        ConflictPanel.Children.Clear();
        if (_merge == null) return;

        foreach (var c in _merge.Conflicts)
        {
            var box = new Border
            {
                Background = Brush("#252537"),
                BorderBrush = CConflict,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(8)
            };
            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = c.Key.ToString(),
                Foreground = Brush("#CDD6F4"),
                FontWeight = FontWeights.Bold
            });
            stack.Children.Add(new TextBlock
            {
                Text = "kind: " + c.Kind,
                Foreground = CConflict,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var group = "grp_" + c.Key.ToString().GetHashCode();
            foreach (var (label, res, enabled) in new[]
                     {
                         ("Keep ours", Resolution.TakeOurs,  c.Ours != null),
                         ("Take theirs", Resolution.TakeTheirs, c.Theirs != null),
                         ("Keep base", Resolution.TakeBase,  c.Base != null),
                         ("Delete operation", Resolution.Delete, true)
                     })
            {
                var rb = new RadioButton
                {
                    Content = label,
                    GroupName = group,
                    Foreground = Brush("#BAC2DE"),
                    IsEnabled = enabled,
                    Margin = new Thickness(0, 1, 0, 1),
                    Tag = c
                };
                var captured = res;
                rb.Checked += (_, _) => { c.Resolution = captured; OnResolutionChanged(); };
                stack.Children.Add(rb);
            }

            box.Child = stack;
            ConflictPanel.Children.Add(box);
        }
    }

    private void OnResolutionChanged()
    {
        if (_merge == null) return;
        var remaining = _merge.Conflicts.Count(x => x.Resolution == Resolution.Unresolved);
        Status(remaining == 0
            ? "All conflicts resolved — press Apply resolutions."
            : $"{remaining} conflict(s) still unresolved.");
        RedrawAll();
    }

    // ---------------- save / terraform ----------------

    private void SaveSpec_Click(object sender, RoutedEventArgs e)
    {
        if (_ours == null) { Status("No spec to save."); return; }
        var dlg = new SaveFileDialog
        {
            Title = "Save OpenAPI spec",
            Filter = "JSON (*.json)|*.json|YAML (*.yaml)|*.yaml",
            FileName = "api.json"
        };
        if (dlg.ShowDialog() == true)
        {
            try { _ours.Save(dlg.FileName); Status("Saved " + dlg.FileName); }
            catch (Exception ex) { Status("Save failed: " + ex.Message); }
        }
    }

    private void GenerateTf_Click(object sender, RoutedEventArgs e)
    {
        if (_ours == null) { Status("No spec to generate from."); return; }
        var dlg = new SaveFileDialog
        {
            Title = "Generate Terraform JSON",
            Filter = "Terraform JSON (*.tf.json)|*.tf.json",
            FileName = "api.tf.json"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                var apiName = System.IO.Path.GetFileName(dlg.FileName).Replace(".tf.json", "").Replace(".json", "");
                if (apiName.Length == 0) apiName = "api";
                var specPath = $"specs/{apiName}.json";
                var envs = TxtEnvs.Text.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var tf = envs.Length > 0
                    ? TerraformGenerator.GenerateMultiEnvTfJson(_ours, apiName, specPath, envs)
                    : TerraformGenerator.GenerateTfJson(_ours, apiName, specPath);
                File.WriteAllText(dlg.FileName, tf);
                Status(envs.Length > 0
                    ? $"Generated {dlg.FileName} for {envs.Length} env(s): {string.Join(", ", envs)} (shared spec {specPath})."
                    : $"Generated {dlg.FileName} (references {specPath}).");
            }
            catch (Exception ex) { Status("Generate failed: " + ex.Message); }
        }
    }

    // ---------------- graph drawing ----------------

    private void RedrawAll()
    {
        if (_merge != null) DrawMergeGraph();
        else if (_ours != null) DrawSpecGraph(_ours);
        else GraphCanvas.Children.Clear();
    }

    private sealed record Node(string Path, string Method, Brush Color, string Tip);

    private void DrawSpecGraph(SpecModel spec)
    {
        var nodes = spec.Operations()
            .Select(o => new Node(o.Key.Path, o.Key.Method.ToUpperInvariant(), CAdded,
                                   Summary(o.Node)))
            .ToList();
        Draw(nodes, "Current spec");
    }

    private void DrawMergeGraph()
    {
        if (_merge == null) return;
        var nodes = new List<Node>();
        foreach (var entry in _merge.Entries)
        {
            Brush color;
            if (entry.Conflicted) color = CConflict;
            else if (entry.OursVsBase == ChangeType.Added || entry.TheirsVsBase == ChangeType.Added) color = CAdded;
            else if (entry.OursVsBase == ChangeType.Removed || entry.TheirsVsBase == ChangeType.Removed) color = CRemoved;
            else if (entry.OursVsBase == ChangeType.Modified || entry.TheirsVsBase == ChangeType.Modified) color = CModified;
            else color = CUnchanged;

            var tip = $"ours: {entry.OursVsBase}, theirs: {entry.TheirsVsBase}" +
                      (entry.Conflicted ? " — CONFLICT" : "");
            nodes.Add(new Node(entry.Key.Path, entry.Key.Method.ToUpperInvariant(), color, tip));
        }
        Draw(nodes, "Three-way merge");
    }

    private void Draw(List<Node> nodes, string rootLabel)
    {
        GraphCanvas.Children.Clear();
        DrawLegend();

        const double xRoot = 40, xPath = 300, xMethod = 560;
        const double rowH = 46, nodeH = 30;
        double y = 70;

        var byPath = nodes.GroupBy(n => n.Path).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();

        // total height to center the root
        double totalRows = byPath.Sum(g => Math.Max(1, g.Count()));
        double contentHeight = totalRows * rowH;
        var rootCenter = new Point(xRoot + 90, y + contentHeight / 2);

        var rootNode = AddNode(xRoot, rootCenter.Y - nodeH / 2, 180, nodeH, rootLabel, CRoot, "API");

        foreach (var grp in byPath)
        {
            var methods = grp.ToList();
            double bandHeight = Math.Max(1, methods.Count) * rowH;
            double bandCenterY = y + bandHeight / 2;

            var pathCenter = AddNode(xPath, bandCenterY - nodeH / 2, 230, nodeH, grp.Key, CPath, grp.Key);
            AddEdge(rootNode, pathCenter);

            double my = y;
            foreach (var n in methods)
            {
                var label = $"{n.Method}  {n.Path}";
                var mc = AddNode(xMethod, my + (rowH - nodeH) / 2, 230, nodeH, label, n.Color, n.Tip);
                AddEdge(pathCenter, mc);
                my += rowH;
            }
            y += bandHeight;
        }

        GraphCanvas.Height = Math.Max(1500, y + 80);
    }

    private Point AddNode(double x, double y, double w, double h, string text, Brush fill, string tip)
    {
        var border = new Border
        {
            Width = w,
            Height = h,
            Background = fill,
            CornerRadius = new CornerRadius(5),
            ToolTip = tip
        };
        var tb = new TextBlock
        {
            Text = text,
            Foreground = CText,
            FontSize = 12,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        border.Child = tb;
        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);
        GraphCanvas.Children.Add(border);
        return new Point(x + w, y + h / 2); // right-center anchor for edges
    }

    private void AddEdge(Point from, Point to)
    {
        var line = new Line
        {
            X1 = from.X, Y1 = from.Y,
            X2 = to.X - 230, Y2 = to.Y, // to-node left edge (width 230)
            Stroke = CEdge,
            StrokeThickness = 1.5
        };
        GraphCanvas.Children.Add(line);
    }

    private void DrawLegend()
    {
        var items = new (string, Brush)[]
        {
            ("unchanged", CUnchanged), ("added", CAdded), ("modified", CModified),
            ("removed", CRemoved), ("conflict", CConflict)
        };
        double x = 40;
        foreach (var (label, brush) in items)
        {
            var sw = new Border { Width = 14, Height = 14, Background = brush, CornerRadius = new CornerRadius(3) };
            Canvas.SetLeft(sw, x); Canvas.SetTop(sw, 24);
            GraphCanvas.Children.Add(sw);
            var tb = new TextBlock { Text = label, Foreground = Brush("#BAC2DE"), FontSize = 11 };
            Canvas.SetLeft(tb, x + 18); Canvas.SetTop(tb, 23);
            GraphCanvas.Children.Add(tb);
            x += 18 + label.Length * 7 + 22;
        }
    }

    private static string Summary(System.Text.Json.Nodes.JsonNode? node) =>
        (node as System.Text.Json.Nodes.JsonObject)?["summary"]?.GetValue<string>() ?? "";

    // ---------------- helpers ----------------

    private void Status(string msg) => StatusText.Text = msg;

    private static SolidColorBrush Brush(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
