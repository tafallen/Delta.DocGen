namespace Delta.DocGen.Tests.Pipeline.Fixtures;

public sealed class TestWorkspace : IDisposable
{
    public string Workspace { get; }
    public string Root      { get; }
    public string Output    { get; }

    public TestWorkspace()
    {
        Workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Root      = Path.Combine(Workspace, "src");
        Output    = Path.Combine(Workspace, "dist", "step-library.json");
        Directory.CreateDirectory(Root);
        PipelineFixture.WriteFixture(Root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(Workspace)) Directory.Delete(Workspace, recursive: true); }
        catch (IOException) { }
    }
}
