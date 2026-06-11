using CodeFlowIQ.Analyzers;
using CodeFlowIQ.Core.Git;
using CodeFlowIQ.Core.Indexing;
using CodeFlowIQ.Data;
using CodeFlowIQ.Indexing;
using Microsoft.EntityFrameworkCore;

namespace CodeFlowIQ.Tests;

public sealed class WorkspaceIndexingServiceTests
{
    [Fact]
    public async Task InitializeAsync_IndexesPlainDirectoryFilesAndSymbols()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "RegisterService.cs"),
            """
            namespace Sample;

            public sealed class RegisterService
            {
                public void RegisterUser() { }
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "schema.sql"),
            "CREATE TABLE dbo.Users (Id int NOT NULL);");

        var service = new WorkspaceIndexingService(
            new PlainDirectoryGitDetector(),
            new LanguageDetector(),
            [new CSharpLanguageAnalyzer(), new SqlLanguageAnalyzer(), new JavaScriptTypeScriptLanguageAnalyzer()],
            new IndexingOptions());

        var summary = await service.InitializeAsync(workspacePath, CancellationToken.None);
        var query = new WorkspaceQueryService();
        var status = await query.GetStatusAsync(workspacePath, CancellationToken.None);
        var symbols = await query.SearchSymbolsAsync(workspacePath, "Register", 10, CancellationToken.None);

        Assert.Equal(2, summary.FilesScanned);
        Assert.Equal(2, status!.FileCount);
        Assert.Contains(symbols, x => x.Contains("RegisterService", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InitializeAsync_RespectsCodeFlowIqIgnore()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspacePath, "ignored"));

        await File.WriteAllTextAsync(Path.Combine(workspacePath, ".codeflowiqignore"), "ignored/\n*.skip.cs");
        await File.WriteAllTextAsync(Path.Combine(workspacePath, "KeepService.cs"), "public sealed class KeepService { }");
        await File.WriteAllTextAsync(Path.Combine(workspacePath, "Drop.skip.cs"), "public sealed class DropService { }");
        await File.WriteAllTextAsync(Path.Combine(workspacePath, "ignored", "HiddenService.cs"), "public sealed class HiddenService { }");

        var service = CreateService();

        await service.InitializeAsync(workspacePath, CancellationToken.None);
        var query = new WorkspaceQueryService();
        var files = await query.ListFilesAsync(workspacePath, null, null, 20, CancellationToken.None);

        Assert.Contains(files, x => x.Contains("KeepService.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(files, x => x.Contains("Drop.skip.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(files, x => x.Contains("HiddenService.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SyncAsync_ReindexesUnchangedFileWhenAnalyzerFingerprintIsStale()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "CarryForward.cs"),
            """
            public sealed class CarryForwardTrialBalanceLevviaManager : CarryForwardLevviaBase
            {
                public void Run()
                {
                    FinancialCarryForwardInternalAsync();
                }
            }

            public abstract class CarryForwardLevviaBase
            {
                protected void FinancialCarryForwardInternalAsync() { }
            }
            """);

        var service = CreateService();
        await service.InitializeAsync(workspacePath, CancellationToken.None);

        await using (var db = await WorkspaceDatabase.OpenMigratedAsync(workspacePath, CancellationToken.None))
        {
            var indexedFile = await db.IndexedFiles.SingleAsync(x => x.RelativePath == "CarryForward.cs");
            indexedFile.ContentHash = indexedFile.ContentHash.Replace("csharp-analysis-v2:", string.Empty, StringComparison.Ordinal);
            await db.SaveChangesAsync();
        }

        var summary = await service.SyncAsync(workspacePath, CancellationToken.None);

        await using var refreshedDb = await WorkspaceDatabase.OpenMigratedAsync(workspacePath, CancellationToken.None);
        var refreshedFile = await refreshedDb.IndexedFiles.SingleAsync(x => x.RelativePath == "CarryForward.cs");
        var relationships = await refreshedDb.CodeRelationships
            .Where(x => x.WorkspaceId == refreshedFile.WorkspaceId)
            .Select(x => $"{x.RelationshipKind}:{x.SourceIdentifier}->{x.TargetIdentifier}")
            .ToListAsync();

        Assert.Equal(1, summary.FilesIndexed);
        Assert.StartsWith("csharp-analysis-v2:", refreshedFile.ContentHash, StringComparison.Ordinal);
        Assert.Contains(relationships, x => x.Contains("inherits_from", StringComparison.Ordinal));
        Assert.Contains(relationships, x => x.Contains("calls_method", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InitializeAsync_CreatesRelationshipsForDiscoveredSymbols()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "RegisterService.cs"),
            """
            public sealed class RegisterService
            {
                public void RegisterUser() { }
            }
            """);

        var service = CreateService();

        await service.InitializeAsync(workspacePath, CancellationToken.None);

        await using var db = await WorkspaceDatabase.OpenMigratedAsync(workspacePath, CancellationToken.None);
        var relationships = await db.CodeRelationships
            .OrderBy(x => x.RelationshipKind)
            .Select(x => $"{x.SourceKind}:{x.SourceIdentifier}->{x.RelationshipKind}->{x.TargetKind}:{x.TargetIdentifier}")
            .ToListAsync();

        Assert.Contains(relationships, x => x.Contains("file:RegisterService.cs->contains_symbol->class:RegisterService.cs::RegisterService", StringComparison.Ordinal));
        Assert.Contains(relationships, x => x.Contains("symbol:RegisterService.cs::RegisterService->contains_symbol->method:RegisterService.cs::RegisterUser", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InitializeAsync_CreatesFrontendFlowRelationships()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "register.component.html"),
            """<button type="button" (click)="register()">Register</button>""");

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "register.component.ts"),
            """
            export class RegisterComponent {
                register() {
                    this.http.post('/api/register', {});
                }
            }
            """);

        var service = CreateService();

        await service.InitializeAsync(workspacePath, CancellationToken.None);

        await using var db = await WorkspaceDatabase.OpenMigratedAsync(workspacePath, CancellationToken.None);
        var relationships = await db.CodeRelationships
            .Select(x => $"{x.SourceKind}:{x.SourceIdentifier}->{x.RelationshipKind}->{x.TargetKind}:{x.TargetIdentifier}")
            .ToListAsync();

        Assert.Contains(relationships, x => x.Contains("ui-event:register.component.html::click:register->invokes_handler->method:register.component.html::register", StringComparison.Ordinal));
        Assert.Contains(relationships, x => x.Contains("ui-event:register.component.html::click:register->invokes_handler->method:register.component.ts::register", StringComparison.Ordinal));
        Assert.Contains(relationships, x => x.Contains("symbol:register.component.ts::register->calls_api->api:POST /api/register", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InitializeAsync_CreatesAspNetApiRepositoryModelAndAzureRelationships()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "AuthController.cs"),
            """
            using Azure.Messaging.ServiceBus;
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            [Route("api/[controller]")]
            public sealed class AuthController : ControllerBase
            {
                [HttpPost("register")]
                public IActionResult Register(RegisterRequest request) => Ok();
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "AppDbContext.cs"),
            """
            using Microsoft.EntityFrameworkCore;

            public sealed class AppDbContext : DbContext
            {
                public DbSet<User> Users { get; set; }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(workspacePath, "UserRepository.cs"), "public sealed class UserRepository { }");
        await File.WriteAllTextAsync(Path.Combine(workspacePath, "User.cs"), "public sealed class User { public int Id { get; set; } }");

        var service = CreateService();

        await service.InitializeAsync(workspacePath, CancellationToken.None);

        await using var db = await WorkspaceDatabase.OpenMigratedAsync(workspacePath, CancellationToken.None);
        var symbols = await db.CodeSymbols
            .Select(x => $"{x.Kind}:{x.Name}")
            .ToListAsync();
        var relationships = await db.CodeRelationships
            .Select(x => $"{x.SourceKind}:{x.SourceIdentifier}->{x.RelationshipKind}->{x.TargetKind}:{x.TargetIdentifier}")
            .ToListAsync();

        Assert.Contains(symbols, x => x == "controller:AuthController");
        Assert.Contains(symbols, x => x == "db-context:AppDbContext");
        Assert.Contains(symbols, x => x == "repository:UserRepository");
        Assert.Contains(relationships, x => x.Contains("symbol:AuthController.cs::Register->handles_api->api:POST /api/Auth/register", StringComparison.Ordinal));
        Assert.Contains(relationships, x => x.Contains("api:POST /api/Auth/register->handled_by->method:AuthController.cs::Register", StringComparison.Ordinal));
        Assert.Contains(relationships, x => x.Contains("symbol:AppDbContext.cs::AppDbContext->maps_dbset->domain-model:AppDbContext.cs::User", StringComparison.Ordinal));
        Assert.Contains(relationships, x => x.Contains("domain-model:AppDbContext.cs::User->maps_to_table->database-table:AppDbContext.cs::Users", StringComparison.Ordinal));
        Assert.Contains(relationships, x => x.Contains("uses_azure_service->azure-service:Azure Service Bus", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InitializeAsync_CreatesBackendInternalFlowRelationships()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "AuthController.cs"),
            """
            using Microsoft.AspNetCore.Mvc;

            [Route("api/auth")]
            public sealed class AuthController : ControllerBase
            {
                private readonly IUserRegistrationService _registrationService;

                public AuthController(IUserRegistrationService registrationService)
                {
                    _registrationService = registrationService;
                }

                [HttpPost("register")]
                public IActionResult Register()
                {
                    _registrationService.RegisterAsync();
                    return Ok();
                }
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "UserRegistrationService.cs"),
            """
            public sealed class UserRegistrationService : IUserRegistrationService
            {
                private readonly IUserRepository _userRepository;

                public UserRegistrationService(IUserRepository userRepository)
                {
                    _userRepository = userRepository;
                }

                public void RegisterAsync()
                {
                    _userRepository.CreateAsync();
                }
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "UserRepository.cs"),
            """
            public sealed class UserRepository : IUserRepository
            {
                private readonly AppDbContext _dbContext;

                public UserRepository(AppDbContext dbContext)
                {
                    _dbContext = dbContext;
                }

                public void CreateAsync()
                {
                    _dbContext.Users.Add(new User());
                    _dbContext.SaveChanges();
                }
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "AppDbContext.cs"),
            """
            using Microsoft.EntityFrameworkCore;

            public sealed class AppDbContext : DbContext
            {
                public DbSet<User> Users { get; set; }
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "Program.cs"),
            """
            services.AddScoped<IUserRegistrationService, UserRegistrationService>();
            services.AddScoped<IUserRepository, UserRepository>();
            """);

        var service = CreateService();

        await service.InitializeAsync(workspacePath, CancellationToken.None);

        await using var db = await WorkspaceDatabase.OpenMigratedAsync(workspacePath, CancellationToken.None);
        var relationships = await db.CodeRelationships
            .Select(x => $"{x.SourceKind}:{x.SourceIdentifier}->{x.RelationshipKind}->{x.TargetKind}:{x.TargetIdentifier}")
            .ToListAsync();

        Assert.Contains(relationships, x => x.Contains("symbol:AuthController.cs::AuthController->depends_on->service:IUserRegistrationService", StringComparison.Ordinal));
        Assert.Contains(relationships, x => x.Contains("service:IUserRegistrationService->implemented_by->class:UserRegistrationService", StringComparison.Ordinal));
        Assert.Contains(relationships, x => x.Contains("symbol:AuthController.cs::Register->calls_method->method:IUserRegistrationService.RegisterAsync", StringComparison.Ordinal));
        Assert.Contains(relationships, x => x.Contains("symbol:UserRegistrationService.cs::RegisterAsync->calls_method->method:IUserRepository.CreateAsync", StringComparison.Ordinal));
        Assert.Contains(relationships, x => x.Contains("symbol:UserRepository.cs::CreateAsync->writes_table->database-table:UserRepository.cs::Users", StringComparison.Ordinal));
        Assert.Contains(relationships, x => x.Contains("symbol:UserRepository.cs::CreateAsync->saves_changes->db-context:AppDbContext", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InitializeAsync_DoesNotTreatHttpHeadersAsDatabaseWrites()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "CortexSystem.cs"),
            """
            using System.Net.Http;

            public sealed class CortexSystem
            {
                private readonly HttpClient _httpClient;

                public CortexSystem(HttpClient httpClient)
                {
                    _httpClient = httpClient;
                }

                public void Send()
                {
                    _httpClient.DefaultRequestHeaders.Add("x-request-id", "123");
                }
            }
            """);

        var service = CreateService();

        await service.InitializeAsync(workspacePath, CancellationToken.None);

        await using var db = await WorkspaceDatabase.OpenMigratedAsync(workspacePath, CancellationToken.None);
        var relationships = await db.CodeRelationships
            .Select(x => $"{x.SourceKind}:{x.SourceIdentifier}->{x.RelationshipKind}->{x.TargetKind}:{x.TargetIdentifier}")
            .ToListAsync();

        Assert.DoesNotContain(relationships, x => x.Contains("writes_table", StringComparison.Ordinal));
        Assert.DoesNotContain(relationships, x => x.Contains("DefaultRequestHeaders", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InitializeAsync_ConnectsCSharpRepositoryToStoredProcedureAndTables()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "UserRepository.cs"),
            """
            public sealed class UserRepository
            {
                private readonly AppDbContext _dbContext;

                public UserRepository(AppDbContext dbContext)
                {
                    _dbContext = dbContext;
                }

                public void Create()
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
                SELECT Id, Name FROM dbo.Users;
            END
            """);

        var service = CreateService();

        await service.InitializeAsync(workspacePath, CancellationToken.None);

        await using var db = await WorkspaceDatabase.OpenMigratedAsync(workspacePath, CancellationToken.None);
        var relationships = await db.CodeRelationships
            .Select(x => $"{x.SourceKind}:{x.SourceIdentifier}->{x.RelationshipKind}->{x.TargetKind}:{x.TargetIdentifier}")
            .ToListAsync();

        Assert.Contains(relationships, x => x.Contains("symbol:UserRepository.cs::Create->executes_procedure->procedure:dbo.RegisterUser", StringComparison.Ordinal));
        Assert.Contains(relationships, x => x.Contains("procedure:dbo.RegisterUser->writes_table->database-table:dbo.Users", StringComparison.Ordinal));
        Assert.Contains(relationships, x => x.Contains("procedure:dbo.RegisterUser->reads_table->database-table:dbo.Users", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryService_StitchesFrontendToBackendProcedureAndTableChain()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

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
                private readonly IUserRegistrationService _registrationService;

                public AuthController(IUserRegistrationService registrationService)
                {
                    _registrationService = registrationService;
                }

                [HttpPost("register")]
                public IActionResult Register()
                {
                    _registrationService.RegisterAsync();
                    return Ok();
                }
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "UserRegistrationService.cs"),
            """
            public sealed class UserRegistrationService : IUserRegistrationService
            {
                private readonly IUserRepository _userRepository;

                public UserRegistrationService(IUserRepository userRepository)
                {
                    _userRepository = userRepository;
                }

                public void RegisterAsync()
                {
                    _userRepository.CreateAsync();
                }
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "UserRepository.cs"),
            """
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
            Path.Combine(workspacePath, "Program.cs"),
            """
            services.AddScoped<IUserRegistrationService, UserRegistrationService>();
            services.AddScoped<IUserRepository, UserRepository>();
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

        var service = CreateService();

        await service.InitializeAsync(workspacePath, CancellationToken.None);

        var query = new WorkspaceQueryService();
        var chains = await query.ListFlowChainsAsync(workspacePath, "register", null, "dbo.Users", "compact", false, 8, 10, CancellationToken.None);

        Assert.True(chains.Any(x => x.Contains("register.component.ts::register", StringComparison.Ordinal)
            && x.Contains("AuthController.cs::Register", StringComparison.Ordinal)
            && x.Contains("IUserRegistrationService.RegisterAsync", StringComparison.Ordinal)
            && x.Contains("UserRegistrationService.cs::RegisterAsync", StringComparison.Ordinal)
            && x.Contains("IUserRepository.CreateAsync", StringComparison.Ordinal)
            && x.Contains("UserRepository.cs::CreateAsync", StringComparison.Ordinal)
            && x.Contains("dbo.RegisterUser", StringComparison.Ordinal)
            && x.Contains("dbo.Users", StringComparison.Ordinal)),
            string.Join(Environment.NewLine, chains));

        var treeChains = await query.ListFlowChainsAsync(workspacePath, "register", null, "dbo.Users", "tree", false, 8, 1, CancellationToken.None);
        Assert.Single(treeChains);
        Assert.Contains(Environment.NewLine, treeChains[0], StringComparison.Ordinal);
        Assert.Contains("resolved_to -> symbol:UserRegistrationService.cs::RegisterAsync", treeChains[0], StringComparison.Ordinal);
        Assert.Contains("matches_backend_handler -> symbol:AuthController.cs::Register\trelationship:", treeChains[0], StringComparison.Ordinal);
        Assert.Contains("writes_table -> database-table:dbo.Users\trelationship:", treeChains[0], StringComparison.Ordinal);

        var jsonChains = await query.ListFlowChainsAsync(workspacePath, "register", null, "dbo.Users", "json", false, 8, 1, CancellationToken.None);
        Assert.Single(jsonChains);
        Assert.Contains("\"nodes\"", jsonChains[0], StringComparison.Ordinal);
        Assert.Contains("\"edges\"", jsonChains[0], StringComparison.Ordinal);
        Assert.Contains("\"relationship\":\"writes_table\"", jsonChains[0], StringComparison.Ordinal);
        Assert.Contains("\"evidenceItemId\":\"relationship:", jsonChains[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepositoryExplorerRelatedItems_ReturnsBackendBackedContextForSelectedApiRow()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        await CreateRepositoryExplorerRelatedSampleAsync(workspacePath);

        var service = CreateService();
        await service.InitializeAsync(workspacePath, CancellationToken.None);

        var query = new WorkspaceQueryService();
        var apiRows = await query.ListRepositoryExplorerItemsAsync(workspacePath, "apis", "register", null, false, 10, CancellationToken.None);
        var selectedApi = Assert.Single(apiRows, x => x.RelationshipKind == "handles_api");

        Assert.Contains("register", selectedApi.DisplayTitle, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Handled by", selectedApi.DisplaySubtitle, StringComparison.Ordinal);
        Assert.Contains("Evidence #", selectedApi.DisplayLocator, StringComparison.Ordinal);
        Assert.Contains("handles api", selectedApi.EvidenceSummary, StringComparison.Ordinal);
        Assert.Contains("apis|", selectedApi.OccurrenceKey, StringComparison.Ordinal);

        var related = await query.ListRepositoryExplorerRelatedItemsAsync(workspacePath, selectedApi.Surface, selectedApi.Id, false, 6, CancellationToken.None);

        Assert.Contains(related, x => x.Label == "Outgoing from this evidence");
        Assert.Contains(related, x => x.Label == "Incoming to this evidence");
        Assert.Contains(related, x => x.Label == "Same file or source area");

        var relatedRows = related.SelectMany(x => x.Rows).ToList();
        Assert.All(relatedRows, x => Assert.NotEqual(selectedApi.Id, x.Id));
        Assert.All(relatedRows, x => Assert.False(string.IsNullOrWhiteSpace(x.DisplayTitle)));
        Assert.All(relatedRows, x => Assert.False(string.IsNullOrWhiteSpace(x.DisplaySubtitle)));
        Assert.All(relatedRows, x => Assert.False(string.IsNullOrWhiteSpace(x.DisplayLocator)));
        Assert.All(relatedRows, x => Assert.False(string.IsNullOrWhiteSpace(x.EvidenceSummary)));
        Assert.All(relatedRows, x => Assert.False(string.IsNullOrWhiteSpace(x.OccurrenceKey)));
        Assert.Contains(relatedRows, x => x.Surface == "backend" && x.RelationshipKind == "calls_method" && x.TargetIdentifier == "IUserRegistrationService.RegisterAsync");
        Assert.Contains(relatedRows, x => x.Surface == "backend" && x.RelationshipKind == "matches_backend_handler" && x.SourceIdentifier == "register.component.ts::register");
        Assert.Contains(relatedRows, x => x.Surface == "backend" && x.RelationshipKind == "calls_api" && x.TargetIdentifier == "POST /api/register");
        Assert.Contains(relatedRows, x => x.Surface == "backend" && x.RelationshipKind == "contains_symbol" && x.SourceIdentifier == "AuthController.cs");
    }

    [Fact]
    public async Task PreviewRows_IncludeStableRepositoryExplorerEvidenceIds()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        await CreateRepositoryExplorerRelatedSampleAsync(workspacePath);

        var service = CreateService();
        await service.InitializeAsync(workspacePath, CancellationToken.None);

        var query = new WorkspaceQueryService();
        var files = await query.ListFilesAsync(workspacePath, null, null, 10, CancellationToken.None);
        var apis = await query.ListApisAsync(workspacePath, "POST", "register", "Auth", false, 10, CancellationToken.None);
        var backend = await query.SearchRelationshipsAsync(workspacePath, null, "calls_method", null, "IUserRegistrationService", false, 10, CancellationToken.None);

        Assert.NotEmpty(files);
        Assert.Single(apis);
        Assert.NotEmpty(backend);
        AssertStableEvidenceId("file:", files[0]);
        AssertStableEvidenceId("relationship:", apis[0]);
        AssertStableEvidenceId("relationship:", backend[0]);
    }

    [Fact]
    public async Task RepositoryExplorerItems_HydrateExactSelectedEvidenceOutsideCurrentPage()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        await CreateRepositoryExplorerRelatedSampleAsync(workspacePath);

        var service = CreateService();
        await service.InitializeAsync(workspacePath, CancellationToken.None);

        await using var db = await WorkspaceDatabase.OpenMigratedAsync(workspacePath, CancellationToken.None);
        var selectedRelationship = await db.CodeRelationships
            .Where(x => x.RelationshipKind == "writes_table")
            .SingleAsync(CancellationToken.None);
        var selectedItemId = $"relationship:{selectedRelationship.Id}";

        var query = new WorkspaceQueryService();
        var explorerRows = await query.ListRepositoryExplorerItemsAsync(workspacePath, "backend", "", selectedItemId, false, 1, CancellationToken.None);

        Assert.Contains(explorerRows, x => x.Id == selectedItemId);
        Assert.Equal(selectedRelationship.TargetIdentifier, explorerRows.Single(x => x.Id == selectedItemId).TargetIdentifier);
    }

    [Fact]
    public async Task InitializeAsync_CreatesMinimalApiAndAzureFunctionRelationships()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "Program.cs"),
            """app.MapPost("/api/register", () => Results.Ok());""");

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "RegisterFunction.cs"),
            """
            using Microsoft.Azure.Functions.Worker;
            using Microsoft.Azure.Functions.Worker.Http;

            public sealed class RegisterFunction
            {
                [Function("Register")]
                public object Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/register")] object request) => new();
            }
            """);

        var service = CreateService();

        await service.InitializeAsync(workspacePath, CancellationToken.None);

        await using var db = await WorkspaceDatabase.OpenMigratedAsync(workspacePath, CancellationToken.None);
        var relationships = await db.CodeRelationships
            .Select(x => $"{x.SourceKind}:{x.SourceIdentifier}->{x.RelationshipKind}->{x.TargetKind}:{x.TargetIdentifier}")
            .ToListAsync();

        Assert.Contains(relationships, x => x.Contains("api:POST /api/register->handled_by->minimal-api:POST /api/register", StringComparison.Ordinal));
        Assert.Contains(relationships, x => x.Contains("symbol:RegisterFunction.cs::Run->handles_api->api:POST /api/register", StringComparison.Ordinal));
        Assert.Contains(relationships, x => x.Contains("api:POST /api/register->handled_by->azure-function:RegisterFunction.cs::Run", StringComparison.Ordinal));
        Assert.Contains(relationships, x => x.Contains("uses_azure_service->azure-service:Azure Functions", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryService_FiltersApisAzureAndSummaryForLargeRepoStyleInspection()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        Directory.CreateDirectory(Path.Combine(workspacePath, "Sample.Tests"));

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "AccountSetupController.cs"),
            """
            using Azure.Storage.Blobs;
            using Microsoft.AspNetCore.Mvc;

            [Route("api/[controller]")]
            public sealed class AccountSetupController : ControllerBase
            {
                [HttpPost("save")]
                public IActionResult Save() => Ok();
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "Sample.Tests", "AccountSetupControllerTests.cs"),
            """
            using Azure.Storage.Blobs;
            public sealed class AccountSetupControllerTests { }
            """);

        var service = CreateService();

        await service.InitializeAsync(workspacePath, CancellationToken.None);

        var query = new WorkspaceQueryService();
        var apis = await query.ListApisAsync(workspacePath, "POST", "accountsetup", "AccountSetup", false, 10, CancellationToken.None);
        var azure = await query.ListAzureServicesAsync(workspacePath, "Blob", false, 10, CancellationToken.None);
        var summary = await query.GetSummaryAsync(workspacePath, false, 10, CancellationToken.None);

        Assert.Single(apis);
        Assert.Contains("POST /api/AccountSetup/save", apis[0], StringComparison.Ordinal);
        AssertStableEvidenceId("relationship:", apis[0]);
        Assert.Single(azure);
        Assert.DoesNotContain("Tests", azure[0], StringComparison.OrdinalIgnoreCase);
        AssertStableEvidenceId("relationship:", azure[0]);
        Assert.NotNull(summary);
        Assert.DoesNotContain(summary!.LanguageCounts, x => x.Contains("Sample.Tests", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InitializeAsync_MatchesFrontendApiCallsToBackendHandlers()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "register.component.ts"),
            """
            export class RegisterComponent {
                register() {
                    this.http.post('/api/register', {});
                    this.http.post('/v4/engagements/123/accountsetup', {});
                }
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "RegistrationController.cs"),
            """
            using Microsoft.AspNetCore.Mvc;

            [Route("api")]
            public sealed class RegistrationController : ControllerBase
            {
                [HttpPost("register")]
                public IActionResult Register() => Ok();
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "AccountSetupController.cs"),
            """
            using Microsoft.AspNetCore.Mvc;

            [Route("v4/engagements/{engagementId}/accountsetup")]
            public sealed class AccountSetupController : ControllerBase
            {
                [HttpPost]
                public IActionResult Save() => Ok();
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "TrialBalanceController.cs"),
            """
            using Microsoft.AspNetCore.Mvc;

            [Route("v4/engagements/{engagementId}/TrialBalance/GetConsolidationTBSummary/{currency}")]
            public sealed class TrialBalanceController : ControllerBase
            {
                [HttpGet]
                public IActionResult GetConsolidationTBSummary() => Ok();
            }
            """);

        var service = CreateService();

        await service.InitializeAsync(workspacePath, CancellationToken.None);

        var query = new WorkspaceQueryService();
        var flows = await query.ListFlowsAsync(workspacePath, null, null, null, false, 20, CancellationToken.None);

        Assert.Contains(flows, x => x.Contains("register.component.ts::register", StringComparison.Ordinal)
            && x.Contains("RegistrationController.cs::Register", StringComparison.Ordinal)
            && x.Contains("match=exact", StringComparison.Ordinal));
        Assert.Contains(flows, x => x.Contains("register.component.ts::register", StringComparison.Ordinal)
            && x.Contains("AccountSetupController.cs::Save", StringComparison.Ordinal)
            && x.Contains("match=template", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InitializeAsync_ResolvesFrontendRouteVariablesBeforeCrossStackMatching()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "accountSetup.api.js"),
            """
            const engagementsPrefix = '/engagements/{engagementId}';
            const accountSetupPrefix = `${engagementsPrefix}/accountsetup`;
            const routes = {
              save: accountSetupPrefix,
            };

            export function saveAccountSetup() {
              const url = urlFactory(apiSpecifications.financialFacts.key) + routes.save;
              return http.post(url, {});
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "AccountSetupController.cs"),
            """
            using Microsoft.AspNetCore.Mvc;

            [Route("v4/engagements/{engagementId}/accountsetup")]
            public sealed class AccountSetupController : ControllerBase
            {
                [HttpPost]
                public IActionResult Save() => Ok();
            }
            """);

        var service = CreateService();

        await service.InitializeAsync(workspacePath, CancellationToken.None);

        var query = new WorkspaceQueryService();
        var flows = await query.ListFlowsAsync(workspacePath, "accountsetup", null, null, false, 20, CancellationToken.None);

        Assert.Contains(flows, x => x.Contains("accountSetup.api.js::saveAccountSetup", StringComparison.Ordinal)
            && x.Contains("AccountSetupController.cs::Save", StringComparison.Ordinal));
        Assert.DoesNotContain(flows, x => x.Contains("TrialBalanceController.cs::GetConsolidationTBSummary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RuntimeMap_StitchesRouteUiServiceApiBackendAndNavigation()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "app-routing.module.ts"),
            """
            const routes = [
              { path: 'login', component: LoginComponent }
            ];
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "login.component.html"),
            """<form (ngSubmit)="submit()"></form>""");

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "login.component.ts"),
            """
            export class LoginComponent {
                submit() {
                    this.authService.login();
                    this.router.navigate(['/home']);
                }
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "auth.service.ts"),
            """
            export class AuthService {
                login() {
                    return this.http.post('/api/auth/login', {});
                }
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "AuthController.cs"),
            """
            using Microsoft.AspNetCore.Mvc;

            [Route("api/auth")]
            public sealed class AuthController : ControllerBase
            {
                [HttpPost("login")]
                public IActionResult Login() => Ok();
            }
            """);

        var service = CreateService();

        await service.InitializeAsync(workspacePath, CancellationToken.None);

        var query = new WorkspaceQueryService();
        var runtimeMap = await query.GetRuntimeFlowMapAsync(workspacePath, false, 10, CancellationToken.None);

        Assert.NotNull(runtimeMap);
        Assert.NotEmpty(runtimeMap!.ExecutionPaths);
        var rendered = string.Join(
            Environment.NewLine,
            runtimeMap.ExecutionPaths.SelectMany(path => path.Flows).SelectMany(flow => flow.Steps.Select(step => $"{step.Stage}:{step.Title}:{step.Detail}")));

        Assert.Contains("Route:/login", rendered, StringComparison.Ordinal);
        Assert.Contains("UI event", rendered, StringComparison.Ordinal);
        Assert.Contains("submit", rendered, StringComparison.Ordinal);
        Assert.Contains("auth.service.ts::login", rendered, StringComparison.Ordinal);
        Assert.Contains("POST /api/auth/login", rendered, StringComparison.Ordinal);
        Assert.Contains("AuthController.cs::Login", rendered, StringComparison.Ordinal);
        Assert.Contains("Navigation outcome:/home", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CSharpBackendTrace_ResolvesKeyedDiBaseClassRepoAndSqlEvidence()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "codeflowiq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "CarryForwardLevviaController.cs"),
            """
            using Microsoft.AspNetCore.Mvc;

            [Route("v4/engagements/{engagementId:guid}/CarryForwardLevvia")]
            public sealed class CarryForwardLevviaController : ControllerBase
            {
                private readonly ICarryForwardLevvia _carryForwardTrialBalanceLevvia;

                public CarryForwardLevviaController(
                    [FromKeyedServices(nameof(CarryForwardTrialBalanceLevviaManager))] ICarryForwardLevvia carryForwardTrialBalanceLevvia)
                {
                    _carryForwardTrialBalanceLevvia = carryForwardTrialBalanceLevvia;
                }

                [HttpPost]
                [Route("FinancialTrialBalanceCarryForward")]
                public async Task<IActionResult> FinancialTrialBalanceCarryForward(Guid engagementId, LevviaCFRequestDto levviaCFRequestDto)
                {
                    var requestedBy = Guid.NewGuid();
                    var isSuccess = await _carryForwardTrialBalanceLevvia.FinancialCarryForwardAsync(engagementId, levviaCFRequestDto, requestedBy);
                    return Ok();
                }
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "CarryForwardTrialBalanceLevviaManager.cs"),
            """
            public sealed class CarryForwardTrialBalanceLevviaManager : CarryForwardLevviaBase
            {
                public CarryForwardTrialBalanceLevviaManager(ICarryForwardLevviaRepo carryForwardLevviaRepo, ILevviaSystem levviaSystem, IShardProvider shardProvider, ICortexMigrationDocumentProvider cortexMigrationDocumentProvider)
                    : base(carryForwardLevviaRepo, levviaSystem, shardProvider, cortexMigrationDocumentProvider)
                {
                }

                public override async Task<bool> FinancialCarryForwardAsync(Guid engagementId, LevviaCFRequestDto levviaCFRequestDto, Guid requestedBy)
                {
                    return await FinancialCarryForwardInternalAsync(engagementId, levviaCFRequestDto, requestedBy);
                }

                protected override async Task<bool> ProcessDataAsync()
                {
                    await carryForwardLevviaRepo.SaveFinancialTrialBalanceCarryForward(Guid.NewGuid(), new LevviaCFRequestDto());
                    return true;
                }
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "CarryForwardLevviaBase.cs"),
            """
            public abstract class CarryForwardLevviaBase : ICarryForwardLevvia
            {
                protected readonly ICarryForwardLevviaRepo carryForwardLevviaRepo;
                protected readonly ILevviaSystem levviaSystem;
                protected readonly IShardProvider shardProvider;
                protected readonly ICortexMigrationDocumentProvider cortexMigrationDocumentProvider;

                protected CarryForwardLevviaBase(ICarryForwardLevviaRepo carryForwardLevviaRepo, ILevviaSystem levviaSystem, IShardProvider shardProvider, ICortexMigrationDocumentProvider cortexMigrationDocumentProvider)
                {
                    this.carryForwardLevviaRepo = carryForwardLevviaRepo;
                    this.levviaSystem = levviaSystem;
                    this.shardProvider = shardProvider;
                    this.cortexMigrationDocumentProvider = cortexMigrationDocumentProvider;
                }

                public abstract Task<bool> FinancialCarryForwardAsync(Guid engagementId, LevviaCFRequestDto levviaCFRequestDto, Guid requestedBy);
                protected abstract Task<bool> ProcessDataAsync();

                protected async Task<bool> FinancialCarryForwardInternalAsync(Guid engagementId, LevviaCFRequestDto levviaCFRequestDto, Guid requestedBy)
                {
                    await GetAndCreateManifest(engagementId, levviaCFRequestDto, requestedBy);
                    await GetManifestFromLevvia(engagementId, levviaCFRequestDto, requestedBy);
                    await UploadFileToBlobAsync();
                    return await ProcessDataAsync();
                }

                private async Task GetAndCreateManifest(Guid engagementId, LevviaCFRequestDto levviaCFRequestDto, Guid requestedBy)
                {
                    var existingManifest = await carryForwardLevviaRepo.GetCFManifestAsync(levviaCFRequestDto.TransitionId, engagementId);
                    if (existingManifest == null)
                    {
                        await carryForwardLevviaRepo.SaveCFManifestAsync(new CFManifest());
                    }
                }

                private async Task GetManifestFromLevvia(Guid engagementId, LevviaCFRequestDto levviaCFRequestDto, Guid requestedBy)
                {
                    await shardProvider.GetAsync(engagementId);
                    await levviaSystem.GetManifestFileFromLevvia(levviaCFRequestDto.TransitionId, GetType().Name);
                }

                private async Task UploadFileToBlobAsync()
                {
                    await cortexMigrationDocumentProvider.UploadTBFileToBlob();
                }
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "CarryForwardLevviaRepo.cs"),
            """
            using System.Data;

            public sealed class CarryForwardLevviaRepo : ICarryForwardLevviaRepo
            {
                private readonly ISqlDatabase database;

                public CarryForwardLevviaRepo(ISqlDatabase database)
                {
                    this.database = database;
                }

                public async Task<CFManifest?> GetCFManifestAsync(Guid transitionId, Guid engagementId)
                {
                    var sqlCommand = "SELECT [Id] FROM [fin].[CFManifest] WHERE TransitionId = @TransitionId";
                    return await database.Query<CFManifest>(sqlCommand);
                }

                public async Task SaveCFManifestAsync(CFManifest manifest)
                {
                    await database.Execute("[fin].[USP_SaveCFManifest]", commandType: CommandType.StoredProcedure);
                }

                public async Task SaveFinancialTrialBalanceCarryForward(Guid engagementId, LevviaCFRequestDto dto)
                {
                    await database.Execute("[fin].[USP_SaveTrialBalanceCarryForward]", commandType: CommandType.StoredProcedure);
                }
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "Program.cs"),
            """
            services.AddKeyedTransient<ICarryForwardLevvia, CarryForwardTrialBalanceLevviaManager>(nameof(CarryForwardTrialBalanceLevviaManager));
            services.AddSingleton<ICarryForwardLevviaRepo, CarryForwardLevviaRepo>();
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "Contracts.cs"),
            """
            using System.Data;

            public interface ICarryForwardLevvia
            {
                Task<bool> FinancialCarryForwardAsync(Guid engagementId, LevviaCFRequestDto levviaCFRequestDto, Guid requestedBy);
            }

            public interface ICarryForwardLevviaRepo
            {
                Task<CFManifest?> GetCFManifestAsync(Guid transitionId, Guid engagementId);
                Task SaveCFManifestAsync(CFManifest manifest);
                Task SaveFinancialTrialBalanceCarryForward(Guid engagementId, LevviaCFRequestDto dto);
            }

            public interface ILevviaSystem
            {
                Task GetManifestFileFromLevvia(Guid transitionId, string processName);
            }

            public sealed class LevviaSystem : ILevviaSystem
            {
                public Task GetManifestFileFromLevvia(Guid transitionId, string processName) => Task.CompletedTask;
            }

            public interface IShardProvider
            {
                Task GetAsync(Guid engagementId);
            }

            public sealed class ShardProvider : IShardProvider
            {
                public Task GetAsync(Guid engagementId) => Task.CompletedTask;
            }

            public interface ICortexMigrationDocumentProvider
            {
                Task UploadTBFileToBlob();
            }

            public interface ISqlDatabase
            {
                Task<T?> Query<T>(string sql);
                Task Execute(string sql, CommandType commandType);
            }

            public sealed class LevviaCFRequestDto
            {
                public Guid TransitionId { get; set; }
            }

            public sealed class CFManifest { }
            """);

        var service = CreateService();
        await service.InitializeAsync(workspacePath, CancellationToken.None);

        var query = new WorkspaceQueryService();
        var trace = await query.GetCSharpBackendTraceAsync(
            workspacePath,
            "POST /v4/engagements/{engagementId}/CarryForwardLevvia/FinancialTrialBalanceCarryForward",
            includeTests: false,
            maxDepth: 40,
            CancellationToken.None);

        Assert.NotNull(trace);
        var rendered = string.Join('\n', trace!.Steps.Select(x => $"{x.Stage}: {x.Title} | {x.Detail} | {x.Confidence} | {x.Reason}"));

        Assert.Contains("Keyed DI handoff", rendered, StringComparison.Ordinal);
        Assert.Contains("ICarryForwardLevvia resolves to CarryForwardTrialBalanceLevviaManager", rendered, StringComparison.Ordinal);
        Assert.Contains("Base class call", rendered, StringComparison.Ordinal);
        Assert.Contains("GetAndCreateManifest", rendered, StringComparison.Ordinal);
        Assert.Contains("Reads fin.CFManifest", rendered, StringComparison.Ordinal);
        Assert.Contains("USP_SaveCFManifest", rendered, StringComparison.Ordinal);
        Assert.Contains("IShardProvider resolves to ShardProvider", rendered, StringComparison.Ordinal);
        Assert.Contains("ILevviaSystem resolves to LevviaSystem", rendered, StringComparison.Ordinal);
        Assert.Contains("GetManifestFileFromLevvia", rendered, StringComparison.Ordinal);
        Assert.Contains("UploadTBFileToBlob", rendered, StringComparison.Ordinal);
        Assert.Contains("Override call", rendered, StringComparison.Ordinal);
        Assert.Contains("USP_SaveTrialBalanceCarryForward", rendered, StringComparison.Ordinal);
        Assert.Contains(trace.Steps, x => x.Category == "handoff" && x.Title.Contains("ILevviaSystem resolves", StringComparison.Ordinal));
        Assert.Contains(trace.Steps, x => x.Category == "data" && x.Title.Contains("USP_SaveCFManifest", StringComparison.Ordinal));
        Assert.Contains(trace.Steps, x => x.SourceFilePath == "CarryForwardLevviaController.cs"
            && x.SourceLineNumber is > 0
            && x.SourcePreview?.Contains("FinancialTrialBalanceCarryForward", StringComparison.Ordinal) == true);

        var shortTrace = await query.GetCSharpBackendTraceAsync(
            workspacePath,
            "POST /v4/engagements/{engagementId}/CarryForwardLevvia/FinancialTrialBalanceCarryForward",
            includeTests: false,
            maxDepth: 5,
            CancellationToken.None);

        Assert.NotNull(shortTrace);
        Assert.True(shortTrace!.HasMore);
        Assert.NotNull(shortTrace.ContinuationEntry);
        Assert.Contains("Stopped after", shortTrace.StopReason, StringComparison.Ordinal);
    }

    private sealed class PlainDirectoryGitDetector : IGitWorkspaceDetector
    {
        public GitWorkspaceInfo Detect(string workspacePath) => new(false, null, null, null);
    }

    private static WorkspaceIndexingService CreateService() =>
        new(
            new PlainDirectoryGitDetector(),
            new LanguageDetector(),
            [new CSharpLanguageAnalyzer(), new SqlLanguageAnalyzer(), new JavaScriptTypeScriptLanguageAnalyzer(), new AngularTemplateLanguageAnalyzer()],
            new IndexingOptions());

    private static void AssertStableEvidenceId(string prefix, string row)
    {
        var evidenceId = row
            .Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Last();

        Assert.StartsWith(prefix, evidenceId, StringComparison.Ordinal);
        Assert.True(int.TryParse(evidenceId[prefix.Length..], out _), $"Expected numeric evidence id in row: {row}");
    }

    private static async Task CreateRepositoryExplorerRelatedSampleAsync(string workspacePath)
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
                private readonly IUserRegistrationService _registrationService;

                public AuthController(IUserRegistrationService registrationService)
                {
                    _registrationService = registrationService;
                }

                [HttpPost("register")]
                public IActionResult Register()
                {
                    _registrationService.RegisterAsync();
                    return Ok();
                }
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "UserRegistrationService.cs"),
            """
            public sealed class UserRegistrationService : IUserRegistrationService
            {
                private readonly IUserRepository _userRepository;

                public UserRegistrationService(IUserRepository userRepository)
                {
                    _userRepository = userRepository;
                }

                public void RegisterAsync()
                {
                    _userRepository.CreateAsync();
                }
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "UserRepository.cs"),
            """
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
            Path.Combine(workspacePath, "Program.cs"),
            """
            services.AddScoped<IUserRegistrationService, UserRegistrationService>();
            services.AddScoped<IUserRepository, UserRepository>();
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
}
