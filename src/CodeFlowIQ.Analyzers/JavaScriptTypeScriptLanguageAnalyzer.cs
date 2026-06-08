using System.Text.RegularExpressions;
using CodeFlowIQ.Core.Analysis;

namespace CodeFlowIQ.Analyzers;

public sealed class JavaScriptTypeScriptLanguageAnalyzer : ILanguageAnalyzer
{
    private static readonly Regex SymbolPattern = new(
        @"^\s*(?:export\s+)?(?:default\s+)?(?:(class|interface|function|const|let|var|type)\s+([A-Za-z_$][\w$]*)|(?:async\s+)?function\s+([A-Za-z_$][\w$]*)|([A-Za-z_$][\w$]*)\s*[:=]\s*(?:async\s*)?\(?[^=]*?\)?\s*=>)",
        RegexOptions.Compiled);

    private static readonly Regex ClassMethodPattern = new(
        @"^\s*(?:public\s+|private\s+|protected\s+)?(?:async\s+)?([A-Za-z_$][\w$]*)\s*\([^)]*\)\s*(?::\s*[^{]+)?\{?",
        RegexOptions.Compiled);

    private static readonly Regex HttpCallPattern = new(
        @"(?:this\.)?(?:http|httpClient|client)\.(get|post|put|patch|delete)\s*(?:<[^>]+>)?\s*\(\s*([^,\)]+)|fetch\s*\(\s*([^,\)]+)|axios\.(get|post|put|patch|delete)\s*\(\s*([^,\)]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ReactEventPattern = new(
        @"\bon(Click|Submit|Change|Blur|Focus)\s*=\s*\{\s*([A-Za-z_$][\w$]*)\s*\}",
        RegexOptions.Compiled);

    private static readonly Regex MethodCallPattern = new(
        @"\b(?:this\.)?([A-Za-z_$][\w$]*)\.([A-Za-z_$][\w$]*)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex AngularRoutePattern = new(
        @"path\s*:\s*['""]([^'""]*)['""][^{}\r\n]*component\s*:\s*([A-Za-z_$][\w$]*)",
        RegexOptions.Compiled);

    private static readonly Regex ReactRoutePattern = new(
        @"<Route\b[^>]*\bpath\s*=\s*[""']([^""']+)[""'][^>]*(?:\belement\s*=\s*\{\s*<([A-Za-z_$][\w$]*)|\bcomponent\s*=\s*\{\s*([A-Za-z_$][\w$]*))",
        RegexOptions.Compiled);

    private static readonly Regex NavigatePattern = new(
        @"(?:router|history|navigate|navigation)\.(?:navigate|navigateByUrl|push|replace)\s*\(\s*(?:\[\s*)?([^,\]\)]+)|\bnavigate\s*\(\s*([^,\)]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ConstAssignmentPattern = new(
        @"^\s*const\s+([A-Za-z_$][\w$]*)\s*=\s*(.+?);?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex ObjectStartPattern = new(
        @"^\s*const\s+([A-Za-z_$][\w$]*)\s*=\s*\{\s*$",
        RegexOptions.Compiled);

    private static readonly Regex ObjectPropertyPattern = new(
        @"^\s*([A-Za-z_$][\w$]*)\s*:\s*(.+?)(?:,)?\s*$",
        RegexOptions.Compiled);

    public string LanguageId => "typescript";

