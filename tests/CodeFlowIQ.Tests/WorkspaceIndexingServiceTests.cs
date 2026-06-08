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

        var jsonChains = await query.ListFlowChainsAsync(workspacePath, "register", null, "dbo.Users", "json", false, 8, 1, CancellationToken.None);
        Assert.Single(jsonChains);
        Assert.Contains("\"nodes\"", jsonChains[0], StringComparison.Ordinal);
        Assert.Contains("\"edges\"", jsonChains[0], StringComparison.Ordinal);
        Assert.Contains("\"relationship\":\"writes_table\"", jsonChains[0], StringComparison.Ordinal);
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
        Assert.Single(azure);
        Assert.DoesNotContain("Tests", azure[0], StringComparison.OrdinalIgnoreCase);
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
}
