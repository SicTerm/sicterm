using Sic.Core.Sessions;
using Xunit;

namespace Sic.Core.Tests;

public sealed class ConversationGroupsTests
{
    // UTC-5 without daylight saving, so calendar days are unambiguous whatever machine runs the tests.
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("test-east", TimeSpan.FromHours(-5), "test", "test");
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 0, 10, 0, TimeSpan.FromHours(-5));

    private static Conversation At(string id, DateTimeOffset updated, string agent = "claude") =>
        new(agent, id, "Title " + id, @"C:\work", updated, updated, @"C:\history\" + id + ".jsonl", @"C:\history", "");

    private static string[] Ids(ConversationSection section) => section.Conversations.Select(item => item.SessionId).ToArray();

    [Fact]
    public void CalendarDaysInTheTimeZoneDecideTheGroup()
    {
        var sections = ConversationGroups.Arrange(
        [
            At("midnight", Now.AddMinutes(-10)),
            At("late", Now.AddMinutes(-20)),
            At("dawn", new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.FromHours(-5))),
            At("older", new DateTimeOffset(2026, 9, 26, 23, 59, 0, TimeSpan.FromHours(-5))),
            At("utc", new DateTimeOffset(2026, 9, 28, 4, 0, 0, TimeSpan.Zero))
        ], new HashSet<string>(), Now, Zone);
        Assert.Equal([ConversationGroup.Today, ConversationGroup.Yesterday, ConversationGroup.Earlier], sections.Select(section => section.Group));
        // 00:00 today; 23:50 yesterday although only twenty minutes ago; 04:00 UTC is 23:00 yesterday here.
        Assert.Equal(["midnight"], Ids(sections[0]));
        Assert.Equal(["late", "utc", "dawn"], Ids(sections[1]));
        Assert.Equal(["older"], Ids(sections[2]));
        Assert.Equal(["Today", "Yesterday", "Earlier"], sections.Select(section => section.Label));
    }

    [Fact]
    public void APinnedConversationAppearsOnlyUnderPinnedAndPinnedComesFirst()
    {
        var old = At("old", Now.AddDays(-40));
        var recent = At("recent", Now.AddMinutes(-1));
        var pinnedToday = At("pinned-today", Now.AddMinutes(-2));
        var sections = ConversationGroups.Arrange([recent, old, pinnedToday], new HashSet<string> { old.Key, pinnedToday.Key }, Now, Zone);
        Assert.Equal([ConversationGroup.Pinned, ConversationGroup.Today], sections.Select(section => section.Group));
        Assert.Equal(["pinned-today", "old"], Ids(sections[0]));
        Assert.Equal(["recent"], Ids(sections[1]));
    }

    [Fact]
    public void EmptyGroupsAreLeftOutAndAnEmptyListHasNoSections()
    {
        Assert.Empty(ConversationGroups.Arrange([], new HashSet<string>(), Now, Zone));
        var only = ConversationGroups.Arrange([At("a", Now.AddDays(-3))], new HashSet<string>(), Now, Zone);
        Assert.Equal(ConversationGroup.Earlier, Assert.Single(only).Group);
    }

    [Fact]
    public void AFutureTimeCountsAsTodayAndADuplicateKeyIsListedOnce()
    {
        var ahead = At("ahead", Now.AddDays(2));
        var sections = ConversationGroups.Arrange([ahead, ahead with { Title = "again" }], new HashSet<string>(), Now, Zone);
        Assert.Equal(["ahead"], Ids(Assert.Single(sections)));
        Assert.Equal(ConversationGroup.Today, sections[0].Group);
    }

    [Fact]
    public void ByFolderRollsUpToTheNearestRootAndListsTheUnknownFolderLast()
    {
        var inRepo = At("repo", Now.AddMinutes(-30)) with { WorkingDirectory = @"C:\repos\sicterm" };
        var deep = At("deep", Now.AddMinutes(-1)) with { WorkingDirectory = @"c:/repos/sicterm/docs/welcome/" };
        var other = At("other", Now.AddMinutes(-5)) with { WorkingDirectory = @"C:\repos\web" };
        var nowhere = At("nowhere", Now.AddMinutes(-2)) with { WorkingDirectory = null };
        var pinnedOld = At("pinned", Now.AddDays(-9)) with { WorkingDirectory = @"C:\repos\web" };
        Conversation[] repoThenDeep = [inRepo, deep];
        var sections = ConversationGroups.Arrange([inRepo, deep, other, nowhere, pinnedOld], new HashSet<string> { pinnedOld.Key }, Now, Zone,
            ConversationListing.ByFolder, roots: [@"C:\repos\sicterm"]);
        // Pinned first; then the listed folder's workspace; web and the unknown folder, outside every listed folder, are Scratch, last.
        Assert.Equal(["pinned", @"folder:C:\repos\sicterm", "folder:"], sections.Select(section => section.Key));
        Assert.Equal(["Pinned", "sicterm", Sic.Core.State.Workspaces.ScratchName], sections.Select(section => section.Label));
        Assert.Equal(["deep", "repo"], Ids(sections[1]));
        Assert.Equal(["nowhere", "other"], Ids(sections[2]));
        Assert.Equal(@"C:\repos\sicterm", sections[1].Folder);
        Assert.Equal("", sections[2].Folder);
        // With no folder listed every folder is a section of its own, newest first, and the unknown folder is last under its own label.
        var unlisted = ConversationGroups.Arrange([inRepo, deep, other, nowhere], new HashSet<string>(), Now, Zone, ConversationListing.ByFolder);
        Assert.Equal(["welcome", "web", "sicterm", ConversationGroups.UnknownFolderLabel], unlisted.Select(section => section.Label));
        // The workspaces keep the list's order whatever the sort, and a ranked list keeps the order given within each.
        var ordered = ConversationGroups.Arrange([other, repoThenDeep[0], repoThenDeep[1]], new HashSet<string>(), Now, Zone, ConversationListing.ByFolder,
            ConversationListing.ByName, roots: [@"C:\repos\web", @"C:\repos\sicterm"], ranked: true);
        Assert.Equal(["web", "sicterm"], ordered.Select(section => section.Label));
        Assert.Equal(["repo", "deep"], Ids(ordered[1]));
        Assert.All(sections, section => Assert.Equal(section.Group, section.Group == ConversationGroup.Pinned ? ConversationGroup.Pinned : ConversationGroup.Folder));
        // The nearest root wins over a shorter one that also holds the folder, and a folder under no root is its own section.
        Assert.Equal(@"C:\repos\sicterm", ConversationGroups.RootOf(@"C:\repos\sicterm\docs", [@"C:\repos", @"C:\repos\sicterm"]));
        Assert.Equal(@"D:\elsewhere", ConversationGroups.RootOf(@"D:\elsewhere\", [@"C:\repos"]));
        Assert.Equal(@"docs\welcome", ConversationGroups.Under(@"C:\Repos\Sicterm\docs\welcome", @"C:\repos\sicterm"));
        Assert.Equal("", ConversationGroups.Under(@"C:\repos\sicterm", @"C:\repos\sicterm"));
        Assert.Equal("sicterm", ConversationGroups.Leaf(@"C:\repos\sicterm\"));
        Assert.Equal(@"C:\", ConversationGroups.Leaf(@"C:\"));
    }

    [Fact]
    public void ByAgentAndByNameOrderSectionsAndRowsByTheirNames()
    {
        var claudeB = At("b", Now.AddMinutes(-1)) with { Title = "Beta" };
        var claudeA = At("a", Now.AddMinutes(-9)) with { Title = "alpha" };
        var codex = At("c", Now.AddMinutes(-2), "codex") with { Title = "Gamma" };
        var sections = ConversationGroups.Arrange([claudeB, claudeA, codex], new HashSet<string>(), Now, Zone,
            ConversationListing.ByAgent, ConversationListing.ByName, agentName: id => id == "codex" ? "Codex" : "Claude Code");
        Assert.Equal(["Claude Code", "Codex"], sections.Select(section => section.Label));
        Assert.Equal(["agent:claude", "agent:codex"], sections.Select(section => section.Key));
        Assert.Equal(["a", "b"], Ids(sections[0]));
        // Newest first by agent puts Claude Code (a minute ago) before Codex, and the rows newest first.
        var newest = ConversationGroups.Arrange([claudeA, codex, claudeB], new HashSet<string>(), Now, Zone, ConversationListing.ByAgent);
        Assert.Equal(["agent:claude", "agent:codex"], newest.Select(section => section.Key));
        Assert.Equal(["b", "a"], Ids(newest[0]));
        // The words are normalized, anything else reading as the defaults.
        Assert.Equal(ConversationListing.ByFolder, ConversationListing.NormalizeGrouping(" FOLDER "));
        Assert.Equal(ConversationListing.ByFolder, ConversationListing.NormalizeGrouping(null));
        Assert.Equal(ConversationListing.ByDate, ConversationListing.NormalizeGrouping("date"));
        Assert.Equal(ConversationListing.Newest, ConversationListing.NormalizeSort("whatever"));
        Assert.Equal(ConversationListing.ByName, ConversationListing.NormalizeSort("Name"));
    }

    [Fact]
    public void KeysDistinguishAgentsWithTheSameSessionId()
    {
        var claude = At("same", Now.AddMinutes(-1));
        var codex = At("same", Now.AddMinutes(-2), "codex");
        var sections = ConversationGroups.Arrange([claude, codex], new HashSet<string> { codex.Key }, Now, Zone);
        Assert.Equal("codex", Assert.Single(sections[0].Conversations).AgentId);
        Assert.Equal("claude", Assert.Single(sections[1].Conversations).AgentId);
    }
}
