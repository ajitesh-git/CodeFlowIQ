using CodeFlowIQ.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;

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
                        AddBackendFlowRelationships(type, relationships);
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
            AddDependencyInjectionRelationships(root, relationships);

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

        foreach (var baseType in type.BaseList?.Types ?? [])
        {
            var baseTypeName = baseType.Type.ToString();
            if (string.IsNullOrWhiteSpace(baseTypeName))
            {
                continue;
            }

            relationships.Add(new DiscoveredRelationship(
                "symbol",
                className,
                "inherits_from",
                "class",
                ToGlobalIdentifier(baseTypeName),
                null));
        }

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

    private static void AddBackendFlowRelationships(ClassDeclarationSyntax type, List<DiscoveredRelationship> relationships)
    {
        var className = type.Identifier.Text;
        var memberTypes = GetMemberTypes(type);

        AddConstructorDependencyRelationships(type, relationships);

        foreach (var dependencyType in memberTypes.Values.Distinct(StringComparer.Ordinal))
        {
            relationships.Add(new DiscoveredRelationship(
                "symbol",
                className,
                "depends_on",
                "service",
                ToGlobalIdentifier(dependencyType),
                null));
        }

        foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
        {
            var methodName = method.Identifier.Text;

            foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                AddMethodCallRelationship(methodName, invocation, memberTypes, relationships);
                AddDirectMethodCallRelationship(methodName, invocation, relationships);
                AddEntityFrameworkRelationship(methodName, invocation, memberTypes, relationships);
                AddSqlProcedureRelationship(methodName, invocation, relationships);
            }
        }
    }

    private static void AddConstructorDependencyRelationships(ClassDeclarationSyntax type, List<DiscoveredRelationship> relationships)
    {
        var className = type.Identifier.Text;

        foreach (var constructor in type.Members.OfType<ConstructorDeclarationSyntax>())
        {
            var parameters = constructor.ParameterList.Parameters
                .Where(x => x.Type is not null)
                .ToDictionary(x => x.Identifier.Text, x => x, StringComparer.Ordinal);

            foreach (var parameter in parameters.Values)
            {
                var parameterType = parameter.Type?.ToString();
                if (string.IsNullOrWhiteSpace(parameterType))
                {
                    continue;
                }

                var key = GetFromKeyedServicesKey(parameter);
                if (key is null)
                {
                    continue;
                }

                var fieldName = constructor.DescendantNodes()
                    .OfType<AssignmentExpressionSyntax>()
                    .Select(x => new { Left = GetIdentifierName(x.Left), Right = GetIdentifierName(x.Right) })
                    .Where(x => x.Right == parameter.Identifier.Text && !string.IsNullOrWhiteSpace(x.Left))
                    .Select(x => x.Left)
                    .FirstOrDefault();

                relationships.Add(new DiscoveredRelationship(
                    "symbol",
                    className,
                    "depends_on",
                    "service",
                    ToGlobalIdentifier(parameterType),
                    $"parameter={parameter.Identifier.Text};field={fieldName};key={key};source=FromKeyedServices"));
            }
        }
    }

    private static Dictionary<string, string> GetMemberTypes(ClassDeclarationSyntax type)
    {
        var memberTypes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var field in type.Members.OfType<FieldDeclarationSyntax>())
        {
            var fieldType = field.Declaration.Type.ToString();
            foreach (var variable in field.Declaration.Variables)
            {
                memberTypes[variable.Identifier.Text] = fieldType;
            }
        }

        foreach (var constructor in type.Members.OfType<ConstructorDeclarationSyntax>())
        {
            foreach (var parameter in constructor.ParameterList.Parameters)
            {
                if (parameter.Type is not null)
                {
                    memberTypes[parameter.Identifier.Text] = parameter.Type.ToString();
                }
            }

            foreach (var assignment in constructor.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                var leftName = GetIdentifierName(assignment.Left);
                var rightName = GetIdentifierName(assignment.Right);
                if (leftName is null || rightName is null || !memberTypes.TryGetValue(rightName, out var parameterType))
                {
                    continue;
                }

                memberTypes[leftName] = parameterType;
            }
        }

        return memberTypes;
    }

    private static void AddMethodCallRelationship(
        string sourceMethodName,
        InvocationExpressionSyntax invocation,
        IReadOnlyDictionary<string, string> memberTypes,
        List<DiscoveredRelationship> relationships)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        var receiverName = GetIdentifierName(memberAccess.Expression);
        if (receiverName is null || !memberTypes.TryGetValue(receiverName, out var receiverType))
        {
            return;
        }

        var targetMethodName = memberAccess.Name.Identifier.Text;
        if (IsEntityFrameworkMethod(targetMethodName)
            || LooksLikeSqlExecutionMethod(targetMethodName)
            || targetMethodName is "GetShardConnection" or "GetReadonlyShardConnection"
            || targetMethodName.StartsWith("Log", StringComparison.Ordinal)
            || receiverType.StartsWith("ILogger", StringComparison.Ordinal))
        {
            return;
        }

        relationships.Add(new DiscoveredRelationship(
            "symbol",
            sourceMethodName,
            "calls_method",
            "method",
            ToGlobalIdentifier($"{receiverType}.{targetMethodName}"),
            $"receiver={receiverName}"));
    }

    private static void AddDirectMethodCallRelationship(
        string sourceMethodName,
        InvocationExpressionSyntax invocation,
        List<DiscoveredRelationship> relationships)
    {
        if (invocation.Expression is not IdentifierNameSyntax identifier)
        {
            return;
        }

        var targetMethodName = identifier.Identifier.Text;
        if (targetMethodName is "nameof" or "ConfigureAwait" or "Ok" or "BadRequest" or "StatusCode" or "GetType" or "ReferenceEquals")
        {
            return;
        }

        relationships.Add(new DiscoveredRelationship(
            "symbol",
            sourceMethodName,
            "calls_method",
            "method",
            targetMethodName,
            "receiver=this;direct=true"));
    }

    private static void AddEntityFrameworkRelationship(
        string sourceMethodName,
        InvocationExpressionSyntax invocation,
        IReadOnlyDictionary<string, string> memberTypes,
        List<DiscoveredRelationship> relationships)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        var invokedMethodName = memberAccess.Name.Identifier.Text;
        var receiverExpression = memberAccess.Expression;

        if (invokedMethodName is "SaveChanges" or "SaveChangesAsync")
        {
            var receiverName = GetIdentifierName(receiverExpression);
            var target = receiverName is not null && memberTypes.TryGetValue(receiverName, out var contextType)
                ? contextType
                : receiverName ?? "DbContext";

            relationships.Add(new DiscoveredRelationship(
                "symbol",
                sourceMethodName,
                "saves_changes",
                "db-context",
                ToGlobalIdentifier(target),
                receiverName is null ? null : $"receiver={receiverName}"));
            return;
        }

        if (receiverExpression is not MemberAccessExpressionSyntax dbSetAccess)
        {
            return;
        }

        var contextName = GetIdentifierName(dbSetAccess.Expression);
        if (contextName is null || !memberTypes.ContainsKey(contextName))
        {
            return;
        }

        if (!IsLikelyDbContextType(memberTypes[contextName]))
        {
            return;
        }

        var tableName = dbSetAccess.Name.Identifier.Text;
        var relationshipKind = invokedMethodName switch
        {
            "Add" or "AddAsync" or "AddRange" or "AddRangeAsync" or "Update" or "UpdateRange" or "Remove" or "RemoveRange" => "writes_table",
            "Find" or "FindAsync" or "Where" or "First" or "FirstAsync" or "FirstOrDefault" or "FirstOrDefaultAsync" or "Single" or "SingleAsync" or "SingleOrDefault" or "SingleOrDefaultAsync" or "ToList" or "ToListAsync" => "reads_table",
            _ => null
        };

        if (relationshipKind is null)
        {
            return;
        }

        relationships.Add(new DiscoveredRelationship(
            "symbol",
            sourceMethodName,
            relationshipKind,
            "database-table",
            tableName,
            $"context={contextName};method={invokedMethodName}"));
    }

    private static void AddSqlProcedureRelationship(
        string sourceMethodName,
        InvocationExpressionSyntax invocation,
        List<DiscoveredRelationship> relationships)
    {
        var invokedMethodName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => string.Empty
        };

        if (!LooksLikeSqlExecutionMethod(invokedMethodName)
            && !InvocationMentionsStoredProcedureCommandType(invocation))
        {
            return;
        }

        var sqlTexts = invocation.ArgumentList.Arguments
            .SelectMany(x => x.Expression.DescendantNodesAndSelf())
            .OfType<LiteralExpressionSyntax>()
            .Where(x => x.IsKind(SyntaxKind.StringLiteralExpression))
            .Select(x => x.Token.ValueText)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Concat(invocation.ArgumentList.Arguments
                .Select(x => x.Expression)
                .OfType<IdentifierNameSyntax>()
                .Select(x => FindStringVariableValue(invocation, x.Identifier.Text))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var literal in sqlTexts)
        {
            foreach (var procedureName in ExtractProcedureNames(literal))
            {
                relationships.Add(new DiscoveredRelationship(
                    "symbol",
                    sourceMethodName,
                    "executes_procedure",
                    "procedure",
                    ToGlobalIdentifier(procedureName),
                    $"method={invokedMethodName}"));
            }

            foreach (var tableReference in ExtractTableReferences(literal))
            {
                relationships.Add(new DiscoveredRelationship(
                    "symbol",
                    sourceMethodName,
                    tableReference.Operation,
                    "database-table",
                    ToGlobalIdentifier(tableReference.TableName),
                    $"method={invokedMethodName}"));
            }
        }
    }

    private static string? FindStringVariableValue(SyntaxNode node, string variableName)
    {
        var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        return method?.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(x => x.Identifier.Text == variableName)
            .Select(x => x.Initializer?.Value)
            .OfType<LiteralExpressionSyntax>()
            .Where(x => x.IsKind(SyntaxKind.StringLiteralExpression))
            .Select(x => x.Token.ValueText)
            .FirstOrDefault();
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
        var methodRoute = GetRouteTemplate(method.AttributeLists, "Route");

        foreach (var endpoint in GetHttpEndpoints(method.AttributeLists))
        {
            var route = CombineRoutes(controllerRoute, endpoint.RouteTemplate ?? methodRoute, controllerName, method.Identifier.Text);
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

    private static void AddDependencyInjectionRelationships(CompilationUnitSyntax root, List<DiscoveredRelationship> relationships)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                continue;
            }

            var methodName = memberAccess.Name.Identifier.Text;
            if (methodName is not ("AddScoped" or "AddTransient" or "AddSingleton"
                or "AddKeyedScoped" or "AddKeyedTransient" or "AddKeyedSingleton"))
            {
                continue;
            }

            if (memberAccess.Name is not GenericNameSyntax genericName || genericName.TypeArgumentList.Arguments.Count < 2)
            {
                continue;
            }

            var serviceType = genericName.TypeArgumentList.Arguments[0].ToString();
            var implementationType = genericName.TypeArgumentList.Arguments[1].ToString();
            var key = methodName.StartsWith("AddKeyed", StringComparison.Ordinal)
                ? invocation.ArgumentList.Arguments.Select(x => TryGetKeyExpressionValue(x.Expression)).FirstOrDefault(x => x is not null)
                : null;

            relationships.Add(new DiscoveredRelationship(
                "service",
                ToGlobalIdentifier(serviceType),
                "implemented_by",
                "class",
                ToGlobalIdentifier(implementationType),
                key is null ? $"lifetime={methodName}" : $"lifetime={methodName};key={key}"));
        }
    }

    private static string? GetFromKeyedServicesKey(ParameterSyntax parameter) =>
        parameter.AttributeLists
            .SelectMany(x => x.Attributes)
            .Where(x => AttributeNameMatches(x.Name.ToString(), "FromKeyedServices"))
            .SelectMany(x => x.ArgumentList?.Arguments ?? [])
            .Select(x => TryGetKeyExpressionValue(x.Expression))
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    private static string? TryGetKeyExpressionValue(ExpressionSyntax expression) =>
        expression switch
        {
            LiteralExpressionSyntax literal => literal.Token.ValueText,
            InvocationExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
                ArgumentList.Arguments.Count: 1
            } invocation => invocation.ArgumentList.Arguments[0].Expression.ToString(),
            _ => null
        };

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

    private static string? GetIdentifierName(ExpressionSyntax expression) =>
        expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            MemberAccessExpressionSyntax memberAccess when memberAccess.Expression is ThisExpressionSyntax => memberAccess.Name.Identifier.Text,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            _ => null
        };

    private static bool IsEntityFrameworkMethod(string methodName) =>
        methodName is "Add" or "AddAsync" or "AddRange" or "AddRangeAsync"
            or "Update" or "UpdateRange"
            or "Remove" or "RemoveRange"
            or "Find" or "FindAsync"
            or "Where"
            or "First" or "FirstAsync" or "FirstOrDefault" or "FirstOrDefaultAsync"
            or "Single" or "SingleAsync" or "SingleOrDefault" or "SingleOrDefaultAsync"
            or "ToList" or "ToListAsync"
            or "SaveChanges" or "SaveChangesAsync";

    private static bool IsLikelyDbContextType(string typeName) =>
        typeName.Equals("DbContext", StringComparison.Ordinal)
        || typeName.EndsWith("DbContext", StringComparison.Ordinal)
        || typeName.EndsWith("DbContext?", StringComparison.Ordinal)
        || typeName.Contains(".DbContext", StringComparison.Ordinal)
        || typeName.EndsWith("Context", StringComparison.Ordinal)
        || typeName.EndsWith("Context?", StringComparison.Ordinal);

    private static bool LooksLikeSqlExecutionMethod(string methodName) =>
        methodName.Contains("Sql", StringComparison.OrdinalIgnoreCase)
        || methodName.Contains("Query", StringComparison.OrdinalIgnoreCase)
        || methodName.Contains("Execute", StringComparison.OrdinalIgnoreCase)
        || methodName.Contains("Procedure", StringComparison.OrdinalIgnoreCase)
        || methodName is "FromSqlRaw" or "FromSqlInterpolated";

    private static bool InvocationMentionsStoredProcedureCommandType(InvocationExpressionSyntax invocation) =>
        invocation.ArgumentList.Arguments
            .SelectMany(x => x.Expression.DescendantNodesAndSelf())
            .OfType<MemberAccessExpressionSyntax>()
            .Any(x => x.ToString().EndsWith("CommandType.StoredProcedure", StringComparison.Ordinal));

    private static IEnumerable<string> ExtractProcedureNames(string sqlText)
    {
        var normalized = sqlText.Trim();
        foreach (Match match in Regex.Matches(
            normalized,
            @"\b(?:EXEC(?:UTE)?|CALL)\s+(?:\[?(?<schema>[A-Za-z_][\w$]*)\]?\.)?\[?(?<name>[A-Za-z_][\w$]*)\]?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var name = match.Groups["name"].Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var schema = match.Groups["schema"].Success ? match.Groups["schema"].Value : null;
            yield return string.IsNullOrWhiteSpace(schema) ? name : $"{schema}.{name}";
        }

        if (Regex.IsMatch(normalized, @"^[\[\]\w$]+(\.[\[\]\w$]+)?$", RegexOptions.CultureInvariant)
            && !normalized.Contains(' ', StringComparison.Ordinal)
            && (normalized.Contains('.', StringComparison.Ordinal)
                || normalized.EndsWith("Procedure", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("Proc", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("sp", StringComparison.OrdinalIgnoreCase)))
        {
            yield return normalized.Replace("[", string.Empty, StringComparison.Ordinal)
                .Replace("]", string.Empty, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<(string Operation, string TableName)> ExtractTableReferences(string sqlText)
    {
        foreach (Match match in Regex.Matches(
            sqlText,
            @"\b(?:FROM|JOIN)\s+(?<name>(?:\[[A-Za-z_][\w$]*\]\.)?\[[A-Za-z_][\w$]*\]|(?:[A-Za-z_][\w$]*\.)?[A-Za-z_][\w$]*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            yield return ("reads_table", NormalizeSqlIdentifier(match.Groups["name"].Value));
        }

        foreach (Match match in Regex.Matches(
            sqlText,
            @"\b(?:INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+(?<name>(?:\[[A-Za-z_][\w$]*\]\.)?\[[A-Za-z_][\w$]*\]|(?:[A-Za-z_][\w$]*\.)?[A-Za-z_][\w$]*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            yield return ("writes_table", NormalizeSqlIdentifier(match.Groups["name"].Value));
        }
    }

    private static string NormalizeSqlIdentifier(string identifier) =>
        identifier.Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal);

    private static string ToGlobalIdentifier(string identifier) => "global::" + identifier;

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
