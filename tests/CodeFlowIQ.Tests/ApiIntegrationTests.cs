using System.Net;
using System.Net.Http.Json;
using CodeFlowIQ.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace CodeFlowIQ.Tests;

public sealed class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Root_ReturnsApiMetadata()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("CodeFlowIQ.Api", body, StringComparison.Ordinal);
        Assert.Contains("/api/overview", body, StringComparison.Ordinal);
        Assert.Contains("/api/runtime-flows", body, StringComparison.Ordinal);
        Assert.Contains("/api/chains", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_ReturnsRuntimeMetadata()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("healthy", body, StringComparison.Ordinal);
        Assert.Contains("processId", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalRuntimeHost_WritesRuntimeMetadataFile()
    {
        var runtimePath = Path.Combine(Path.GetTempPath(), "codeflowiq-runtime-tests", Guid.NewGuid().ToString("N"), "api.json");
        var metadata = LocalRuntimeHost.CreateMetadata("http://127.0.0.1:51234");

        LocalRuntimeHost.WriteRuntimeFile(runtimePath, metadata);

        Assert.True(File.Exists(runtimePath));
        await using var stream = File.OpenRead(runtimePath);
        var json = await JsonDocument.ParseAsync(stream);
        Assert.Equal("http://127.0.0.1:51234", json.RootElement.GetProperty("baseUrl").GetString());
        Assert.Equal(Environment.ProcessId, json.RootElement.GetProperty("processId").GetInt32());
        Assert.NotEmpty(json.RootElement.GetProperty("version").GetString()!);
    }

    [Fact]
    public void LocalRuntimeHost_UsesDynamicLocalhostByDefault()
    {
        var configuration = new ConfigurationBuilder().Build();

        var options = LocalRuntimeHost.CreateOptions(configuration);

        Assert.Equal("http://127.0.0.1:0", options.ListenUrl);
        Assert.EndsWith(Path.Combine("CodeFlowIQ", "runtime", "api.json"), options.RuntimeFilePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(options.WriteRuntimeFile);
    }

    [Fact]
    public void LocalRuntimeHost_ResolvesWindowsRuntimeDirectory()
    {
        var runtimeDirectory = LocalRuntimeHost.GetDefaultRuntimeDirectory(
            LocalRuntimePlatform.Windows,
            Path.Combine("C:", "Users", "ajite"));

        Assert.EndsWith(Path.Combine("CodeFlowIQ", "runtime"), runtimeDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine("AppData", "Local"), runtimeDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalRuntimeHost_ResolvesMacOsRuntimeDirectory()
    {
        var runtimeDirectory = LocalRuntimeHost.GetDefaultRuntimeDirectory(
            LocalRuntimePlatform.MacOS,
            Path.Combine("/Users", "ajite"));

        Assert.Equal(Path.Combine("/Users", "ajite", "Library", "Application Support", "CodeFlowIQ", "runtime"), runtimeDirectory);
    }

    [Fact]
    public void LocalRuntimeHost_ResolvesLinuxRuntimeDirectory()
    {
        var runtimeDirectory = LocalRuntimeHost.GetDefaultRuntimeDirectory(
            LocalRuntimePlatform.Linux,
            Path.Combine("/home", "ajite"));

        Assert.Equal(Path.Combine("/home", "ajite", ".local", "share", "CodeFlowIQ", "runtime"), runtimeDirectory);
    }

    [Fact]
    public void LocalRuntimeHost_UsesConfiguredRuntimeDirectoryAndCanDisableRuntimeFile()
    {
        var runtimeDirectory = Path.Combine(Path.GetTempPath(), "codeflowiq-configured-runtime");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodeFlowIQ:ApiUrls"] = "http://127.0.0.1:61234",
                ["CodeFlowIQ:RuntimeDirectory"] = runtimeDirectory,
                ["CodeFlowIQ:DisableRuntimeFile"] = "true"
            })
            .Build();

        var options = LocalRuntimeHost.CreateOptions(configuration);

        Assert.Equal("http://127.0.0.1:61234", options.ListenUrl);
        Assert.Equal(Path.Combine(runtimeDirectory, "api.json"), options.RuntimeFilePath);
        Assert.False(options.WriteRuntimeFile);
    }

    [Fact]
    public async Task Init_ReturnsBadRequestWhenPathIsMissing()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/workspace/init", new { path = "" });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Path is required", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitSummaryAndChains_WorkThroughHttp()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        await CreateChainSampleWorkspaceAsync(workspacePath);
        using var client = _factory.CreateClient();

        using var initResponse = await client.PostAsJsonAsync("/api/workspace/init", new { path = workspacePath });
        initResponse.EnsureSuccessStatusCode();
        var initJson = await JsonDocument.ParseAsync(await initResponse.Content.ReadAsStreamAsync());
        var initState = initJson.RootElement.GetProperty("state").GetString();
        Assert.True(initState is "queued" or "running", $"Expected queued or running, got {initState}.");
        var completedInit = await WaitForIndexingAsync(client, workspacePath);
        Assert.Equal("completed", completedInit.GetProperty("state").GetString());
        Assert.Equal(3, completedInit.GetProperty("summary").GetProperty("filesIndexed").GetInt32());

        using var cachedInitResponse = await client.PostAsJsonAsync("/api/workspace/init", new { path = workspacePath });
        cachedInitResponse.EnsureSuccessStatusCode();
        var cachedInitJson = await JsonDocument.ParseAsync(await cachedInitResponse.Content.ReadAsStreamAsync());
        Assert.Equal(0, cachedInitJson.RootElement.GetProperty("filesIndexed").GetInt32());
        Assert.True(cachedInitJson.RootElement.GetProperty("reusedExistingIndex").GetBoolean());

        var encodedPath = Uri.EscapeDataString(workspacePath);
        var summary = await client.GetFromJsonAsync<JsonElement>($"/api/summary?path={encodedPath}&take=5");
        Assert.Equal("PlainDirectory", summary.GetProperty("kind").GetString());
        Assert.True(summary.GetProperty("relationshipCount").GetInt32() > 0);

        var chains = await client.GetFromJsonAsync<string[]>($"/api/chains?path={encodedPath}&api=register&target=dbo.Users&format=tree&take=1&depth=8");
        Assert.NotNull(chains);
        Assert.Single(chains);
        Assert.Contains("matches_backend_handler -> symbol:AuthController.cs::Register", chains[0], StringComparison.Ordinal);
        Assert.Contains("executes_procedure -> procedure:dbo.RegisterUser", chains[0], StringComparison.Ordinal);
        Assert.Contains("writes_table -> database-table:dbo.Users", chains[0], StringComparison.Ordinal);
        Assert.Contains("\trelationship:", chains[0], StringComparison.Ordinal);

        var overview = await client.GetFromJsonAsync<JsonElement>($"/api/overview?path={encodedPath}&take=5");
        Assert.Contains("PlainDirectory", overview.GetProperty("kind").GetString(), StringComparison.Ordinal);
        Assert.NotEmpty(overview.GetProperty("suggestedStartingPoints").EnumerateArray());
        Assert.Contains("Register", overview.GetProperty("detectedFlows")[0].GetProperty("title").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("POST /api/register", overview.GetProperty("importantApis")[0].GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Contains("Users", overview.GetProperty("dataTouchpoints")[0].GetProperty("title").GetString(), StringComparison.Ordinal);

        var runtimeMap = await client.GetFromJsonAsync<JsonElement>($"/api/runtime-flows?path={encodedPath}&take=5");
        Assert.NotEmpty(runtimeMap.GetProperty("entryPoints").EnumerateArray());
        Assert.NotEmpty(runtimeMap.GetProperty("executionPaths").EnumerateArray());
        Assert.NotEmpty(runtimeMap.GetProperty("flows").EnumerateArray());
        Assert.Contains("Register", runtimeMap.GetProperty("flows")[0].GetProperty("title").GetString(), StringComparison.OrdinalIgnoreCase);

        var explorerRows = await client.GetFromJsonAsync<JsonElement>($"/api/explorer?path={encodedPath}&surface=apis&q=register&take=5");
        var selectedExplorerRow = explorerRows.EnumerateArray().Single(x => x.GetProperty("relationshipKind").GetString() == "handles_api");
        var selectedItemId = Uri.EscapeDataString(selectedExplorerRow.GetProperty("id").GetString()!);

        var relatedRows = await client.GetFromJsonAsync<JsonElement>($"/api/explorer/related?path={encodedPath}&surface=apis&itemId={selectedItemId}&take=6");
        var relatedGroups = relatedRows.EnumerateArray().ToList();
        Assert.Contains(relatedGroups, x => x.GetProperty("label").GetString() == "Outgoing from this evidence");
        Assert.Contains(relatedGroups, x => x.GetProperty("label").GetString() == "Incoming to this evidence");

        var flattenedRelatedRows = relatedGroups
            .SelectMany(x => x.GetProperty("rows").EnumerateArray())
            .ToList();
        Assert.Contains(flattenedRelatedRows, x => x.GetProperty("relationshipKind").GetString() == "matches_backend_handler");
        Assert.Contains(flattenedRelatedRows, x => x.GetProperty("relationshipKind").GetString() == "calls_method");
        Assert.Contains(flattenedRelatedRows, x => x.GetProperty("relationshipKind").GetString() == "contains_symbol");
    }

    private static async Task CreateChainSampleWorkspaceAsync(string workspacePath)
    {
        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "register.component.ts"),
            """
            export class RegisterComponent {
                register() {
                    this.http.post('/api/register', {});
                }
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "AuthController.cs"),
            """
            using Microsoft.AspNetCore.Mvc;

            [Route("api")]
            public sealed class AuthController : ControllerBase
            {
                [HttpPost("register")]
                public IActionResult Register()
                {
                    _userRepository.CreateAsync();
                    return Ok();
                }

                private readonly IUserRepository _userRepository;

                public AuthController(IUserRepository userRepository)
                {
                    _userRepository = userRepository;
                }
            }

            public sealed class UserRepository : IUserRepository
            {
                private readonly AppDbContext _dbContext;

                public UserRepository(AppDbContext dbContext)
                {
                    _dbContext = dbContext;
                }

                public void CreateAsync()
                {
                    _dbContext.Database.ExecuteSqlRaw("EXEC dbo.RegisterUser @Name");
                }
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "RegisterUser.sql"),
            """
            CREATE PROCEDURE dbo.RegisterUser
            AS
            BEGIN
                INSERT INTO dbo.Users (Name) VALUES ('Ada');
            END
            """);
    }

    private static async Task<JsonElement> WaitForIndexingAsync(HttpClient client, string workspacePath)
    {
        var encodedPath = Uri.EscapeDataString(workspacePath);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var response = await client.GetAsync($"/api/workspace/indexing-status?path={encodedPath}");
            response.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var root = json.RootElement.Clone();
            var state = root.GetProperty("state").GetString();
            if (state is "completed" or "failed" or "cancelled")
            {
                return root;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Indexing did not finish during the test window.");
    }
}
