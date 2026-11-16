using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TeamManager.Web;

public sealed record RepositoryCommit(string Id, string Author, DateTimeOffset Date, string Subject);
public sealed record RepositoryPatch(string Text, bool Truncated);

public sealed partial class RepositoryHistory(IConfiguration configuration)
{
    private const int HistorySize = 40;

    public bool IsConfigured(int repositoryId) => GetPath(repositoryId) is not null;

    public async Task<List<RepositoryCommit>> Recent(int repositoryId, CancellationToken cancellationToken)
    {
        var output = await Run(repositoryId, 128 * 1024, cancellationToken,
            "log", "--no-color", "--no-notes", "--no-show-signature", $"-{HistorySize}", "--format=%H%x00%aI%x00%an%x00%s%x00", "HEAD", "--");
        if (output.Truncated) throw new InvalidOperationException();

        var fields = output.Text.Split('\0');
        var commits = new List<RepositoryCommit>();
        for (var i = 0; i + 4 < fields.Length; i += 4)
        {
            var id = fields[i].Trim();
            if (!CommitId().IsMatch(id) ||
                !DateTimeOffset.TryParse(fields[i+1],CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))
                throw new InvalidOperationException();
            commits.Add(new(id,fields[i+2],date,fields[i+3]));
        }
        return commits;
    }

    public Task<RepositoryPatch> Diff(int repositoryId, RepositoryCommit commit, CancellationToken cancellationToken)
    {
        if (!CommitId().IsMatch(commit.Id)) throw new ArgumentException("Invalid commit ID.");
        return Run(repositoryId, 128 * 1024, cancellationToken,
            "show", "--format=", "--patch", "--no-color", "--no-ext-diff", "--no-textconv",
            "--diff-merges=first-parent", commit.Id, "--");
    }

    private string? GetPath(int repositoryId)
    {
        var configured = configuration[$"RepositoryHistory:Paths:{repositoryId}"];
        return configured is not null && Path.IsPathFullyQualified(configured) ? configured : null;
    }

    private async Task<RepositoryPatch> Run(int repositoryId, int maxCharacters,
        CancellationToken requestCancellation, params string[] args)
    {
        var path = GetPath(repositoryId);
        if (path is null || !Directory.Exists(path)) throw new InvalidOperationException();

        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("--no-pager");
        foreach (var argument in args) start.ArgumentList.Add(argument);
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["GIT_NO_LAZY_FETCH"] = "1";
        start.Environment["GIT_ALLOW_PROTOCOL"] = "";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment.Remove("GIT_DIR");
        start.Environment.Remove("GIT_WORK_TREE");

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new InvalidOperationException();
        }
        catch (Win32Exception ex) { throw new InvalidOperationException("Git could not start.", ex); }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
        try
        {
            var text = new StringBuilder();
            var buffer = new char[4096];
            while (true)
            {
                var read = await process.StandardOutput.ReadAsync(buffer.AsMemory(), timeout.Token);
                if (read == 0) break;
                var remaining = maxCharacters - text.Length;
                text.Append(buffer, 0, Math.Min(read, remaining));
                if (read > remaining)
                {
                    Stop(process);
                    await errors;
                    return new(text.ToString(), true);
                }
            }
            await process.WaitForExitAsync(timeout.Token);
            await errors;
            if (process.ExitCode != 0) throw new InvalidOperationException();
            return new(text.ToString(), false);
        }
        catch (OperationCanceledException) when (!requestCancellation.IsCancellationRequested)
        {
            throw new InvalidOperationException("Git operation timed out.");
        }
        finally
        {
            Stop(process);
            try { await errors; } catch (OperationCanceledException) { }
        }
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) when (process.HasExited) { }
    }

    [GeneratedRegex("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitId();
}
