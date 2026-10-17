using System.Text;
using System.Text.RegularExpressions;

namespace ApiTestFramework.Core.Configuration;

/// <summary>
/// Loads a <c>.env</c> file into the process environment, so every setting can live in one
/// git-ignored file. Keys use the same names as environment variables
/// (<c>TEST_Services__OrderService__BaseUrl=…</c>); variables that are already set in the
/// environment win, so CI secrets and per-run overrides keep working.
/// <para>
/// File lookup: the path in <c>TEST_ENV_FILE</c>, otherwise the nearest <c>.env</c> found by
/// walking up from the test assembly directory (so a <c>.env</c> at the repository root is found).
/// </para>
/// <para>
/// Syntax: <c>KEY=value</c> per line, <c>#</c> comments, optional <c>export</c> prefix,
/// <c>"double quoted"</c> values with <c>\n \" \\</c> escapes, <c>'single quoted'</c> literal values,
/// trailing <c> # comment</c> on unquoted values.
/// </para>
/// </summary>
public static class DotEnv
{
    /// <summary>Environment variable holding an explicit path to the .env file.</summary>
    public const string FileVariable = "TEST_ENV_FILE";

    private static readonly Regex KeyPattern = new(@"^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.Compiled);
    private static readonly object Lock = new();
    private static bool _loaded;

    /// <summary>The file loaded by <see cref="LoadDefault"/>, or null when none was found.</summary>
    public static string? LoadedFrom { get; private set; }

    /// <summary>Keys applied from the file (values are never exposed — they may be secrets).</summary>
    public static IReadOnlyList<string> LoadedKeys { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// Finds and loads the default .env once per process (called by <see cref="ConfigurationProvider"/>).
    /// </summary>
    public static void LoadDefault()
    {
        lock (Lock)
        {
            if (_loaded)
                return;
            _loaded = true;

            var path = Find();
            if (path is null)
                return;

            LoadedKeys = Load(path);
            LoadedFrom = path;
        }
    }

    /// <summary>The path from <c>TEST_ENV_FILE</c>, else the nearest .env above <paramref name="startDirectory"/>.</summary>
    public static string? Find(string? startDirectory = null)
    {
        var explicitPath = Environment.GetEnvironmentVariable(FileVariable);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var full = Path.GetFullPath(explicitPath);
            return File.Exists(full)
                ? full
                : throw new FileNotFoundException($"{FileVariable} points to a missing file", full);
        }

        for (var dir = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// Applies the file's variables to the process environment and returns the keys that were set.
    /// Existing environment variables are kept unless <paramref name="overrideExisting"/> is true.
    /// </summary>
    public static IReadOnlyList<string> Load(string path, bool overrideExisting = false)
    {
        var applied = new List<string>();
        foreach (var (key, value) in Parse(File.ReadAllText(path), path))
        {
            if (!overrideExisting && Environment.GetEnvironmentVariable(key) is not null)
                continue;
            Environment.SetEnvironmentVariable(key, value);
            applied.Add(key);
        }
        return applied;
    }

    /// <summary>Parses .env content into key/value pairs (later duplicates win).</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Parse(string content, string source = ".env")
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var lineNumber = 0;
        foreach (var rawLine in content.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (line.StartsWith("export ", StringComparison.Ordinal))
                line = line["export ".Length..].TrimStart();

            var separator = line.IndexOf('=');
            if (separator <= 0)
                throw new FormatException($"{source}:{lineNumber}: expected KEY=value");

            var key = line[..separator].Trim();
            if (!KeyPattern.IsMatch(key))
                throw new FormatException($"{source}:{lineNumber}: invalid key '{key}'");

            result[key] = ParseValue(line[(separator + 1)..].Trim(), source, lineNumber);
        }
        return result.ToList();
    }

    private static string ParseValue(string value, string source, int lineNumber)
    {
        if (value.Length >= 2 && value[0] == '\'' )
        {
            var end = value.IndexOf('\'', 1);
            if (end < 0) throw new FormatException($"{source}:{lineNumber}: unterminated single quote");
            return value[1..end];
        }

        if (value.Length >= 1 && value[0] == '"')
        {
            var sb = new StringBuilder();
            for (var i = 1; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '"')
                    return sb.ToString();
                if (c == '\\' && i + 1 < value.Length)
                {
                    var next = value[++i];
                    sb.Append(next switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => next });
                    continue;
                }
                sb.Append(c);
            }
            throw new FormatException($"{source}:{lineNumber}: unterminated double quote");
        }

        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        return (comment >= 0 ? value[..comment] : value).Trim();
    }
}
