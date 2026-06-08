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
            return Task.FromResult(new CodeAnalysisResult(LanguageId, "parsed", visitor.Symbols, visitor.Relationships));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(new CodeAnalysisResult(LanguageId, "error", [], [], ex.Message));
        }
    }

    private sealed class SqlSymbolVisitor : TSqlFragmentVisitor
    {
        public List<DiscoveredSymbol> Symbols { get; } = [];
        public List<DiscoveredRelationship> Relationships { get; } = [];
        private readonly HashSet<string> _relationshipKeys = new(StringComparer.OrdinalIgnoreCase);
        private string? _currentProcedureName;

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
            var previousProcedureName = _currentProcedureName;
            _currentProcedureName = ToSqlIdentifier(node.ProcedureReference.Name);
            Add(node.ProcedureReference.Name, "procedure");
            base.ExplicitVisit(node);
            _currentProcedureName = previousProcedureName;
        }

        public override void ExplicitVisit(CreateFunctionStatement node)
        {
            Add(node.Name, "function");
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(NamedTableReference node)
        {
            AddTableRelationship("reads_table", node.SchemaObject);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(InsertStatement node)
        {
            AddTableRelationship("writes_table", node.InsertSpecification.Target);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(UpdateStatement node)
        {
            AddTableRelationship("writes_table", node.UpdateSpecification.Target);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(DeleteStatement node)
        {
            AddTableRelationship("writes_table", node.DeleteSpecification.Target);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(MergeStatement node)
        {
            AddTableRelationship("writes_table", node.MergeSpecification.Target);
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

        private void AddTableRelationship(string relationshipKind, TableReference? tableReference)
        {
            if (tableReference is NamedTableReference namedTable)
            {
                AddTableRelationship(relationshipKind, namedTable.SchemaObject);
            }
        }

        private void AddTableRelationship(string relationshipKind, SchemaObjectName? tableName)
        {
            if (_currentProcedureName is null || tableName?.BaseIdentifier is null)
            {
                return;
            }

            var tableIdentifier = ToSqlIdentifier(tableName);
            if (tableIdentifier is null)
            {
                return;
            }

            var key = $"{_currentProcedureName}|{relationshipKind}|{tableIdentifier}";
            if (!_relationshipKeys.Add(key))
            {
                return;
            }

            Relationships.Add(new DiscoveredRelationship(
                "procedure",
                ToGlobalIdentifier(_currentProcedureName),
                relationshipKind,
                "database-table",
                ToGlobalIdentifier(tableIdentifier),
                "source=tsql"));
        }

        private static string? ToSqlIdentifier(SchemaObjectName? name)
        {
            if (name?.BaseIdentifier is null)
            {
                return null;
            }

            return name.SchemaIdentifier is null
                ? name.BaseIdentifier.Value
                : $"{name.SchemaIdentifier.Value}.{name.BaseIdentifier.Value}";
        }

        private static string ToGlobalIdentifier(string identifier) => "global::" + identifier;
    }
}
