using System.Text;
using BrowserWall.Core.Launch;
using BrowserWall.Core.Session;

namespace BrowserWall.Tests;

public class AddressBarTests
{
    private static AddressBarCandidate Edit(bool underToolBar = false, string? name = null, bool value = true, bool inDoc = false) =>
        new(true, value, inDoc, underToolBar, name);

    [Fact(DisplayName = "AddressBar_PickOnlyToolbarOrKnownName")]
    public void AddressBar_PickOnlyToolbarOrKnownName()
    {
        // Edit + ValuePattern under a toolbar: recognized.
        Assert.Equal(1, AddressBarPick.Choose(new[] { Edit(name: "x"), Edit(underToolBar: true) }));

        // Known name: recognized.
        Assert.Equal(0, AddressBarPick.Choose(new[] { Edit(name: "網址與搜尋列"), Edit(name: "Search") }));

        // A text box inside web content does not count, even with the same name or under a toolbar.
        Assert.Equal(-1, AddressBarPick.Choose(new[] { Edit(name: "網址與搜尋列", inDoc: true), Edit(underToolBar: true, inDoc: true) }));

        // No ValuePattern, not an Edit: does not count.
        Assert.Equal(-1, AddressBarPick.Choose(new[] { Edit(underToolBar: true, value: false) }));
        Assert.Equal(-1, AddressBarPick.Choose(new[] { new AddressBarCandidate(false, true, false, true, "網址與搜尋列") }));

        // Zero or two or more: not recognized; no guessing.
        Assert.Equal(-1, AddressBarPick.Choose(Array.Empty<AddressBarCandidate>()));
        Assert.Equal(-1, AddressBarPick.Choose(new[] { Edit(underToolBar: true), Edit(name: "網址與搜尋列") }));
    }

    [Fact(DisplayName = "AddressBar_EnglishNameRecognized")]
    public void AddressBar_EnglishNameRecognized()
    {
        // The match list holds exactly the Traditional Chinese and English Chrome names.
        Assert.Equal(new[] { "網址與搜尋列", "Address and search bar" }, AddressBarPick.KnownNames);

        // English name, not under a toolbar: recognized; the Chinese name still works.
        Assert.Equal(1, AddressBarPick.Choose(new[] { Edit(name: "Search"), Edit(name: "Address and search bar") }));
        Assert.Equal(0, AddressBarPick.Choose(new[] { Edit(name: "網址與搜尋列"), Edit(name: "Search") }));

        // Exact match only: different case or a partial name does not count.
        Assert.Equal(-1, AddressBarPick.Choose(new[] { Edit(name: "address and search bar") }));
        Assert.Equal(-1, AddressBarPick.Choose(new[] { Edit(name: "Address bar") }));

        // Inside web content, or without ValuePattern: not counted even with the English name.
        Assert.Equal(-1, AddressBarPick.Choose(new[] { Edit(name: "Address and search bar", inDoc: true) }));
        Assert.Equal(-1, AddressBarPick.Choose(new[] { Edit(name: "Address and search bar", value: false) }));

        // Exactly one: English-named edit plus another toolbar edit → not recognized.
        Assert.Equal(-1, AddressBarPick.Choose(new[] { Edit(name: "Address and search bar"), Edit(underToolBar: true) }));
    }

    [Fact(DisplayName = "AddressBar_FixedTwoSecondSchedule")]
    public void AddressBar_FixedTwoSecondSchedule()
    {
        TimeSpan start = TimeSpan.FromSeconds(0);
        TimeSpan interval = TimeSpan.FromSeconds(2);
        TimeSpan S(double s) => TimeSpan.FromSeconds(s);

        Assert.Equal(S(2), ReadSchedule.NextDue(start, S(0), interval));
        Assert.Equal(S(4), ReadSchedule.NextDue(start, S(2), interval));   // Started on time and just finished: next tick
        Assert.Equal(S(4), ReadSchedule.NextDue(start, S(2.5), interval)); // Took 0.5 seconds: wait 1.5 more seconds
        Assert.Equal(S(8), ReadSchedule.NextDue(start, S(7), interval));   // Ran from 2 to 7 seconds: next is 8 seconds, not 9
        Assert.Equal(S(6), ReadSchedule.NextDue(start, S(5), interval));
        Assert.Equal(S(10), ReadSchedule.NextDue(S(10), S(3), interval));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReadSchedule.NextDue(start, S(1), TimeSpan.Zero));
    }

    [Fact(DisplayName = "Preferences_SetsOnlyPreventUrlElisions")]
    public void Preferences_SetsOnlyPreventUrlElisions()
    {
        static string? Run(string? json) =>
            PreferencesPatch.Apply(json is null ? null : Encoding.UTF8.GetBytes(json)) is byte[] b ? Encoding.UTF8.GetString(b) : null;

        // File missing: only this setting.
        Assert.Equal("{\"omnibox\":{\"prevent_url_elisions\":true}}", Run(null));

        // Other keys, other keys inside omnibox, Chinese strings and big numbers are kept.
        string original = "{\"profile\":{\"name\":\"使用者 1\"},\"big\":12345678901234567890,\"omnibox\":{\"other\":1},\"z\":[1,2]}";
        Assert.Equal(
            "{\"profile\":{\"name\":\"使用者 1\"},\"big\":12345678901234567890,\"omnibox\":{\"other\":1,\"prevent_url_elisions\":true},\"z\":[1,2]}",
            Run(original));

        // No omnibox: added at the end.
        Assert.Equal("{\"a\":1,\"omnibox\":{\"prevent_url_elisions\":true}}", Run("{\"a\":1}"));

        // false becomes true; already true is not written.
        Assert.Equal("{\"omnibox\":{\"prevent_url_elisions\":true}}", Run("{\"omnibox\":{\"prevent_url_elisions\":false}}"));
        Assert.Null(Run("{\"omnibox\":{\"prevent_url_elisions\":true}}"));

        // Invalid JSON, top level not an object, omnibox not an object: left alone.
        Assert.Null(Run("{\"a\":"));
        Assert.Null(Run("[1,2]"));
        Assert.Null(Run("{\"omnibox\":5}"));
        Assert.Null(PreferencesPatch.Apply(new byte[] { 0x7B, 0xFF, 0xFE, 0x7D }));
        Assert.Null(Run(""));
    }
}
