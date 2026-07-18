using TerraformMerge.Engine;

namespace TerraformMerge.Tests;

public class TextDistanceTests
{
    [Theory]
    [InlineData("", "", 0)]
    [InlineData("abc", "", 3)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "abc", 0)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("flaw", "lawn", 2)]
    public void Levenshtein_KnownDistances(string a, string b, int expected)
    {
        Assert.Equal(expected, TextDistance.Levenshtein(a, b));
    }

    [Fact]
    public void NormalizedLevenshtein_IdenticalIsZero_DisjointIsOne()
    {
        Assert.Equal(0.0, TextDistance.NormalizedLevenshtein("same", "same"));
        Assert.Equal(1.0, TextDistance.NormalizedLevenshtein("aaaa", "bbbb"));
    }

    [Fact]
    public void NormalizedLevenshtein_BothEmpty_IsZero()
    {
        Assert.Equal(0.0, TextDistance.NormalizedLevenshtein("", ""));
    }

    [Fact]
    public void Jaccard_Basics()
    {
        Assert.Equal(1.0, TextDistance.Jaccard([], []));
        Assert.Equal(1.0, TextDistance.Jaccard(["a", "b"], ["b", "a"]));
        Assert.Equal(0.0, TextDistance.Jaccard(["a"], ["b"]));
        Assert.Equal(1.0 / 3.0, TextDistance.Jaccard(["a", "b"], ["b", "c"]), 5);
    }

    [Fact]
    public void Jaccard_IgnoresDuplicates()
    {
        Assert.Equal(1.0, TextDistance.Jaccard(["a", "a", "b"], ["a", "b", "b"]));
    }
}
