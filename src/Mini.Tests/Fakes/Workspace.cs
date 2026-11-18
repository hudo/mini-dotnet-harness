namespace Mini.Tests.Fakes;

/// <summary>
/// The <c>ws</c> fixture: a fresh temp directory that is the process cwd until disposed, then deleted.
/// Sessions go to the run's temp MINI_NET_HOME (see <see cref="TestEnvironment"/>), keyed by this directory.
/// </summary>
// Python test helper: tests/test_mini.py ws, make
public sealed class Workspace : IDisposable
{
    readonly string _previousCwd = Directory.GetCurrentDirectory();

    /// <summary>This test's services: options, paths, model catalog, fake clock.</summary>
    public TestRuntime Runtime { get; }

    /// <summary>The working directory, as the OS reports it (symlinks such as macOS's /var resolved).</summary>
    public string Root { get; }

    /// <summary>Creates the directory and makes it the cwd.</summary>
    /// <param name="configure">Changes the options of <see cref="Runtime"/> (MINI_NO_TASK, HOME and the like).</param>
    public Workspace(Func<MiniOptions, MiniOptions>? configure = null)
    {
        Runtime = new TestRuntime(configure);
        var dir = Directory.CreateTempSubdirectory("mini-tests-ws-").FullName;
        Directory.SetCurrentDirectory(dir);
        Root = Directory.GetCurrentDirectory(); // getcwd() is the real path
    }

    /// <summary>A path inside the workspace.</summary>
    public string Path(string relative) => System.IO.Path.Combine(Root, relative);

    /// <summary>Writes a file inside the workspace (creating folders) and returns its full path.</summary>
    public string Write(string relative, string content)
    {
        var path = Path(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>A new session for this directory, as <c>SessionCatalog.Create</c> makes it.</summary>
    public ISessionStore NewSession(string? name = null) => Runtime.Sessions.Create(Root, name);

    /// <summary>The session file at <paramref name="path"/>, opened as a resume would.</summary>
    public ISessionStore OpenSession(string path) => Runtime.Sessions.Open(path);

    /// <summary>The latest session of this directory, or the latest whose id starts with <paramref name="prefix"/>.</summary>
    public ISessionStore? FindSession(string? prefix = null) => Runtime.Sessions.Find(Root, prefix);

    /// <summary>The sessions of this directory, newest first.</summary>
    public List<SessionSummary> ListSessions() => Runtime.Sessions.List(Root);

    /// <summary>The tools over this directory, over <see cref="Runtime"/> unless another runtime is given.</summary>
    public ToolKit Tools(ApprovalSettings approval, TestRuntime? runtime = null, IApprovalJudge? judge = null) =>
        new(Root, approval, runtime ?? Runtime, judge);

    /// <summary>The MCP servers of this directory's mcp.json.</summary>
    public Task<McpHub> StartMcpAsync() => McpHub.StartAsync(Root, Runtime.Paths);

    /// <summary>The skills of this directory.</summary>
    public List<Skill> FindSkills() => SkillCatalog.Find(Root, Runtime.Paths);

    /// <summary>
    /// An Agent over this directory with a scripted model, a new session unless one is given, and an auto-approving
    /// approval settings unless one is given, over <see cref="Runtime"/> unless another runtime is given. The output is silent (nothing on stdout; warnings still go to stderr: use
    /// <see cref="ConsoleCapture"/> to read them, or pass an output that isn't silent to see notes).
    /// </summary>
    public (Agent Agent, FakeModelClient Fake) Make(IEnumerable<object> script, ISessionStore? session = null, ApprovalSettings? approver = null,
        string? model = null, bool sub = false, string effort = "high", McpHub? hub = null, IReadOnlyList<Skill>? skills = null,
        IAgentOutput? ui = null, TestRuntime? runtime = null, IModelClient? client = null)
    {
        var fake = new FakeModelClient(script);
        var output = ui ?? new ConsoleAgentOutput(silent: true);
        var setup = new AgentSetup(Root, output, approver ?? new ApprovalSettings(auto: true))
        {
            Hub = hub, Skills = skills, Model = model, Effort = effort, Sub = sub, Client = client ?? fake,
            Interaction = new ConsoleUserInteraction(output),
        };
        return (Open(session ?? NewSession(), setup, runtime), fake);
    }

    /// <summary>An agent for the session: resumed when it has a meta line, new otherwise; on the real client unless the setup has one.</summary>
    public Agent Open(ISessionStore session, AgentSetup setup, TestRuntime? runtime = null)
    {
        var factory = (runtime ?? Runtime).Factory;
        return factory.CanResume(session) ? factory.Resume(session, setup) : factory.CreateNew(session, setup);
    }

    /// <summary>The setup of an agent that uses the real model client: silent output, ask-approval, in this directory.</summary>
    public AgentSetup RealSetup(string? model = null)
    {
        var output = new ConsoleAgentOutput(silent: true);
        return new AgentSetup(Root, output, new ApprovalSettings()) { Model = model, Interaction = new ConsoleUserInteraction(output) };
    }

    /// <summary>Restores the previous cwd and deletes the directory.</summary>
    public void Dispose()
    {
        Directory.SetCurrentDirectory(_previousCwd);
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { } // best effort: a test left a handle open
    }
}
