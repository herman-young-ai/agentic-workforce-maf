using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace AgenticWorkforce.Architecture.Tests;

/// <summary>
/// Source-text scans that catch convention violations NetArchTest can't see
/// (it inspects compiled metadata, not method bodies). Each test walks the
/// repository's <c>src/</c> tree and asserts the rule against every <c>.cs</c>
/// file under it. Test files and the <c>obj/</c> / <c>bin/</c> trees are
/// excluded.
/// </summary>
public class SourceConventionTests
{
    [Fact]
    public void NoDateTimeNow()
    {
        AssertNoMatch(
            // Match DateTime.Now and DateTime.Today but not DateTime.UtcNow,
            // DateTimeOffset.Now (which is a separate rule below), or comments
            // that reference the API in prose.
            new Regex(@"(?<!\w)DateTime\.(Now|Today)\b", RegexOptions.Compiled),
            "Use DateTime.UtcNow — DateTime.Now/Today depend on the host's local clock.",
            allowComments: true);
    }

    [Fact]
    public void NoDateTimeOffset()
    {
        AssertNoMatch(
            // Match the type identifier as a standalone token. Skip the using
            // alias line so files importing System aren't a false positive
            // (System.DateTimeOffset is fine to reference in usings).
            new Regex(@"(?<!\w)DateTimeOffset\b", RegexOptions.Compiled),
            "AGENTS.md: DateTime UTC only — never DateTimeOffset.",
            allowComments: true);
    }

    [Fact]
    public void EveryAsyncMethodTakesCancellationToken()
    {
        // Catches `Task<...> Foo() {` / `Task<...> FooAsync() {` declarations
        // that don't include a CancellationToken in the parameter list. We
        // look for `Task` or `Task<...>` or `ValueTask` returns followed by an
        // identifier, opening paren, params, closing paren, then a body or
        // expression-body arrow.
        var asyncSigPattern = new Regex(
            @"(?<!\w)(?:Task|ValueTask)(?:<[^>(]+>)?\s+(\w+)\s*\(([^)]*)\)\s*(=>|\{|;)",
            RegexOptions.Compiled);

        var failures = new List<string>();
        foreach (var file in EnumerateProductionFiles())
        {
            var text = StripCommentsAndStrings(File.ReadAllText(file));
            foreach (Match match in asyncSigPattern.Matches(text))
            {
                var name = match.Groups[1].Value;
                var paramList = match.Groups[2].Value;
                if (paramList.Contains("CancellationToken", StringComparison.Ordinal)) continue;
                // Skip well-known framework hooks that the runtime invokes without a token.
                if (IsFrameworkInvokedMethod(name, paramList, file)) continue;
                failures.Add($"{Path.GetFileName(file)}:{name}({paramList.Trim()})");
            }
        }

        failures.Should().BeEmpty(
            "Every async method must take CancellationToken (AGENTS.md). " +
            "Offenders: " + string.Join(", ", failures));
    }

    private static bool IsFrameworkInvokedMethod(string name, string paramList, string filePath)
    {
        // BackgroundService.ExecuteAsync, IHostedService.StartAsync/StopAsync,
        // IDisposable.DisposeAsync, and EF Core SaveChanges* are invoked by
        // the runtime with the token it owns — we can't widen their signature.
        if (name is "ExecuteAsync" or "StartAsync" or "StopAsync"
                  or "DisposeAsync" or "InitializeAsync")
            return true;
        // GlobalExceptionHandler.TryHandleAsync, ASP.NET middleware InvokeAsync,
        // authentication handler HandleAuthenticateAsync are runtime callbacks.
        if (name is "TryHandleAsync" or "InvokeAsync" or "HandleAuthenticateAsync"
                  or "OnConnectedAsync" or "OnDisconnectedAsync")
            return true;
        // SignalR hub methods (client-callable RPCs) and hub clients (typed
        // client interface invoked by the framework) live under Hubs/ and do
        // not take CancellationToken in SignalR's wire model. Helpers called
        // only from hub methods inherit that limitation.
        if (filePath.Contains(Path.DirectorySeparatorChar + "Hubs" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return true;
        // Methods overriding IChatClient's contract are framework callbacks
        // (FunctionInvokingChatClient supplies its own cancellation token).
        if (name is "GetResponseAsync" or "GetStreamingResponseAsync")
            return paramList.Contains("cancellationToken", StringComparison.Ordinal);
        return false;
    }

    private static void AssertNoMatch(Regex pattern, string rule, bool allowComments)
    {
        var failures = new List<string>();
        foreach (var file in EnumerateProductionFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (allowComments && IsCommentLine(line)) continue;
                if (pattern.IsMatch(StripInlineComment(line)))
                    failures.Add($"{Path.GetFileName(file)}:{i + 1}");
            }
        }
        failures.Should().BeEmpty($"{rule}\nOffenders: {string.Join(", ", failures)}");
    }

    private static bool IsCommentLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal)
            || (trimmed.Length > 0 && trimmed[0] == '*')
            || trimmed.StartsWith("/*", StringComparison.Ordinal);
    }

    private static string StripInlineComment(string line)
    {
        var idx = line.IndexOf("//", StringComparison.Ordinal);
        return idx < 0 ? line : line[..idx];
    }

    private static string StripCommentsAndStrings(string source)
    {
        // Coarse but adequate for signature scanning: drop // line comments,
        // /* */ block comments, and verbatim/interpolated string contents so
        // an `Async` mention inside a docstring or a logged message doesn't
        // register as a method declaration.
        source = Regex.Replace(source, @"//[^\n]*", string.Empty);
        source = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        source = Regex.Replace(source, "\"[^\"\\n]*\"", "\"\"");
        return source;
    }

    private static IEnumerable<string> EnumerateProductionFiles()
    {
        var root = FindRepoRoot();
        var srcDir = Path.Combine(root, "src");
        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            if (file.EndsWith(".Designer.cs", StringComparison.Ordinal)) continue;       // EF Core generated migrations
            if (file.EndsWith(".g.cs", StringComparison.Ordinal)) continue;              // generated code
            if (file.EndsWith("AppDbContextModelSnapshot.cs", StringComparison.Ordinal)) continue;
            yield return file;
        }
    }

    private static string FindRepoRoot()
    {
        // Tests run from .../tests/AgenticWorkforce.Architecture.Tests/bin/Debug/net10.0
        // — walk up until we find the .slnx file.
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && dir.GetFiles("AgenticWorkforce.slnx").Length == 0)
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Could not locate repository root from test assembly location.");
        return dir.FullName;
    }
}