    public bool CanAnalyze(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".ts", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase);
    }

    public Task<CodeAnalysisResult> AnalyzeAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        var symbols = new List<DiscoveredSymbol>();
        var relationships = new List<DiscoveredRelationship>();
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = new StringReader(content);
        var lineNumber = 0;
        string? currentSymbol = null;
        string? currentObjectName = null;

        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;

            var trimmedLine = line.Trim();
            if (currentObjectName is not null)
            {
                if (trimmedLine.StartsWith("};", StringComparison.Ordinal) || trimmedLine == "}")
                {
                    currentObjectName = null;
                    continue;
                }

                var objectPropertyMatch = ObjectPropertyPattern.Match(line);
                if (objectPropertyMatch.Success
                    && TryResolveExpression(objectPropertyMatch.Groups[2].Value, constants, out var propertyValue))
                {
                    constants[$"{currentObjectName}.{objectPropertyMatch.Groups[1].Value}"] = propertyValue;
                }
            }
            else
            {
                var objectStartMatch = ObjectStartPattern.Match(line);
                if (objectStartMatch.Success)
                {
                    currentObjectName = objectStartMatch.Groups[1].Value;
                    continue;
                }

                var constMatch = ConstAssignmentPattern.Match(line);
                if (constMatch.Success && TryResolveExpression(constMatch.Groups[2].Value, constants, out var constValue))
                {
                    constants[constMatch.Groups[1].Value] = constValue;
                }
            }

            var symbolMatch = SymbolPattern.Match(line);
            var methodMatch = symbolMatch.Success ? Match.Empty : ClassMethodPattern.Match(line);

            if (symbolMatch.Success)
            {
                var kind = symbolMatch.Groups[1].Success ? symbolMatch.Groups[1].Value : "function";
                var name = symbolMatch.Groups[2].Success ? symbolMatch.Groups[2].Value :
                    symbolMatch.Groups[3].Success ? symbolMatch.Groups[3].Value :
                    symbolMatch.Groups[4].Value;

                var normalizedKind = NormalizeKind(kind, filePath, name);
                symbols.Add(new DiscoveredSymbol(name, normalizedKind, null, lineNumber, symbolMatch.Index + 1));
                if (normalizedKind is not ("const" or "let" or "var" or "constant" or "type-alias"))
                {
                    currentSymbol = name;
                }
            }
            else if (methodMatch.Success && !IsControlFlowKeyword(methodMatch.Groups[1].Value))
            {
                var name = methodMatch.Groups[1].Value;
                symbols.Add(new DiscoveredSymbol(name, "method", null, lineNumber, methodMatch.Index + 1));
                currentSymbol = name;
            }

            foreach (Match eventMatch in ReactEventPattern.Matches(line))
            {
                var eventName = $"on{eventMatch.Groups[1].Value}";
                var handlerName = eventMatch.Groups[2].Value;
                relationships.Add(new DiscoveredRelationship(
                    "ui-event",
                    eventName,
                    "invokes_handler",
                    "function",
                    handlerName,
                    $"line={lineNumber}"));
            }

            foreach (Match routeMatch in AngularRoutePattern.Matches(line))
            {
                var route = NormalizeClientRoute(routeMatch.Groups[1].Value);
                relationships.Add(new DiscoveredRelationship(
                    "route",
                    route,
                    "renders_component",
                    "component",
                    $"global::{routeMatch.Groups[2].Value}",
                    $"line={lineNumber};framework=angular"));
            }

            foreach (Match routeMatch in ReactRoutePattern.Matches(line))
            {
                var componentName = routeMatch.Groups[2].Success ? routeMatch.Groups[2].Value : routeMatch.Groups[3].Value;
                relationships.Add(new DiscoveredRelationship(
                    "route",
                    NormalizeClientRoute(routeMatch.Groups[1].Value),
                    "renders_component",
                    "component",
                    $"global::{componentName}",
                    $"line={lineNumber};framework=react"));
            }

            foreach (Match navigateMatch in NavigatePattern.Matches(line))
            {
                var routeExpression = FirstSuccessfulGroup(navigateMatch, 1, 2);
                if (TryResolveExpression(routeExpression, constants, out var route))
                {
                    relationships.Add(new DiscoveredRelationship(
                        "symbol",
                        currentSymbol ?? Path.GetFileName(filePath),
                        "navigates_to",
                        "route",
                        NormalizeClientRoute(route),
                        $"line={lineNumber}"));
                }
            }

            foreach (Match httpMatch in HttpCallPattern.Matches(line))
            {
                var method = FirstSuccessfulGroup(httpMatch, 1, 4).ToUpperInvariant();
                var apiArgument = FirstSuccessfulGroup(httpMatch, 2, 3, 5);
                if (TryResolveExpression(apiArgument, constants, out var route))
                {
                    relationships.Add(new DiscoveredRelationship(
                        "symbol",
                        currentSymbol ?? Path.GetFileName(filePath),
                        "calls_api",
                        "api",
                        $"{method} {route}",
                    $"line={lineNumber}"));
                }
            }

            foreach (Match callMatch in MethodCallPattern.Matches(line))
            {
                var receiver = callMatch.Groups[1].Value;
                var member = callMatch.Groups[2].Value;
                if (ShouldIgnoreMethodCall(receiver, member))
                {
                    continue;
                }

                relationships.Add(new DiscoveredRelationship(
                    "symbol",
                    currentSymbol ?? Path.GetFileName(filePath),
                    "calls_method",
                    "method",
                    $"{receiver}.{member}",
                    $"line={lineNumber}"));
            }
        }

        return Task.FromResult(new CodeAnalysisResult(LanguageId, "parsed", symbols, relationships));
    }

    private static string NormalizeKind(string kind, string filePath, string name)
    {
        if (kind is "const" or "let" or "var" && char.IsUpper(name[0]))
        {
            return Path.GetExtension(filePath).Equals(".tsx", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(filePath).Equals(".jsx", StringComparison.OrdinalIgnoreCase)
                    ? "react-component"
                    : "constant";
        }

        return kind switch
        {
            "type" => "type-alias",
            _ => kind
        };
    }

    private static string FirstSuccessfulGroup(Match match, params int[] groupIndexes)
    {
        foreach (var groupIndex in groupIndexes)
        {
            if (match.Groups[groupIndex].Success)
            {
                return match.Groups[groupIndex].Value;
            }
        }

        return string.Empty;
    }

    private static bool IsControlFlowKeyword(string value) =>
        value is "if" or "for" or "foreach" or "while" or "switch" or "catch";

    private static bool ShouldIgnoreMethodCall(string receiver, string member) =>
        receiver is "http" or "httpClient" or "client" or "axios" or "console" or "Math" or "JSON" or "Object" or "Array"
        || member is "get" or "post" or "put" or "patch" or "delete" or "log" or "map" or "filter" or "reduce" or "subscribe"
            or "navigate" or "navigateByUrl" or "push" or "replace";

    private static string NormalizeClientRoute(string route)
    {
        var normalized = route.Trim().Trim('"', '\'', '`');
        return normalized.StartsWith("/", StringComparison.Ordinal) ? normalized : $"/{normalized}";
    }

    private static bool TryResolveExpression(string expression, IReadOnlyDictionary<string, string> constants, out string value)
    {
        var cleaned = expression.Trim().TrimEnd(';', ',').Trim();
        if (TryUnquote(cleaned, out value, out var quoteChar))
        {
            if (quoteChar == '`')
            {
                value = ResolveTemplateValue(value, constants);
            }

            return true;
        }

        if (constants.TryGetValue(cleaned, out var constantValue))
        {
            value = constantValue;
            return true;
        }

        value = ResolveTemplateExpression(cleaned, constants);
        if (value.Length > 0 && value.StartsWith('/'))
        {
            return true;
        }

        if (cleaned.Contains('+', StringComparison.Ordinal))
        {
            var parts = cleaned.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var resolvedParts = new List<string>();
            foreach (var part in parts)
            {
                var normalizedPart = part.StartsWith("urlFactory(", StringComparison.Ordinal) ? string.Empty : part;
                if (string.IsNullOrEmpty(normalizedPart))
                {
                    continue;
                }

                if (TryResolveExpression(normalizedPart, constants, out var resolvedPart))
                {
                    resolvedParts.Add(resolvedPart);
                }
            }

            value = string.Concat(resolvedParts);
            return value.StartsWith('/');
        }

        value = string.Empty;
        return false;
    }

    private static bool TryUnquote(string value, out string unquoted, out char quoteChar)
    {
        if (value.Length >= 2
            && ((value[0] == '\'' && value[^1] == '\'')
                || (value[0] == '"' && value[^1] == '"')))
        {
            unquoted = value[1..^1];
            quoteChar = value[0];
            return true;
        }

        if (value.Length >= 2 && value[0] == '`' && value[^1] == '`')
        {
            unquoted = value[1..^1];
            quoteChar = '`';
            return true;
        }

        unquoted = string.Empty;
        quoteChar = '\0';
        return false;
    }

    private static string ResolveTemplateExpression(string expression, IReadOnlyDictionary<string, string> constants)
    {
        if (!TryUnquote(expression, out var template, out _))
        {
            template = expression;
        }

        return ResolveTemplateValue(template, constants);
    }

    private static string ResolveTemplateValue(string template, IReadOnlyDictionary<string, string> constants)
    {
        return Regex.Replace(
            template,
            @"\$\{\s*([A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)?)\s*\}",
            match => constants.TryGetValue(match.Groups[1].Value, out var replacement) ? replacement : string.Empty);
    }
}
