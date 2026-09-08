using Horizon.Core.Models;
using Horizon.Tweaks;
using Xunit;

namespace Horizon.Tests;

public sealed class TweakCatalogTests
{
    [Fact]
    public void Catalogue_ids_are_unique()
    {
        var tweaks = new HorizonTweakCatalog().GetAll();
        Assert.Equal(tweaks.Count, tweaks.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Every_tweak_has_actionable_copy()
    {
        var tweaks = new HorizonTweakCatalog().GetAll();
        Assert.All(tweaks, tweak =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tweak.Description));
            Assert.False(string.IsNullOrWhiteSpace(tweak.WhatItDoes));
        });
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1048576, "1 MB")]
    [InlineData(2899102924, "2.7 GB")]
    public void Size_formatter_is_human_readable(long bytes, string expected) => Assert.Equal(expected, SizeFormatter.Format(bytes));
}
