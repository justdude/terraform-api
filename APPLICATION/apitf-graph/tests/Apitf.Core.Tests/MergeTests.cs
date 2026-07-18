using Apitf.Core;
using Xunit;

namespace Apitf.Core.Tests;

public class MergeTests
{
    private static SpecModel Spec(params (string verb, string path, string summary)[] ops)
    {
        var s = SpecEditor.NewSpec("T");
        foreach (var (v, p, sm) in ops)
            SpecEditor.AddOperation(s, v, p, summary: sm);
        return s;
    }

    [Fact]
    public void CleanMerge_NoChanges_NoConflicts()
    {
        var b = Spec(("get", "/orders", "List"));
        var o = Spec(("get", "/orders", "List"));
        var t = Spec(("get", "/orders", "List"));

        var r = ThreeWayMerger.Merge(b, o, t);

        Assert.False(r.HasConflicts);
        Assert.Empty(r.Conflicts);
        var merged = r.Build();
        Assert.Single(merged.Operations());
    }

    [Fact]
    public void TheirsAdds_OursUnchanged_AutoTakesTheirs()
    {
        var b = Spec(("get", "/orders", "List"));
        var o = Spec(("get", "/orders", "List"));
        var t = Spec(("get", "/orders", "List"), ("post", "/orders", "Create"));

        var r = ThreeWayMerger.Merge(b, o, t);
        Assert.False(r.HasConflicts);

        var merged = r.Build().OperationMap();
        Assert.Contains(new OperationKey("/orders", "post"), merged.Keys);
        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void OursAdds_TheirsUnchanged_AutoTakesOurs()
    {
        var b = Spec(("get", "/orders", "List"));
        var o = Spec(("get", "/orders", "List"), ("delete", "/orders/{id}", "Del"));
        var t = Spec(("get", "/orders", "List"));

        var r = ThreeWayMerger.Merge(b, o, t);
        Assert.False(r.HasConflicts);
        Assert.Contains(new OperationKey("/orders/{id}", "delete"), r.Build().OperationMap().Keys);
    }

    [Fact]
    public void BothAddIdentical_NoConflict()
    {
        var b = Spec(("get", "/orders", "List"));
        var o = Spec(("get", "/orders", "List"), ("post", "/orders", "Create"));
        var t = Spec(("get", "/orders", "List"), ("post", "/orders", "Create"));

        var r = ThreeWayMerger.Merge(b, o, t);
        Assert.False(r.HasConflicts);
        Assert.Equal(2, r.Build().Operations().Count());
    }

    [Fact]
    public void AddAdd_DifferentContent_Conflicts()
    {
        var b = Spec(("get", "/orders", "List"));
        var o = Spec(("get", "/orders", "List"), ("post", "/orders", "Create A"));
        var t = Spec(("get", "/orders", "List"), ("post", "/orders", "Create B"));

        var r = ThreeWayMerger.Merge(b, o, t);
        Assert.True(r.HasConflicts);
        var c = Assert.Single(r.Conflicts);
        Assert.Equal(ConflictKind.AddAdd, c.Kind);
        Assert.Equal(new OperationKey("/orders", "post"), c.Key);
    }

    [Fact]
    public void ModifyModify_Conflicts()
    {
        var b = Spec(("get", "/orders", "base"));
        var o = Spec(("get", "/orders", "ours"));
        var t = Spec(("get", "/orders", "theirs"));

        var r = ThreeWayMerger.Merge(b, o, t);
        var c = Assert.Single(r.Conflicts);
        Assert.Equal(ConflictKind.ModifyModify, c.Kind);
    }

    [Fact]
    public void OursModifies_TheirsUnchanged_TakesOurs()
    {
        var b = Spec(("get", "/orders", "base"));
        var o = Spec(("get", "/orders", "ours-mod"));
        var t = Spec(("get", "/orders", "base"));

        var r = ThreeWayMerger.Merge(b, o, t);
        Assert.False(r.HasConflicts);
        var op = r.Build().OperationMap()[new OperationKey("/orders", "get")];
        Assert.Contains("ours-mod", op.Canonical);
    }

    [Fact]
    public void TheirsModifies_OursUnchanged_TakesTheirs()
    {
        var b = Spec(("get", "/orders", "base"));
        var o = Spec(("get", "/orders", "base"));
        var t = Spec(("get", "/orders", "theirs-mod"));

        var r = ThreeWayMerger.Merge(b, o, t);
        Assert.False(r.HasConflicts);
        var op = r.Build().OperationMap()[new OperationKey("/orders", "get")];
        Assert.Contains("theirs-mod", op.Canonical);
    }

    [Fact]
    public void ModifyDelete_Conflicts()
    {
        var b = Spec(("get", "/orders", "base"));
        var o = Spec(("get", "/orders", "ours-mod")); // ours modifies
        var t = Spec();                                // theirs deletes

        var r = ThreeWayMerger.Merge(b, o, t);
        var c = Assert.Single(r.Conflicts);
        Assert.Equal(ConflictKind.ModifyDelete, c.Kind);
    }

    [Fact]
    public void DeleteModify_Conflicts()
    {
        var b = Spec(("get", "/orders", "base"));
        var o = Spec();                                  // ours deletes
        var t = Spec(("get", "/orders", "theirs-mod"));  // theirs modifies

        var r = ThreeWayMerger.Merge(b, o, t);
        var c = Assert.Single(r.Conflicts);
        Assert.Equal(ConflictKind.DeleteModify, c.Kind);
    }

    [Fact]
    public void BothDeleteSame_NoConflict_OperationAbsent()
    {
        var b = Spec(("get", "/orders", "base"), ("post", "/orders", "Create"));
        var o = Spec(("post", "/orders", "Create"));   // both delete GET
        var t = Spec(("post", "/orders", "Create"));

        var r = ThreeWayMerger.Merge(b, o, t);
        Assert.False(r.HasConflicts);
        Assert.DoesNotContain(new OperationKey("/orders", "get"), r.Build().OperationMap().Keys);
    }

    [Fact]
    public void ResolveConflict_TakeTheirs_BuildsWithTheirs()
    {
        var b = Spec(("get", "/orders", "base"));
        var o = Spec(("get", "/orders", "ours"));
        var t = Spec(("get", "/orders", "theirs"));

        var r = ThreeWayMerger.Merge(b, o, t);
        Assert.True(r.HasConflicts);
        r.Conflicts[0].Resolution = Resolution.TakeTheirs;
        Assert.False(r.HasConflicts);

        var op = r.Build().OperationMap()[new OperationKey("/orders", "get")];
        Assert.Contains("theirs", op.Canonical);
    }

    [Fact]
    public void Build_WithUnresolvedConflict_Throws()
    {
        var b = Spec(("get", "/o", "base"));
        var o = Spec(("get", "/o", "ours"));
        var t = Spec(("get", "/o", "theirs"));
        var r = ThreeWayMerger.Merge(b, o, t);
        Assert.Throws<InvalidOperationException>(() => r.Build());
    }
}
