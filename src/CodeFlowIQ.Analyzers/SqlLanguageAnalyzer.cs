using CodeFlowIQ.Core.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace CodeFlowIQ.Analyzers;

public sealed class SqlLanguageAnalyzer : ILanguageAnalyzer
{
    public string LanguageId => "sql";

    public bool CanAnalyze(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".sql", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tsql", StringComparison.OrdinalIgnoreCase);
    }

    public Task<CodeAnalysisResult> AnalyzeAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StringReader(content);
            var parser = new TSql160Parser(false);
            var fragment = parser.Parse(reader, out var errors);

            if (errors.Count > 0)
            {
                var message = string.Join("; ", errors.Take(3).Select(x => x.Message));
                return Task.FromResult(new CodeAnalysisResult(LanguageId, "error", [], [], message));
            }

            var visitor = new SqlSymbolVisitor();
            fragment.Accept(visitor);
            return Task.FromResult(new CodeAnalysisResult(LanguageId, "parsed", visitor.Symbols, []));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(new CodeAnalysisResult(LanguageId, "error", [], [], ex.Message));
        }
    }

    private sealed class SqlSymbolVisitor : TSqlFragmentVisitor
    {
        public List<DiscoveredSymbol> Symbols { get; } = [];

        public override void ExplicitVisit(CreateTableStatement node)
        {
            Add(node.SchemaObjectName, "table");
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CreateViewStatement node)
        {
            Add(node.SchemaObjectName, "view");
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CreateProcedureStatement node)
        {
            Add(node.ProcedureReference.Name, "procedure");
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CreateFunctionStatement node)
        {
            Add(node.Name, "function");
            base.ExplicitVisit(node);
        }

        private void Add(SchemaObjectName? name, string kind)
        {
            if (name is null)
            {
                return;
            }

            Symbols.Add(new DiscoveredSymbol(
                name.BaseIdentifier.Value,
                kind,
                name.SchemaIdentifier?.Value,
                name.StartLine,
                name.StartColumn));
        }
    }
}
