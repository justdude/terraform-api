using TerraformApi.Domain.Models.Hcl;

namespace TerraformApi.Application.Tests.Hcl;

/// <summary>
/// Structural AST equality for round-trip tests: compares node types, keys and
/// values recursively while ignoring source positions, spans and dirty flags.
/// </summary>
internal static class AstAssert
{
    public static void Equal(HclDocument expected, HclDocument actual)
    {
        EqualItems(expected.RootItems, actual.RootItems, "$");
    }

    private static void EqualItems(List<HclObjectItem> expected, List<HclObjectItem> actual, string path)
    {
        Assert.True(expected.Count == actual.Count,
            $"{path}: item count mismatch — expected {expected.Count}, got {actual.Count}");

        for (var i = 0; i < expected.Count; i++)
            EqualItem(expected[i], actual[i], $"{path}[{i}]");
    }

    private static void EqualItem(HclObjectItem expected, HclObjectItem actual, string path)
    {
        switch (expected, actual)
        {
            case (HclComment e, HclComment a):
                Assert.True(e.Kind == a.Kind, $"{path}: comment kind mismatch");
                Assert.True(e.Text == a.Text, $"{path}: comment text mismatch — '{e.Text}' vs '{a.Text}'");
                break;

            case (HclAssignment e, HclAssignment a):
                Assert.True(e.Key == a.Key, $"{path}: key mismatch — '{e.Key}' vs '{a.Key}'");
                Assert.True(e.KeyIsQuoted == a.KeyIsQuoted, $"{path}.{e.Key}: KeyIsQuoted mismatch");
                EqualValue(e.Value, a.Value, $"{path}.{e.Key}");
                break;

            default:
                Assert.Fail($"{path}: item type mismatch — {expected.GetType().Name} vs {actual.GetType().Name}");
                break;
        }
    }

    private static void EqualValue(HclValue expected, HclValue actual, string path)
    {
        switch (expected, actual)
        {
            case (HclLiteral e, HclLiteral a):
                Assert.True(e.Kind == a.Kind, $"{path}: literal kind mismatch — {e.Kind} vs {a.Kind}");
                Assert.True(e.RawValue == a.RawValue, $"{path}: literal value mismatch — '{e.RawValue}' vs '{a.RawValue}'");
                break;

            case (HclInterpolation e, HclInterpolation a):
                Assert.True(e.InnerText == a.InnerText, $"{path}: interpolation mismatch — '{e.InnerText}' vs '{a.InnerText}'");
                Assert.True(e.Bare == a.Bare, $"{path}: Bare mismatch");
                break;

            case (HclHeredoc e, HclHeredoc a):
                Assert.True(e.Marker == a.Marker, $"{path}: heredoc marker mismatch");
                Assert.True(e.Content == a.Content, $"{path}: heredoc content mismatch");
                Assert.True(e.Indented == a.Indented, $"{path}: heredoc indented mismatch");
                break;

            case (HclObject e, HclObject a):
                EqualItems(e.Items, a.Items, path);
                break;

            case (HclArray e, HclArray a):
                Assert.True(e.Items.Count == a.Items.Count,
                    $"{path}: array length mismatch — {e.Items.Count} vs {a.Items.Count}");
                for (var i = 0; i < e.Items.Count; i++)
                {
                    var ePath = $"{path}[{i}]";
                    Assert.True(e.Items[i].LeadingComments.Count == a.Items[i].LeadingComments.Count,
                        $"{ePath}: leading comment count mismatch");
                    for (var c = 0; c < e.Items[i].LeadingComments.Count; c++)
                    {
                        Assert.True(e.Items[i].LeadingComments[c].Text == a.Items[i].LeadingComments[c].Text,
                            $"{ePath}: leading comment text mismatch");
                    }
                    EqualValue(e.Items[i].Value, a.Items[i].Value, ePath);
                }
                break;

            default:
                Assert.Fail($"{path}: value type mismatch — {expected.GetType().Name} vs {actual.GetType().Name}");
                break;
        }
    }
}
