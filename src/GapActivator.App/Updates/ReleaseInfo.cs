namespace GapActivator.App.Updates;

/// <summary>The latest published release: its git tag and its human-facing page.</summary>
public sealed record ReleaseInfo(string TagName, Uri PageUrl);
