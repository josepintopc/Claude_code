using SupernovaBench.Core.Info;
using SupernovaBench.Core.Localization;

namespace SupernovaBench.Core.Tests;

public class MemoryHeuristicsTests
{
    public MemoryHeuristicsTests() => Loc.SetLanguage("en");

    [Theory]
    [InlineData("DIMM_A2", "A")]
    [InlineData("DIMM_B2", "B")]
    [InlineData("ChannelA-DIMM0", "A")]
    [InlineData("CHANNEL B", "B")]
    [InlineData("Controller0-ChannelA-DIMM1", "A")]
    [InlineData("A1", "A")]
    [InlineData("DIMM 0", null)]
    [InlineData("", null)]
    public void Detects_channel_from_slot_names(string locator, string? expected)
        => Assert.Equal(expected, MemoryHeuristics.ChannelOf(locator));

    [Fact]
    public void Reads_speed_from_part_numbers()
    {
        Assert.Equal(6000, MemoryHeuristics.SpeedFromPartNumbers(new[] { "CMK32GX5M2B6000C30" }));
        Assert.Equal(6000, MemoryHeuristics.SpeedFromPartNumbers(new[] { "F5-6000J3038F16GX2" }));
        Assert.Equal(3200, MemoryHeuristics.SpeedFromPartNumbers(new[] { "KF432C16BB/8", "BLS8G4D32AESTK 3200" }));
        Assert.Equal(0, MemoryHeuristics.SpeedFromPartNumbers(new[] { "M378A1K43CB2-CTD" }));
    }

    [Fact]
    public void Xmp_verdict_flags_profile_off_and_on()
    {
        Assert.StartsWith("Probably OFF", MemoryHeuristics.XmpVerdict(4800, 4800, 6000));
        Assert.StartsWith("Probably ON", MemoryHeuristics.XmpVerdict(6000, 4800, 6000));
        Assert.StartsWith("Running below", MemoryHeuristics.XmpVerdict(2133, 3200, 0));
        Assert.StartsWith("Can't estimate", MemoryHeuristics.XmpVerdict(3200, 3200, 0));
        Assert.StartsWith("Could not read", MemoryHeuristics.XmpVerdict(0, 3200, 3200));
    }

    [Fact]
    public void Channel_verdict_covers_single_dual_same_and_unknown()
    {
        Assert.StartsWith("Single channel (1 module)", MemoryHeuristics.ChannelVerdict(new[] { ("DIMM_A2", "BANK 0") }));
        Assert.StartsWith("2 channels", MemoryHeuristics.ChannelVerdict(new[] { ("DIMM_A2", ""), ("DIMM_B2", "") }));
        Assert.StartsWith("Single channel: ", MemoryHeuristics.ChannelVerdict(new[] { ("DIMM_A1", ""), ("DIMM_A2", "") }));
        Assert.StartsWith("Couldn't tell", MemoryHeuristics.ChannelVerdict(new[] { ("DIMM 0", "BANK 0"), ("DIMM 1", "BANK 1") }));
    }
}
