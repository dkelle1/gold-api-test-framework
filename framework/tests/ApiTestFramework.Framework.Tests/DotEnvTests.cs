using Allure.NUnit.Attributes;
using ApiTestFramework.Core.Base;
using ApiTestFramework.Core.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace ApiTestFramework.Framework.Tests;

[TestFixture]
[AllureSuite("Framework")]
[AllureFeature("Configuration (.env)")]
[Category("Unit")]
[NonParallelizable] // touches process environment variables
public class DotEnvTests : BaseTest
{
    private readonly List<string> _touched = new();
    private string _dir = null!;

    [SetUp]
    public void CreateTempDirectory()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dotenv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void Cleanup()
    {
        foreach (var key in _touched)
            Environment.SetEnvironmentVariable(key, null);
        _touched.Clear();
        Environment.SetEnvironmentVariable(DotEnv.FileVariable, null);
        Directory.Delete(_dir, recursive: true);
    }

    private string Key(string name)
    {
        var key = $"DOTENV_TEST_{name}_{Guid.NewGuid():N}";
        _touched.Add(key);
        return key;
    }

    private string WriteEnv(string content, string? directory = null)
    {
        var path = Path.Combine(directory ?? _dir, ".env");
        File.WriteAllText(path, content);
        return path;
    }

    [Test]
    public void Parse_SupportsCommentsExportQuotesAndInlineComments()
    {
        var pairs = DotEnv.Parse(
            """
            # comment
            PLAIN=value
            export EXPORTED=yes
            SPACED = padded value   # inline comment
            DOUBLE="line1\nline2 # not a comment \"quoted\""
            SINGLE='raw \n $value # kept'
            EMPTY=
            URL=http://localhost:5100/path?a=1#frag
            DUP=first
            DUP=second
            """).ToDictionary(p => p.Key, p => p.Value);

        pairs["PLAIN"].Should().Be("value");
        pairs["EXPORTED"].Should().Be("yes");
        pairs["SPACED"].Should().Be("padded value");
        pairs["DOUBLE"].Should().Be("line1\nline2 # not a comment \"quoted\"");
        pairs["SINGLE"].Should().Be(@"raw \n $value # kept");
        pairs["EMPTY"].Should().BeEmpty();
        pairs["URL"].Should().Be("http://localhost:5100/path?a=1#frag");
        pairs["DUP"].Should().Be("second");
    }

    [Test]
    public void Parse_RejectsMalformedLinesWithFileAndLine()
    {
        var act = () => DotEnv.Parse("OK=1\nNOT A PAIR\n", "my.env");

        act.Should().Throw<FormatException>().WithMessage("my.env:2: expected KEY=value");
    }

    [Test]
    public void Load_DoesNotOverrideExistingVariables_UnlessAsked()
    {
        var existing = Key("EXISTING");
        var fresh = Key("FRESH");
        Environment.SetEnvironmentVariable(existing, "from-environment");
        var path = WriteEnv($"{existing}=from-file\n{fresh}=from-file\n");

        DotEnv.Load(path).Should().Equal(fresh);
        Environment.GetEnvironmentVariable(existing).Should().Be("from-environment");
        Environment.GetEnvironmentVariable(fresh).Should().Be("from-file");

        DotEnv.Load(path, overrideExisting: true);
        Environment.GetEnvironmentVariable(existing).Should().Be("from-file");
    }

    [Test]
    public void Find_WalksUpToTheNearestEnvFile()
    {
        var expected = WriteEnv("A=1");
        var nested = Directory.CreateDirectory(Path.Combine(_dir, "bin", "Debug", "net8.0")).FullName;

        DotEnv.Find(nested).Should().Be(expected);
    }

    [Test]
    public void Find_PrefersTheExplicitTestEnvFile()
    {
        var other = Directory.CreateDirectory(Path.Combine(_dir, "elsewhere")).FullName;
        var explicitFile = WriteEnv("A=1", other);
        WriteEnv("A=2");
        Environment.SetEnvironmentVariable(DotEnv.FileVariable, explicitFile);

        DotEnv.Find(_dir).Should().Be(explicitFile);
    }

    [Test]
    public void Find_MissingExplicitFile_FailsLoudly()
    {
        Environment.SetEnvironmentVariable(DotEnv.FileVariable, Path.Combine(_dir, "missing.env"));

        var act = () => DotEnv.Find(_dir);

        act.Should().Throw<FileNotFoundException>().WithMessage("*TEST_ENV_FILE*");
    }

    [Test]
    public void EnvFile_DrivesTheWholeTestConfiguration()
    {
        // Same keys as environment variables: TEST_ prefix, "__" between sections, list indexes
        var marker = Guid.NewGuid().ToString("N")[..8];
        var prefix = $"TEST{marker}_";
        var path = WriteEnv($"""
            {prefix}Services__OrderService__BaseUrl=https://orders.staging.example.com
            {prefix}Identities__0__Name=admin
            {prefix}Identities__0__Role=Admin
            {prefix}Authentication__Mode=OAuth2ClientCredentials
            {prefix}Authentication__OAuth2__ClientSecret="s3cr3t # with hash"
            {prefix}ContractValidation__Mode=Warn
            {prefix}RetryCount=2
            """);
        _touched.AddRange(DotEnv.Load(path));

        var configuration = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
        var config = new TestConfiguration();
        configuration.Bind(config);

        config.Services["OrderService"].BaseUrl.Should().Be("https://orders.staging.example.com");
        config.Identities.Should().ContainSingle(i => i.Name == "admin" && i.Role == "Admin");
        config.Authentication.Mode.Should().Be("OAuth2ClientCredentials");
        config.Authentication.OAuth2.ClientSecret.Should().Be("s3cr3t # with hash");
        config.RetryCount.Should().Be(2);
        configuration["ContractValidation:Mode"].Should().Be("Warn");
    }
}
