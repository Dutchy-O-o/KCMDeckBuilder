using KCMDeckBuilder;
using Xunit;

public class DeckCodeTests
{
    [Fact]
    public void RoundTrip()
    {
        var names = new[] { "Stoat", "Bullfrog", "PeltHare", "eri_Axolotl" };
        Assert.Equal(names, DeckCode.Decode(DeckCode.Encode(names)));
    }

    [Fact]
    public void CodeHasNoPadding() =>
        Assert.DoesNotContain("=", DeckCode.Encode(new[] { "Wolf" }));

    [Theory]
    [InlineData("my deck: KCM:{0} have fun")]
    [InlineData("kcm:{0}")]
    [InlineData("KCM:{0}=")]
    [InlineData("KCM:{0}çok iyi")] // text written right after the code
    public void FindsCodeInText(string text)
    {
        string code = DeckCode.Encode(new[] { "Stoat", "Wolf" }).Substring(DeckCode.Prefix.Length);
        Assert.Equal(new[] { "Stoat", "Wolf" }, DeckCode.Decode(string.Format(text, code)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no code here")]
    [InlineData("KCM:")]
    [InlineData("KCM:!!!")]
    [InlineData("KCM:A")] // not valid base64
    public void NoCode(string text) => Assert.Null(DeckCode.Decode(text));

    [Fact]
    public void ParsesConfigList() =>
        Assert.Equal(new[] { "Stoat", "Bullfrog", "Wolf" }, DeckCode.ParseList(" Stoat,Bullfrog , ,Wolf,"));

    [Fact]
    public void ParsesNullAsEmpty() => Assert.Empty(DeckCode.ParseList(null));

    [Fact]
    public void JoinsLikeTheConfig() =>
        Assert.Equal("Stoat, Wolf", DeckCode.JoinList(new[] { "Stoat", "Wolf" }));

    [Fact]
    public void ItemsAndTotemRoundTrip()
    {
        var spec = new DeckSpec { Cards = { "Stoat", "Wolf" }, Items = { "Pliers", "FishHook" }, Totem = "Bird:Flying" };
        var back = DeckCode.DecodeSpec(DeckCode.Encode(spec));
        Assert.Equal(spec.Cards, back.Cards);
        Assert.Equal(spec.Items, back.Items);
        Assert.Equal("Bird:Flying", back.Totem);
    }

    [Fact]
    public void PlainDeckTextIsUnchanged() =>
        Assert.Equal("Stoat, Wolf", DeckCode.ToText(new DeckSpec { Cards = { "Stoat", "Wolf" } }));

    [Fact]
    public void OldSavedDeckHasNoExtras()
    {
        var spec = DeckCode.Parse("Stoat, Wolf");
        Assert.Equal(new[] { "Stoat", "Wolf" }, spec.Cards);
        Assert.Empty(spec.Items);
        Assert.Null(spec.Totem);
    }

    [Fact]
    public void TotemWithoutItems()
    {
        var spec = DeckCode.Parse(DeckCode.ToText(new DeckSpec { Cards = { "Stoat" }, Totem = "Canine:Sharp" }));
        Assert.Empty(spec.Items);
        Assert.Equal("Canine:Sharp", spec.Totem);
    }

    [Fact]
    public void OldCodesStillDecode() =>
        Assert.Equal(new[] { "Stoat", "Wolf" }, DeckCode.Decode("KCM:U3RvYXQsV29sZg"));
}
