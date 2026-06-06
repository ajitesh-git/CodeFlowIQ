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
        var files = await query.ListFilesAsync(workspacePath, null, 20, CancellationToken.None);

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
