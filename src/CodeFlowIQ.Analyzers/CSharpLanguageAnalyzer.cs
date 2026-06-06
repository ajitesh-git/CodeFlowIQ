using CodeFlowIQ.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeFlowIQ.Analyzers;

public sealed class CSharpLanguageAnalyzer : ILanguageAnalyzer
{
    public string LanguageId => "csharp";

    public bool CanAnalyze(string filePath) =>
        Path.GetExtension(filePath).Equals(".cs", StringComparison.OrdinalIgnoreCase);

    public Task<CodeAnalysisResult> AnalyzeAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        try
        {
            var tree = CSharpSyntaxTree.ParseText(content, cancellationToken: cancellationToken);
            var root = tree.GetCompilationUnitRoot(cancellationToken);
            var symbols = new List<DiscoveredSymbol>();
            var relationships = new List<DiscoveredRelationship>();

            foreach (var azureService in DetectAzureServices(root))
            {
                relationships.Add(new DiscoveredRelationship(
                    "file",
                    "azure-service-reference",
                    "uses_azure_service",
                    "azure-service",
                    azureService,
                    null));
            }

            foreach (var node in root.DescendantNodes())
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (node)
                {
                    case ClassDeclarationSyntax type:
                        symbols.Add(CreateSymbol(tree, type.Identifier.Text, GetClassKind(filePath, type), GetContainer(type), type.Identifier));
                        AddClassRelationships(type, relationships);
                        break;
                    case BaseTypeDeclarationSyntax type:
                        symbols.Add(CreateSymbol(tree, type.Identifier.Text, GetTypeKind(type), GetContainer(type), type.Identifier));
                        break;
                    case MethodDeclarationSyntax method:
                        symbols.Add(CreateSymbol(tree, method.Identifier.Text, "method", GetContainer(method), method.Identifier));
                        AddApiRelationships(method, relationships);
                        AddAzureFunctionRelationships(method, relationships);
                        break;
                    case ConstructorDeclarationSyntax constructor:
                        symbols.Add(CreateSymbol(tree, constructor.Identifier.Text, "constructor", GetContainer(constructor), constructor.Identifier));
                        break;
                    case PropertyDeclarationSyntax property:
                        symbols.Add(CreateSymbol(tree, property.Identifier.Text, "property", GetContainer(property), property.Identifier));
                        break;
                }
            }

            AddMinimalApiRelationships(root, relationships);

            return Task.FromResult(new CodeAnalysisResult(LanguageId, "parsed", symbols, relationships));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(new CodeAnalysisResult(LanguageId, "error", [], [], ex.Message));
        }
    }

    private static DiscoveredSymbol CreateSymbol(SyntaxTree tree, string name, string kind, string? container, SyntaxToken token)
    {
        var lineSpan = tree.GetLineSpan(token.Span);
        return new DiscoveredSymbol(
            name,
            kind,
            container,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1);
    }

    private static string GetTypeKind(BaseTypeDeclarationSyntax type) =>
        type switch
        {
            ClassDeclarationSyntax => "class",
            InterfaceDeclarationSyntax => "interface",
            StructDeclarationSyntax => "struct",
            RecordDeclarationSyntax => "record",
            EnumDeclarationSyntax => "enum",
            _ => "type"
        };

    private static string GetClassKind(string filePath, ClassDeclarationSyntax type)
    {
        var name = type.Identifier.Text;
        var path = filePath.Replace('\\', '/');

        if (name.EndsWith("Controller", StringComparison.Ordinal)
            || HasAttribute(type.AttributeLists, "ApiController")
            || HasAttribute(type.AttributeLists, "Controller"))
        {
            return "controller";
        }

        if (name.EndsWith("Repository", StringComparison.Ordinal))
        {
            return "repository";
        }

        if (name.EndsWith("Function", StringComparison.Ordinal)
            || type.Members.OfType<MethodDeclarationSyntax>().Any(x => HasAttribute(x.AttributeLists, "Function") || HasAttribute(x.AttributeLists, "FunctionName")))
        {
            return "azure-function";
        }

        if (type.BaseList?.Types.Any(x => x.Type.ToString().EndsWith("DbContext", StringComparison.Ordinal)) == true)
        {
            return "db-context";
        }

        if (path.Contains("/Domain/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/Models/", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Model", StringComparison.Ordinal)
            || name.EndsWith("Entity", StringComparison.Ordinal))
        {
            return "domain-model";
        }

        return "class";
    }

    private static void AddClassRelationships(ClassDeclarationSyntax type, List<DiscoveredRelationship> relationships)
    {
        var className = type.Identifier.Text;

        foreach (var property in type.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (!TryGetDbSetEntityName(property.Type.ToString(), out var entityName))
            {
                continue;
            }

            relationships.Add(new DiscoveredRelationship(
                "symbol",
                className,
                "maps_dbset",
                "domain-model",
                entityName,
                $"property={property.Identifier.Text}"));

            relationships.Add(new DiscoveredRelationship(
                "domain-model",
                entityName,
                "maps_to_table",
                "database-table",
                property.Identifier.Text,
                $"dbContext={className}"));
        }
    }

    private static void AddApiRelationships(MethodDeclarationSyntax method, List<DiscoveredRelationship> relationships)
    {
        var classDeclaration = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDeclaration is null)
        {
            return;
        }

        var controllerRoute = GetRouteTemplate(classDeclaration.AttributeLists, "Route");
        var controllerName = TrimControllerSuffix(classDeclaration.Identifier.Text);

        foreach (var endpoint in GetHttpEndpoints(method.AttributeLists))
        {
            var route = CombineRoutes(controllerRoute, endpoint.RouteTemplate, controllerName, method.Identifier.Text);
            var api = $"{endpoint.HttpMethod} {route}";

            relationships.Add(new DiscoveredRelationship(
                "symbol",
                method.Identifier.Text,
                "handles_api",
                "api",
                api,
                $"controller={classDeclaration.Identifier.Text}"));

            relationships.Add(new DiscoveredRelationship(
                "api",
                api,
                "handled_by",
                "method",
                method.Identifier.Text,
                $"controller={classDeclaration.Identifier.Text}"));
        }
    }

    private static void AddAzureFunctionRelationships(MethodDeclarationSyntax method, List<DiscoveredRelationship> relationships)
    {
        var functionName = GetFirstAttributeStringArgument(method.AttributeLists, "Function")
            ?? GetFirstAttributeStringArgument(method.AttributeLists, "FunctionName");

        foreach (var parameter in method.ParameterList.Parameters)
        {
            var httpTrigger = parameter.AttributeLists
                .SelectMany(x => x.Attributes)
                .FirstOrDefault(x => AttributeNameMatches(x.Name.ToString(), "HttpTrigger"));

            if (httpTrigger is null)
            {
                continue;
            }

            var route = GetNamedAttributeStringArgument(httpTrigger, "Route") ?? functionName ?? method.Identifier.Text;
            var methods = httpTrigger.ArgumentList?.Arguments
                .Select(x => x.Expression)
                .OfType<LiteralExpressionSyntax>()
                .Select(x => x.Token.ValueText.ToUpperInvariant())
                .Where(x => x is "GET" or "POST" or "PUT" or "PATCH" or "DELETE")
                .DefaultIfEmpty("ANY")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? ["ANY"];

            foreach (var httpMethod in methods)
            {
                var api = $"{httpMethod} {NormalizeRoute(route)}";
                relationships.Add(new DiscoveredRelationship(
                    "symbol",
                    method.Identifier.Text,
                    "handles_api",
                    "api",
                    api,
                    $"azureFunction={functionName ?? method.Identifier.Text}"));

                relationships.Add(new DiscoveredRelationship(
                    "api",
                    api,
                    "handled_by",
                    "azure-function",
                    method.Identifier.Text,
                    $"azureFunction={functionName ?? method.Identifier.Text}"));
            }
        }
    }

    private static void AddMinimalApiRelationships(CompilationUnitSyntax root, List<DiscoveredRelationship> relationships)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                continue;
            }

            var methodName = memberAccess.Name.Identifier.Text;
            if (!methodName.StartsWith("Map", StringComparison.Ordinal) || methodName is "MapGroup")
            {
                continue;
            }

            var httpMethod = methodName switch
            {
                "MapGet" => "GET",
                "MapPost" => "POST",
                "MapPut" => "PUT",
                "MapPatch" => "PATCH",
                "MapDelete" => "DELETE",
                _ => null
            };

            if (httpMethod is null)
            {
                continue;
            }

            var route = invocation.ArgumentList.Arguments
                .Select(x => x.Expression)
                .OfType<LiteralExpressionSyntax>()
                .FirstOrDefault()?.Token.ValueText;

            if (string.IsNullOrWhiteSpace(route))
            {
                continue;
            }

            var api = $"{httpMethod} {NormalizeRoute(route)}";
            relationships.Add(new DiscoveredRelationship(
                "api",
                api,
                "handled_by",
                "minimal-api",
                api,
                "source=minimal-api"));
        }
    }

    private static IReadOnlyList<(string HttpMethod, string? RouteTemplate)> GetHttpEndpoints(SyntaxList<AttributeListSyntax> attributeLists)
    {
        var endpoints = new List<(string HttpMethod, string? RouteTemplate)>();

        foreach (var attribute in attributeLists.SelectMany(x => x.Attributes))
        {
            var name = attribute.Name.ToString();
            var httpMethod = name switch
            {
                var value when AttributeNameMatches(value, "HttpGet") => "GET",
                var value when AttributeNameMatches(value, "HttpPost") => "POST",
                var value when AttributeNameMatches(value, "HttpPut") => "PUT",
                var value when AttributeNameMatches(value, "HttpPatch") => "PATCH",
                var value when AttributeNameMatches(value, "HttpDelete") => "DELETE",
                _ => null
            };

            if (httpMethod is not null)
            {
                endpoints.Add((httpMethod, GetFirstStringArgument(attribute)));
            }
        }

        return endpoints;
    }

    private static IEnumerable<string> DetectAzureServices(CompilationUnitSyntax root)
    {
        var names = root.Usings.Select(x => x.Name?.ToString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>();

        foreach (var name in names)
        {
            var service = name switch
            {
                var value when value.StartsWith("Azure.Storage.Blobs", StringComparison.Ordinal) => "Azure Blob Storage",
                var value when value.StartsWith("Azure.Storage.Queues", StringComparison.Ordinal) => "Azure Queue Storage",
                var value when value.StartsWith("Azure.Messaging.ServiceBus", StringComparison.Ordinal) => "Azure Service Bus",
                var value when value.StartsWith("Azure.Messaging.EventGrid", StringComparison.Ordinal) => "Azure Event Grid",
                var value when value.StartsWith("Azure.Security.KeyVault", StringComparison.Ordinal) => "Azure Key Vault",
                var value when value.StartsWith("Microsoft.Azure.Cosmos", StringComparison.Ordinal) => "Azure Cosmos DB",
                var value when value.StartsWith("Azure.Identity", StringComparison.Ordinal) => "Azure Identity",
                var value when value.StartsWith("Microsoft.Azure.Functions", StringComparison.Ordinal) => "Azure Functions",
                var value when value.StartsWith("Microsoft.Azure.WebJobs", StringComparison.Ordinal) => "Azure Functions",
                _ => null
            };

            if (service is not null)
            {
                yield return service;
            }
        }
    }

    private static string? GetRouteTemplate(SyntaxList<AttributeListSyntax> attributeLists, string attributeName) =>
        attributeLists
            .SelectMany(x => x.Attributes)
            .Where(x => AttributeNameMatches(x.Name.ToString(), attributeName))
            .Select(GetFirstStringArgument)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    private static string? GetFirstAttributeStringArgument(SyntaxList<AttributeListSyntax> attributeLists, string attributeName) =>
        attributeLists
            .SelectMany(x => x.Attributes)
            .Where(x => AttributeNameMatches(x.Name.ToString(), attributeName))
            .Select(GetFirstStringArgument)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    private static string? GetFirstStringArgument(AttributeSyntax attribute) =>
        attribute.ArgumentList?.Arguments
            .Select(x => x.Expression)
            .OfType<LiteralExpressionSyntax>()
            .FirstOrDefault()?.Token.ValueText;

    private static string? GetNamedAttributeStringArgument(AttributeSyntax attribute, string name) =>
        attribute.ArgumentList?.Arguments
            .Where(x => x.NameEquals?.Name.Identifier.Text == name)
            .Select(x => x.Expression)
            .OfType<LiteralExpressionSyntax>()
            .FirstOrDefault()?.Token.ValueText;

    private static bool HasAttribute(SyntaxList<AttributeListSyntax> attributeLists, string attributeName) =>
        attributeLists.SelectMany(x => x.Attributes).Any(x => AttributeNameMatches(x.Name.ToString(), attributeName));

    private static bool AttributeNameMatches(string actualName, string expectedName) =>
        actualName.Equals(expectedName, StringComparison.Ordinal)
        || actualName.Equals(expectedName + "Attribute", StringComparison.Ordinal)
        || actualName.EndsWith("." + expectedName, StringComparison.Ordinal)
        || actualName.EndsWith("." + expectedName + "Attribute", StringComparison.Ordinal);

    private static bool TryGetDbSetEntityName(string typeName, out string entityName)
    {
        const string dbSetPrefix = "DbSet<";
        if (typeName.StartsWith(dbSetPrefix, StringComparison.Ordinal) && typeName.EndsWith(">", StringComparison.Ordinal))
        {
            entityName = typeName[dbSetPrefix.Length..^1].Trim();
            return true;
        }

        entityName = string.Empty;
        return false;
    }

    private static string CombineRoutes(string? controllerRoute, string? actionRoute, string controllerName, string actionName)
    {
        var prefix = controllerRoute?.Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", actionName, StringComparison.OrdinalIgnoreCase);
        var suffix = actionRoute?.Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", actionName, StringComparison.OrdinalIgnoreCase);

        return NormalizeRoute(string.Join('/', new[] { prefix, suffix }.Where(x => !string.IsNullOrWhiteSpace(x))));
    }

    private static string NormalizeRoute(string route)
    {
        var normalized = route.Trim().Trim('/');
        return "/" + normalized;
    }

    private static string TrimControllerSuffix(string controllerName) =>
        controllerName.EndsWith("Controller", StringComparison.Ordinal)
            ? controllerName[..^"Controller".Length]
            : controllerName;

    private static string? GetContainer(SyntaxNode node)
    {
        var type = node.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
        if (type is not null)
        {
            return type.Identifier.Text;
        }

        return node.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString()
            ?? node.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();
    }
}
