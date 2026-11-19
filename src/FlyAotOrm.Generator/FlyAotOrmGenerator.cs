using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace FlyAotOrm.Generator;

[Generator]
public sealed class FlyAotOrmGenerator : IIncrementalGenerator
{
    private static readonly SymbolDisplayFormat FullyQualifiedNullableFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly DiagnosticDescriptor MissingSqlParameter = new(
        id: "DORM001",
        title: "SQL parameter is not declared by the repository method",
        messageFormat: "SQL parameter '{0}' is not declared by method '{1}'",
        category: "FlyAotOrm.Sql",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnusedMethodParameter = new(
        id: "DORM002",
        title: "Repository method parameter is not used by SQL",
        messageFormat: "Method parameter '{0}' is not used by SQL in method '{1}'",
        category: "FlyAotOrm.Sql",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor CrudKeyMissing = new(
        id: "DORM003",
        title: "CRUD generation requires exactly one key",
        messageFormat: "Entity '{0}' must declare exactly one [DbKey] property for generated CRUD methods",
        category: "FlyAotOrm.Crud",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ProjectionMaterializerUnavailable = new(
        id: "DORM004",
        title: "Projection materializer cannot be generated",
        messageFormat: "Projection type '{0}' cannot get a generated materializer from Select<TProjection>(); make it public/internal with public settable properties or mark an accessible type with [DbProjection]",
        category: "FlyAotOrm.Projection",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor CrudHasNoUpdateableColumns = new(
        id: "DORM005",
        title: "CRUD update cannot be generated",
        messageFormat: "Entity '{0}' has no updateable columns; generated CRUD will not include UpdateAsync",
        category: "FlyAotOrm.Crud",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor SoftDeleteRequiresBool = new(
        id: "DORM006",
        title: "Soft delete marker requires a bool property",
        messageFormat: "Entity '{0}' property '{1}' is marked [DbSoftDelete] but must be bool",
        category: "FlyAotOrm.Query",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DefaultFilterMethodInvalid = new(
        id: "DORM007",
        title: "Default filter method is invalid",
        messageFormat: "Entity '{0}' default filter '{1}' must reference an accessible static parameterless method returning FlyAotOrm.SqlCondition",
        category: "FlyAotOrm.Query",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DefaultFilterMarkerAmbiguous = new(
        id: "DORM008",
        title: "Default filter marker is ambiguous",
        messageFormat: "Entity '{0}' must not declare more than one {1} property",
        category: "FlyAotOrm.Query",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RepositoryQueryMethodInvalid = new(
        id: "DORM009",
        title: "Repository query entry point is invalid",
        messageFormat: "Repository query method '{0}' must be parameterless and return FlyAotOrm.Query<TEntity>, FlyAotOrm.CountQuery<TEntity>, or FlyAotOrm.AnyQuery<TEntity> where TEntity is marked [DbTable]",
        category: "FlyAotOrm.Repository",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RepositoryQueryMaterializerUnavailable = new(
        id: "DORM010",
        title: "Repository query result materializer cannot be generated",
        messageFormat: "[SqlQuery] method '{0}' return type '{1}' cannot get a generated materializer; use a public/internal DTO with public settable properties, mark the DTO with [DbProjection], or use [SqlScalar] for scalar values",
        category: "FlyAotOrm.Repository",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RepositoryMethodUnsupported = new(
        id: "DORM011",
        title: "Repository method is unsupported",
        messageFormat: "Repository method '{0}' must declare [SqlQuery], [SqlExecute], [SqlScalar], or be a supported parameterless Query DSL entry point",
        category: "FlyAotOrm.Repository",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RepositorySqlAttributeAmbiguous = new(
        id: "DORM012",
        title: "Repository SQL attribute is ambiguous",
        messageFormat: "Repository method '{0}' must declare only one of [SqlQuery], [SqlExecute], or [SqlScalar]",
        category: "FlyAotOrm.Repository",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RepositorySqlReturnTypeInvalid = new(
        id: "DORM013",
        title: "Repository SQL method return type is invalid",
        messageFormat: "Repository SQL method '{0}' return type '{1}' is invalid for [{2}]; [SqlExecute] must return Task<int>, and [SqlQuery] / [SqlScalar] must return Task<T>",
        category: "FlyAotOrm.Repository",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entities = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "FlyAotOrm.DbTableAttribute",
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, _) => EntityModel.TryCreate((INamedTypeSymbol)ctx.TargetSymbol))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        var repositories = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "FlyAotOrm.DbRepositoryAttribute",
                static (node, _) => node is InterfaceDeclarationSyntax,
                static (ctx, _) => RepositoryModel.TryCreate((INamedTypeSymbol)ctx.TargetSymbol))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        var projections = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "FlyAotOrm.DbProjectionAttribute",
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, _) => ProjectionModel.TryCreate((INamedTypeSymbol)ctx.TargetSymbol))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        var queryDslProjectionResults = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsProjectionSelectInvocation(node),
                static (ctx, _) => ProjectionModel.TryCreateForQueryDslSelect((InvocationExpressionSyntax)ctx.Node, ctx.SemanticModel))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);

        context.RegisterSourceOutput(entities, static (spc, entity) =>
        {
            foreach (var diagnostic in entity.Diagnostics)
            {
                spc.ReportDiagnostic(diagnostic);
            }

            spc.AddSource($"{entity.MetadataTypeName}.g.cs", SourceText.From(EntityEmitter.Emit(entity), Encoding.UTF8));
        });

        context.RegisterSourceOutput(entities.Collect(), static (spc, collectedEntities) =>
        {
            foreach (var group in collectedEntities
                         .GroupBy(static entity => entity.Namespace)
                         .Where(static group => group.Any()))
            {
                var entitiesInNamespace = group
                    .OrderBy(static entity => entity.TypeName, StringComparer.Ordinal)
                    .ToArray();
                var sourceName = string.IsNullOrEmpty(group.Key)
                    ? "FlyDbContext.g.cs"
                    : $"{SanitizeSourceName(group.Key)}_FlyDbContext.g.cs";

                spc.AddSource(sourceName, SourceText.From(DbContextEmitter.Emit(group.Key, entitiesInNamespace), Encoding.UTF8));
            }
        });

        context.RegisterSourceOutput(repositories, static (spc, repository) =>
        {
            foreach (var diagnostic in repository.Diagnostics)
            {
                spc.ReportDiagnostic(diagnostic);
            }

            spc.AddSource($"{repository.ImplementationName}.g.cs", SourceText.From(RepositoryEmitter.Emit(repository), Encoding.UTF8));

            foreach (var projection in repository.QueryProjections)
            {
                spc.AddSource($"{projection.MaterializerTypeName}.g.cs", SourceText.From(ProjectionEmitter.Emit(projection), Encoding.UTF8));
            }
        });

        context.RegisterSourceOutput(projections, static (spc, projection) =>
        {
            spc.AddSource($"{projection.MaterializerTypeName}.g.cs", SourceText.From(ProjectionEmitter.Emit(projection), Encoding.UTF8));
        });

        context.RegisterSourceOutput(queryDslProjectionResults.Collect(), static (spc, projectionResults) =>
        {
            foreach (var diagnostic in projectionResults
                         .Select(static result => result.Diagnostic)
                         .Where(static diagnostic => diagnostic is not null)
                         .Select(static diagnostic => diagnostic!))
            {
                spc.ReportDiagnostic(diagnostic);
            }

            foreach (var projection in projectionResults
                         .Select(static result => result.Projection)
                         .Where(static projection => projection is not null)
                         .Select(static projection => projection!)
                         .GroupBy(static model => model.FullTypeName)
                         .Select(static group => group.First()))
            {
                spc.AddSource($"{projection.MaterializerTypeName}.g.cs", SourceText.From(ProjectionEmitter.Emit(projection), Encoding.UTF8));
            }
        });
    }

    private static bool IsProjectionSelectInvocation(SyntaxNode node)
        => node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name: GenericNameSyntax
                {
                    Identifier.Text: "Select",
                    TypeArgumentList.Arguments.Count: 1
                }
            }
        };

    private sealed record EntityModel(
        string Namespace,
        string Accessibility,
        string TypeName,
        string FullTypeName,
        string MetadataTypeName,
        string TableName,
        EquatableArray<ColumnModel> Columns,
        EquatableArray<string> DefaultFilterExpressions,
        EquatableArray<Diagnostic> Diagnostics)
    {
        public static EntityModel? TryCreate(INamedTypeSymbol symbol)
        {
            var tableAttribute = symbol.GetAttributes().FirstOrDefault(static attr =>
                attr.AttributeClass?.ToDisplayString() == "FlyAotOrm.DbTableAttribute");

            if (tableAttribute is null || tableAttribute.ConstructorArguments.Length == 0)
            {
                return null;
            }

            var tableName = tableAttribute.ConstructorArguments[0].Value as string;
            if (string.IsNullOrWhiteSpace(tableName))
            {
                return null;
            }

            var properties = symbol.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(static property => !property.IsStatic && property.GetMethod is not null)
                .ToImmutableArray();

            var columns = properties
                .Select(static property => new ColumnModel(
                    property.Name,
                    GetColumnName(property),
                    property.Type.ToDisplayString(FullyQualifiedNullableFormat),
                    IsKey(property),
                    IsIdentity(property),
                    IsNullable(property),
                    IsSoftDelete(property),
                    IsTenant(property)))
                .ToImmutableArray();

            var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
            if (columns.Count(static column => column.IsKey) != 1)
            {
                diagnostics.Add(Diagnostic.Create(CrudKeyMissing, symbol.Locations.FirstOrDefault(), symbol.Name));
            }
            else if (!columns.Any(static column => !column.IsKey && !column.IsIdentity))
            {
                diagnostics.Add(Diagnostic.Create(CrudHasNoUpdateableColumns, symbol.Locations.FirstOrDefault(), symbol.Name));
            }

            foreach (var property in properties.Where(static property => IsSoftDelete(property) && property.Type.SpecialType != SpecialType.System_Boolean))
            {
                diagnostics.Add(Diagnostic.Create(SoftDeleteRequiresBool, property.Locations.FirstOrDefault(), symbol.Name, property.Name));
            }

            if (columns.Count(static column => column.IsSoftDelete) > 1)
            {
                diagnostics.Add(Diagnostic.Create(DefaultFilterMarkerAmbiguous, symbol.Locations.FirstOrDefault(), symbol.Name, "[DbSoftDelete]"));
            }

            if (columns.Count(static column => column.IsTenant) > 1)
            {
                diagnostics.Add(Diagnostic.Create(DefaultFilterMarkerAmbiguous, symbol.Locations.FirstOrDefault(), symbol.Name, "[DbTenant]"));
            }

            var defaultFilterExpressions = GetDefaultFilterExpressions(symbol, diagnostics);

            return new EntityModel(
                Namespace: symbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : symbol.ContainingNamespace.ToDisplayString(),
                Accessibility: GetAccessibility(symbol.DeclaredAccessibility),
                TypeName: symbol.Name,
                FullTypeName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                MetadataTypeName: $"{symbol.Name}Metadata",
                TableName: tableName!,
                Columns: new EquatableArray<ColumnModel>(columns),
                DefaultFilterExpressions: new EquatableArray<string>(defaultFilterExpressions),
                Diagnostics: new EquatableArray<Diagnostic>(diagnostics.ToImmutable()));
        }

        private static string GetColumnName(IPropertySymbol property)
        {
            var columnAttribute = property.GetAttributes().FirstOrDefault(static attr =>
                attr.AttributeClass?.ToDisplayString() == "FlyAotOrm.DbColumnAttribute");

            return columnAttribute?.ConstructorArguments.FirstOrDefault().Value as string ?? property.Name;
        }

        private static bool IsKey(IPropertySymbol property)
            => property.GetAttributes().Any(static attr => attr.AttributeClass?.ToDisplayString() == "FlyAotOrm.DbKeyAttribute");

        private static bool IsIdentity(IPropertySymbol property)
        {
            var keyAttribute = property.GetAttributes().FirstOrDefault(static attr =>
                attr.AttributeClass?.ToDisplayString() == "FlyAotOrm.DbKeyAttribute");

            if (keyAttribute is null)
            {
                return false;
            }

            foreach (var namedArgument in keyAttribute.NamedArguments)
            {
                if (namedArgument.Key == "IsIdentity" && namedArgument.Value.Value is bool value)
                {
                    return value;
                }
            }

            return true;
        }

        private static bool IsNullable(IPropertySymbol property)
            => property.NullableAnnotation == NullableAnnotation.Annotated ||
               property.Type is INamedTypeSymbol
               {
                   OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
               };

        private static bool IsSoftDelete(IPropertySymbol property)
            => property.GetAttributes().Any(static attr => attr.AttributeClass?.ToDisplayString() == "FlyAotOrm.DbSoftDeleteAttribute");

        private static bool IsTenant(IPropertySymbol property)
            => property.GetAttributes().Any(static attr => attr.AttributeClass?.ToDisplayString() == "FlyAotOrm.DbTenantAttribute");

        private static ImmutableArray<string> GetDefaultFilterExpressions(INamedTypeSymbol symbol, ImmutableArray<Diagnostic>.Builder diagnostics)
        {
            var expressions = ImmutableArray.CreateBuilder<string>();
            foreach (var attribute in symbol.GetAttributes().Where(static attr => attr.AttributeClass?.ToDisplayString() == "FlyAotOrm.DbDefaultFilterAttribute"))
            {
                if (TryGetDefaultFilterExpression(symbol, attribute, diagnostics) is { } expression)
                {
                    expressions.Add(expression);
                }
            }

            return expressions.ToImmutable();
        }

        private static string? TryGetDefaultFilterExpression(INamedTypeSymbol entityType, AttributeData attribute, ImmutableArray<Diagnostic>.Builder diagnostics)
        {
            var providerType = attribute.ConstructorArguments.Length > 0
                ? attribute.ConstructorArguments[0].Value as INamedTypeSymbol
                : null;
            var methodName = attribute.ConstructorArguments.Length > 1
                ? attribute.ConstructorArguments[1].Value as string
                : null;

            if (providerType is null || string.IsNullOrWhiteSpace(methodName))
            {
                diagnostics.Add(Diagnostic.Create(DefaultFilterMethodInvalid, entityType.Locations.FirstOrDefault(), entityType.Name, methodName ?? string.Empty));
                return null;
            }

            var methods = providerType.GetMembers(methodName!)
                .OfType<IMethodSymbol>()
                .Where(static method => method.MethodKind == MethodKind.Ordinary)
                .ToArray();
            var method = methods.FirstOrDefault(static method =>
                method.IsStatic &&
                method.Parameters.Length == 0 &&
                method.ReturnType.ToDisplayString() == "FlyAotOrm.SqlCondition" &&
                method.DeclaredAccessibility is Microsoft.CodeAnalysis.Accessibility.Public or Microsoft.CodeAnalysis.Accessibility.Internal);

            if (method is null)
            {
                diagnostics.Add(Diagnostic.Create(DefaultFilterMethodInvalid, entityType.Locations.FirstOrDefault(), entityType.Name, $"{providerType.Name}.{methodName}"));
                return null;
            }

            return $"{providerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{method.Name}()";
        }
    }

    private sealed record ColumnModel(string PropertyName, string ColumnName, string TypeName, bool IsKey, bool IsIdentity, bool IsNullable, bool IsSoftDelete, bool IsTenant);

    private sealed record RepositoryModel(
        string Namespace,
        string Accessibility,
        string InterfaceName,
        string ImplementationName,
        EquatableArray<RepositoryMethodModel> Methods,
        EquatableArray<ProjectionModel> QueryProjections,
        EquatableArray<Diagnostic> Diagnostics)
    {
        public static RepositoryModel? TryCreate(INamedTypeSymbol symbol)
        {
            var methods = ImmutableArray.CreateBuilder<RepositoryMethodModel>();
            var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

            foreach (var member in symbol.GetMembers().OfType<IMethodSymbol>())
            {
                if (member.MethodKind != MethodKind.Ordinary)
                {
                    continue;
                }

                var sqlAttribute = GetSqlAttribute(member, diagnostics);
                if (sqlAttribute is null)
                {
                    if (RepositoryMethodModel.TryCreateQueryDsl(member) is { } queryDslMethod)
                    {
                        methods.Add(queryDslMethod);
                    }
                    else if (RepositoryMethodModel.IsQueryDslLikeReturn(member.ReturnType))
                    {
                        diagnostics.Add(Diagnostic.Create(RepositoryQueryMethodInvalid, member.Locations.FirstOrDefault(), member.Name));
                    }
                    else
                    {
                        diagnostics.Add(Diagnostic.Create(RepositoryMethodUnsupported, member.Locations.FirstOrDefault(), member.Name));
                    }

                    continue;
                }

                ValidateSqlParameters(member, sqlAttribute.Sql, diagnostics);
                var returnTypeDiagnostic = RepositoryMethodModel.TryCreateSqlReturnTypeDiagnostic(member, sqlAttribute);
                if (returnTypeDiagnostic is not null)
                {
                    diagnostics.Add(returnTypeDiagnostic);
                }
                else if (RepositoryMethodModel.TryCreateQueryMaterializerDiagnostic(member, sqlAttribute) is { } queryMaterializerDiagnostic)
                {
                    diagnostics.Add(queryMaterializerDiagnostic);
                }

                methods.Add(RepositoryMethodModel.Create(member, sqlAttribute));
            }

            var implementationName = symbol.Name.StartsWith("I", StringComparison.Ordinal) && symbol.Name.Length > 1
                ? symbol.Name.Substring(1)
                : $"{symbol.Name}Impl";

            var queryProjections = methods
                .Where(static method => method.Kind == SqlMethodKind.Query)
                .Select(static method => method.QueryProjection)
                .Where(static projection => projection is not null)
                .Select(static projection => projection!)
                .GroupBy(static projection => projection.FullTypeName)
                .Select(static group => group.First())
                .ToImmutableArray();

            return new RepositoryModel(
                Namespace: symbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : symbol.ContainingNamespace.ToDisplayString(),
                Accessibility: GetAccessibility(symbol.DeclaredAccessibility),
                InterfaceName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ImplementationName: implementationName,
                Methods: new EquatableArray<RepositoryMethodModel>(methods.ToImmutable()),
                QueryProjections: new EquatableArray<ProjectionModel>(queryProjections),
                Diagnostics: new EquatableArray<Diagnostic>(diagnostics.ToImmutable()));
        }

        private static SqlAttributeData? GetSqlAttribute(IMethodSymbol method, ImmutableArray<Diagnostic>.Builder diagnostics)
        {
            SqlAttributeData? sqlAttribute = null;
            var sqlAttributeCount = 0;
            foreach (var attribute in method.GetAttributes())
            {
                var attributeName = attribute.AttributeClass?.ToDisplayString();
                var kind = attributeName switch
                {
                    "FlyAotOrm.SqlQueryAttribute" => SqlMethodKind.Query,
                    "FlyAotOrm.SqlExecuteAttribute" => SqlMethodKind.Execute,
                    "FlyAotOrm.SqlScalarAttribute" => SqlMethodKind.Scalar,
                    _ => (SqlMethodKind?)null
                };

                if (kind is not null && attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string sql)
                {
                    sqlAttributeCount++;
                    sqlAttribute ??= new SqlAttributeData(kind.Value, sql);
                }
            }

            if (sqlAttributeCount > 1)
            {
                diagnostics.Add(Diagnostic.Create(RepositorySqlAttributeAmbiguous, method.Locations.FirstOrDefault(), method.Name));
            }

            return sqlAttribute;
        }

        private static void ValidateSqlParameters(
            IMethodSymbol method,
            string sql,
            ImmutableArray<Diagnostic>.Builder diagnostics)
        {
            var sqlParameters = Regex.Matches(sql, @"(?<!@)@([A-Za-z_][A-Za-z0-9_]*)")
                .Cast<Match>()
                .Select(static match => match.Groups[1].Value)
                .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

            var methodParameters = method.Parameters
                .Where(static parameter => !IsCancellationToken(parameter.Type))
                .Select(static parameter => parameter.Name)
                .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var sqlParameter in sqlParameters)
            {
                if (!methodParameters.Contains(sqlParameter))
                {
                    diagnostics.Add(Diagnostic.Create(MissingSqlParameter, method.Locations.FirstOrDefault(), sqlParameter, method.Name));
                }
            }

            foreach (var methodParameter in methodParameters)
            {
                if (!sqlParameters.Contains(methodParameter))
                {
                    diagnostics.Add(Diagnostic.Create(UnusedMethodParameter, method.Locations.FirstOrDefault(), methodParameter, method.Name));
                }
            }
        }
    }

    private sealed record SqlAttributeData(SqlMethodKind Kind, string Sql);

    private enum SqlMethodKind
    {
        Query,
        Execute,
        Scalar,
        QueryDsl
    }

    private sealed record RepositoryMethodModel(
        string Name,
        string ReturnType,
        string ResultType,
        SqlMethodKind Kind,
        string Sql,
        EquatableArray<ParameterModel> Parameters,
        bool IsCollectionResult,
        string? QueryFactoryMethod,
        string? QueryTableReference,
        string? QueryDefaultFilterExpression,
        ProjectionModel? QueryProjection)
    {
        public static RepositoryMethodModel? TryCreateQueryDsl(IMethodSymbol method)
        {
            if (method.Parameters.Length != 0 ||
                method.ReturnType is not INamedTypeSymbol namedReturnType ||
                namedReturnType.Name is not ("Query" or "CountQuery" or "AnyQuery") ||
                namedReturnType.ContainingNamespace.ToDisplayString() != "FlyAotOrm" ||
                namedReturnType.TypeArguments.Length != 1 ||
                namedReturnType.TypeArguments[0] is not INamedTypeSymbol entityType ||
                !IsDbTableEntity(entityType))
            {
                return null;
            }

            var entityNamespace = entityType.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : entityType.ContainingNamespace.ToDisplayString();
            var tableReference = string.IsNullOrEmpty(entityNamespace)
                ? $"Tables.{entityType.Name}"
                : $"global::{entityNamespace}.Tables.{entityType.Name}";
            var defaultFilterExpression = GetQueryDefaultFilterExpression(entityType, tableReference);
            var queryFactoryMethod = namedReturnType.Name switch
            {
                "CountQuery" => "SelectCount",
                "AnyQuery" => "SelectAny",
                _ => "Select"
            };

            return new RepositoryMethodModel(
                Name: method.Name,
                ReturnType: method.ReturnType.ToDisplayString(FullyQualifiedNullableFormat),
                ResultType: entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Kind: SqlMethodKind.QueryDsl,
                Sql: string.Empty,
                Parameters: new EquatableArray<ParameterModel>(ImmutableArray<ParameterModel>.Empty),
                IsCollectionResult: false,
                QueryFactoryMethod: queryFactoryMethod,
                QueryTableReference: tableReference,
                QueryDefaultFilterExpression: defaultFilterExpression,
                QueryProjection: null);
        }

        public static bool IsQueryDslLikeReturn(ITypeSymbol returnType)
            => returnType is INamedTypeSymbol namedReturnType &&
               namedReturnType.Name is "Query" or "CountQuery" or "AnyQuery" &&
               namedReturnType.ContainingNamespace.ToDisplayString() == "FlyAotOrm";

        private static bool IsDbTableEntity(INamedTypeSymbol entityType)
            => entityType.GetAttributes().Any(static attr => attr.AttributeClass?.ToDisplayString() == "FlyAotOrm.DbTableAttribute");

        public static RepositoryMethodModel Create(IMethodSymbol method, SqlAttributeData sqlAttribute)
        {
            var returnType = method.ReturnType.ToDisplayString(FullyQualifiedNullableFormat);
            var resultType = GetTaskResultType(method.ReturnType);
            var projection = sqlAttribute.Kind == SqlMethodKind.Query && resultType.Symbol is not null
                ? ProjectionModel.TryCreateForRepositoryResult(resultType.Symbol, method.ContainingType.Name, method.Name)
                : null;

            return new RepositoryMethodModel(
                Name: method.Name,
                ReturnType: returnType,
                ResultType: resultType.TypeName,
                Kind: sqlAttribute.Kind,
                Sql: sqlAttribute.Sql,
                Parameters: new EquatableArray<ParameterModel>(method.Parameters
                    .Select(static parameter => new ParameterModel(
                        parameter.Name,
                        parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        IsCancellationToken(parameter.Type)))
                    .ToImmutableArray()),
                IsCollectionResult: resultType.IsCollection,
                QueryFactoryMethod: null,
                QueryTableReference: null,
                QueryDefaultFilterExpression: null,
                QueryProjection: projection);
        }

        public static Diagnostic? TryCreateQueryMaterializerDiagnostic(IMethodSymbol method, SqlAttributeData sqlAttribute)
        {
            if (sqlAttribute.Kind != SqlMethodKind.Query)
            {
                return null;
            }

            var resultType = GetTaskResultType(method.ReturnType);
            if (resultType.Symbol is not INamedTypeSymbol namedSymbol)
            {
                return Diagnostic.Create(
                    RepositoryQueryMaterializerUnavailable,
                    method.Locations.FirstOrDefault(),
                    method.Name,
                    method.ReturnType.ToDisplayString(FullyQualifiedNullableFormat));
            }

            if (ProjectionModel.TryCreateForRepositoryResult(namedSymbol, method.ContainingType.Name, method.Name) is not null)
            {
                return null;
            }

            return Diagnostic.Create(
                RepositoryQueryMaterializerUnavailable,
                method.Locations.FirstOrDefault(),
                method.Name,
                resultType.TypeName);
        }

        public static Diagnostic? TryCreateSqlReturnTypeDiagnostic(IMethodSymbol method, SqlAttributeData sqlAttribute)
        {
            if (!TryGetTaskResult(method.ReturnType, out var taskResultType))
            {
                return CreateSqlReturnTypeDiagnostic(method, sqlAttribute);
            }

            if (sqlAttribute.Kind == SqlMethodKind.Execute && taskResultType.SpecialType != SpecialType.System_Int32)
            {
                return CreateSqlReturnTypeDiagnostic(method, sqlAttribute);
            }

            return null;
        }

        private static Diagnostic CreateSqlReturnTypeDiagnostic(IMethodSymbol method, SqlAttributeData sqlAttribute)
            => Diagnostic.Create(
                RepositorySqlReturnTypeInvalid,
                method.Locations.FirstOrDefault(),
                method.Name,
                method.ReturnType.ToDisplayString(FullyQualifiedNullableFormat),
                sqlAttribute.Kind switch
                {
                    SqlMethodKind.Query => "SqlQuery",
                    SqlMethodKind.Execute => "SqlExecute",
                    SqlMethodKind.Scalar => "SqlScalar",
                    _ => "Sql"
                });

        private static string? GetQueryDefaultFilterExpression(INamedTypeSymbol entityType, string tableReference)
        {
            var filters = ImmutableArray.CreateBuilder<string>();
            var softDeleteProperty = entityType.GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(static property => property.GetAttributes().Any(static attr => attr.AttributeClass?.ToDisplayString() == "FlyAotOrm.DbSoftDeleteAttribute"));

            if (softDeleteProperty is not null)
            {
                filters.Add($"{tableReference}.{softDeleteProperty.Name}.Eq(false)");
            }

            var tenantProperty = entityType.GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(static property => property.GetAttributes().Any(static attr => attr.AttributeClass?.ToDisplayString() == "FlyAotOrm.DbTenantAttribute"));
            if (tenantProperty is not null)
            {
                filters.Add($"{tableReference}.{tenantProperty.Name}.Eq(global::FlyAotOrm.FlyTenant.Get<{tenantProperty.Type.ToDisplayString(FullyQualifiedNullableFormat)}>())");
            }

            foreach (var filterExpression in GetAttributeDefaultFilterExpressions(entityType))
            {
                filters.Add(filterExpression);
            }

            return BuildDefaultFilterChain(filters);
        }

        private static IEnumerable<string> GetAttributeDefaultFilterExpressions(INamedTypeSymbol entityType)
        {
            foreach (var attribute in entityType.GetAttributes().Where(static attr => attr.AttributeClass?.ToDisplayString() == "FlyAotOrm.DbDefaultFilterAttribute"))
            {
                var providerType = attribute.ConstructorArguments.Length > 0
                    ? attribute.ConstructorArguments[0].Value as INamedTypeSymbol
                    : null;
                var methodName = attribute.ConstructorArguments.Length > 1
                    ? attribute.ConstructorArguments[1].Value as string
                    : null;

                if (providerType is null || string.IsNullOrWhiteSpace(methodName))
                {
                    continue;
                }

                var method = providerType.GetMembers(methodName!)
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(static method =>
                        method.MethodKind == MethodKind.Ordinary &&
                        method.IsStatic &&
                        method.Parameters.Length == 0 &&
                        method.ReturnType.ToDisplayString() == "FlyAotOrm.SqlCondition" &&
                        method.DeclaredAccessibility is Microsoft.CodeAnalysis.Accessibility.Public or Microsoft.CodeAnalysis.Accessibility.Internal);

                if (method is not null)
                {
                    yield return $"{providerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{method.Name}()";
                }
            }
        }

        private static string? BuildDefaultFilterChain(IEnumerable<string> filters)
        {
            using var enumerator = filters.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                return null;
            }

            var expression = $".Where({enumerator.Current})";
            while (enumerator.MoveNext())
            {
                expression = $"{expression}.And({enumerator.Current})";
            }

            return expression;
        }

        private static (string TypeName, ITypeSymbol? Symbol, bool IsCollection) GetTaskResultType(ITypeSymbol returnType)
        {
            if (TryGetTaskResult(returnType, out var taskType))
            {
                if (taskType is INamedTypeSymbol namedTaskType &&
                    (namedTaskType.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.IReadOnlyList<T>" ||
                     namedTaskType.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.IReadOnlyCollection<T>" ||
                     namedTaskType.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>") &&
                    namedTaskType.TypeArguments.Length == 1)
                {
                    var itemType = namedTaskType.TypeArguments[0];
                    return (itemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), itemType, true);
                }

                return (taskType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), taskType, false);
            }

            return ("int", null, false);
        }

        private static bool TryGetTaskResult(ITypeSymbol returnType, out ITypeSymbol resultType)
        {
            if (returnType is INamedTypeSymbol namedReturnType &&
                namedReturnType.Name == "Task" &&
                namedReturnType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" &&
                namedReturnType.TypeArguments.Length == 1)
            {
                resultType = namedReturnType.TypeArguments[0];
                return true;
            }

            resultType = returnType;
            return false;
        }
    }

    private sealed record ParameterModel(string Name, string TypeName, bool IsCancellationToken);

    private sealed record ProjectionModel(
        string Namespace,
        string Accessibility,
        string TypeName,
        string FullTypeName,
        string MaterializerTypeName,
        EquatableArray<ProjectionPropertyModel> Properties)
    {
        public static ProjectionModel? TryCreate(INamedTypeSymbol symbol)
        {
            var attribute = symbol.GetAttributes().FirstOrDefault(static attr =>
                attr.AttributeClass?.ToDisplayString() == "FlyAotOrm.DbProjectionAttribute");

            if (attribute is null)
            {
                return null;
            }

            var properties = symbol.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(static property =>
                    !property.IsStatic &&
                    property.GetMethod is not null &&
                    property.SetMethod is not null)
                .Select(static property => new ProjectionPropertyModel(
                    property.Name,
                    property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                .ToImmutableArray();

            return new ProjectionModel(
                Namespace: symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString(),
                Accessibility: GetAccessibility(symbol.DeclaredAccessibility),
                TypeName: symbol.Name,
                FullTypeName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                MaterializerTypeName: symbol.Name + "ProjectionMaterializer",
                Properties: new EquatableArray<ProjectionPropertyModel>(properties));
        }

        public static ProjectionModel? TryCreateForRepositoryResult(ITypeSymbol symbol, string repositoryName, string methodName)
        {
            if (symbol is not INamedTypeSymbol namedSymbol)
            {
                return null;
            }

            if (namedSymbol.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            {
                return null;
            }

            if (namedSymbol.SpecialType != SpecialType.None || namedSymbol.ToDisplayString() == "string")
            {
                return null;
            }

            var properties = namedSymbol.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(static property =>
                    !property.IsStatic &&
                    property.GetMethod is not null &&
                    property.SetMethod is not null &&
                    property.SetMethod.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public)
                .Select(static property => new ProjectionPropertyModel(
                    property.Name,
                    property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                .ToImmutableArray();

            if (properties.Length == 0)
            {
                return null;
            }

            return new ProjectionModel(
                Namespace: namedSymbol.ContainingNamespace.IsGlobalNamespace ? "" : namedSymbol.ContainingNamespace.ToDisplayString(),
                Accessibility: "internal",
                TypeName: namedSymbol.Name,
                FullTypeName: namedSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                MaterializerTypeName: repositoryName + "_" + methodName + "_" + namedSymbol.Name + "Materializer",
                Properties: new EquatableArray<ProjectionPropertyModel>(properties));
        }

        public static QueryDslProjectionResult? TryCreateForQueryDslSelect(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax
                {
                    Name: GenericNameSyntax
                    {
                        Identifier.Text: "Select",
                        TypeArgumentList.Arguments.Count: 1
                    } genericName
                })
            {
                return null;
            }

            if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol
                {
                    Name: "Select",
                    ContainingType: { } containingType
                } ||
                containingType.ToDisplayString() != "FlyAotOrm.FlyDb")
            {
                return null;
            }

            var typeInfo = semanticModel.GetTypeInfo(genericName.TypeArgumentList.Arguments[0]);
            if (typeInfo.Type is not INamedTypeSymbol namedSymbol)
            {
                return null;
            }

            if (namedSymbol.GetAttributes().Any(static attr =>
                    attr.AttributeClass?.ToDisplayString() == "FlyAotOrm.DbProjectionAttribute"))
            {
                return null;
            }

            if (namedSymbol.TypeKind is not (TypeKind.Class or TypeKind.Struct) ||
                namedSymbol.SpecialType != SpecialType.None ||
                namedSymbol.ToDisplayString() == "string")
            {
                return null;
            }

            if (!CanGenerateMaterializerForQueryDsl(namedSymbol))
            {
                return new QueryDslProjectionResult(
                    Projection: null,
                    Diagnostic: Diagnostic.Create(
                        ProjectionMaterializerUnavailable,
                        genericName.TypeArgumentList.Arguments[0].GetLocation(),
                        namedSymbol.ToDisplayString()));
            }

            var projection = CreateProjection(namedSymbol, namedSymbol.Name + "ProjectionMaterializer", GetAccessibility(namedSymbol.DeclaredAccessibility));
            if (projection is null)
            {
                return new QueryDslProjectionResult(
                    Projection: null,
                    Diagnostic: Diagnostic.Create(
                        ProjectionMaterializerUnavailable,
                        genericName.TypeArgumentList.Arguments[0].GetLocation(),
                        namedSymbol.ToDisplayString()));
            }

            return new QueryDslProjectionResult(projection, null);
        }

        private static bool CanGenerateMaterializerForQueryDsl(INamedTypeSymbol symbol)
        {
            if (symbol.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            {
                return false;
            }

            if (symbol.SpecialType != SpecialType.None || symbol.ToDisplayString() == "string")
            {
                return false;
            }

            if (symbol.DeclaredAccessibility is not (Microsoft.CodeAnalysis.Accessibility.Public or Microsoft.CodeAnalysis.Accessibility.Internal))
            {
                return false;
            }

            for (var containingType = symbol.ContainingType; containingType is not null; containingType = containingType.ContainingType)
            {
                if (containingType.DeclaredAccessibility is not (Microsoft.CodeAnalysis.Accessibility.Public or Microsoft.CodeAnalysis.Accessibility.Internal))
                {
                    return false;
                }
            }

            return true;
        }

        private static ProjectionModel? CreateProjection(INamedTypeSymbol symbol, string materializerTypeName, string accessibility)
        {
            var properties = symbol.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(static property =>
                    !property.IsStatic &&
                    property.GetMethod is not null &&
                    property.SetMethod is not null &&
                    property.SetMethod.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public)
                .Select(static property => new ProjectionPropertyModel(
                    property.Name,
                    property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                .ToImmutableArray();

            if (properties.Length == 0)
            {
                return null;
            }

            return new ProjectionModel(
                Namespace: symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString(),
                Accessibility: accessibility,
                TypeName: symbol.Name,
                FullTypeName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                MaterializerTypeName: materializerTypeName,
                Properties: new EquatableArray<ProjectionPropertyModel>(properties));
        }
    }

    private sealed record QueryDslProjectionResult(ProjectionModel? Projection, Diagnostic? Diagnostic);

    private sealed record ProjectionPropertyModel(string PropertyName, string TypeName);

    private static bool IsCancellationToken(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Threading.CancellationToken";

    private static string GetAccessibility(Accessibility accessibility)
        => accessibility == Accessibility.Public ? "public" : "internal";

    private static class EntityEmitter
    {
        public static string Emit(EntityModel entity)
        {
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("#nullable enable");
            builder.AppendLine();
            AppendNamespaceStart(builder, entity.Namespace);
            builder.AppendLine($"{entity.Accessibility} static partial class {entity.MetadataTypeName}");
            builder.AppendLine("{");
            builder.AppendLine($"    public const string TableName = {Literal(entity.TableName)};");
            builder.AppendLine();
            builder.AppendLine("    public static global::System.ReadOnlySpan<string> ColumnNames => new string[]");
            builder.AppendLine("    {");
            foreach (var column in entity.Columns)
            {
                builder.AppendLine($"        {Literal(column.ColumnName)},");
            }
            builder.AppendLine("    };");
            builder.AppendLine();
            builder.AppendLine("    public static global::System.ReadOnlySpan<string> InsertColumns => new string[]");
            builder.AppendLine("    {");
            foreach (var column in entity.Columns.Where(static column => !column.IsIdentity))
            {
                builder.AppendLine($"        {Literal(column.ColumnName)},");
            }
            builder.AppendLine("    };");
            builder.AppendLine();
            builder.AppendLine("    public static global::System.ReadOnlySpan<string> UpdateColumns => new string[]");
            builder.AppendLine("    {");
            foreach (var column in entity.Columns.Where(static column => !column.IsKey && !column.IsIdentity))
            {
                builder.AppendLine($"        {Literal(column.ColumnName)},");
            }
            builder.AppendLine("    };");
            builder.AppendLine();
            builder.AppendLine("    public static global::System.ReadOnlySpan<string> NullableColumnNames => new string[]");
            builder.AppendLine("    {");
            foreach (var column in entity.Columns.Where(static column => column.IsNullable))
            {
                builder.AppendLine($"        {Literal(column.ColumnName)},");
            }
            builder.AppendLine("    };");
            builder.AppendLine();
            foreach (var column in entity.Columns)
            {
                builder.AppendLine($"    public const string {column.PropertyName}Column = {Literal(column.ColumnName)};");
                builder.AppendLine($"    public const bool {column.PropertyName}IsNullable = {ToCSharpBool(column.IsNullable)};");
            }
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine($"{entity.Accessibility} sealed class {entity.TypeName}Table : global::FlyAotOrm.DbTable<{entity.FullTypeName}>");
            builder.AppendLine("{");
            builder.AppendLine($"    public {entity.TypeName}Table() : base({Literal(entity.TableName)}, {Literal(entity.TypeName)}, Materialize)");
            builder.AppendLine("    {");
            foreach (var column in entity.Columns)
            {
                builder.AppendLine($"        {column.PropertyName} = new global::FlyAotOrm.DbColumn<{column.TypeName}>(this, {Literal(column.ColumnName)}, isNullable: {ToCSharpBool(column.IsNullable)});");
            }
            builder.AppendLine("    }");
            builder.AppendLine();
            foreach (var column in entity.Columns)
            {
                builder.AppendLine($"    public global::FlyAotOrm.DbColumn<{column.TypeName}> {column.PropertyName} {{ get; }}");
            }
            builder.AppendLine();
            builder.AppendLine($"    internal static {entity.FullTypeName} Materialize(global::System.Data.Common.DbDataReader reader)");
            builder.AppendLine("    {");
            builder.AppendLine($"        return new {entity.FullTypeName}");
            builder.AppendLine("        {");
            for (var i = 0; i < entity.Columns.Count; i++)
            {
                var column = entity.Columns.ElementAt(i);
                builder.AppendLine($"            {column.PropertyName} = Read<{column.TypeName}>(reader, {i}),");
            }
            builder.AppendLine("        };");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private static T Read<T>(global::System.Data.Common.DbDataReader reader, int ordinal)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (reader.IsDBNull(ordinal))");
            builder.AppendLine("        {");
            builder.AppendLine("            return default!;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        var value = reader.GetValue(ordinal);");
            builder.AppendLine("        return global::FlyAotOrm.FlyDbValueConverter.Read<T>(value);");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine($"{entity.Accessibility} static partial class Tables");
            builder.AppendLine("{");
            builder.AppendLine($"    public static readonly {entity.TypeName}Table {entity.TypeName} = new();");
            builder.AppendLine("}");
            EmitCrud(builder, entity);
            AppendNamespaceEnd(builder, entity.Namespace);
            return builder.ToString();
        }

        private static void EmitCrud(StringBuilder builder, EntityModel entity)
        {
            var key = entity.Columns.SingleOrDefault(static column => column.IsKey);
            if (key is null)
            {
                return;
            }

            var insertColumns = entity.Columns.Where(static column => !column.IsIdentity).ToArray();
            var updateColumns = entity.Columns.Where(static column => !column.IsKey && !column.IsIdentity).ToArray();
            var sqliteSelectableColumns = string.Join(", ", entity.Columns.Select(static column => $"{QuoteSqlite(column.ColumnName)} as {QuoteSqlite(column.PropertyName)}"));
            var sqlServerSelectableColumns = string.Join(", ", entity.Columns.Select(static column => $"{QuoteSqlServer(column.ColumnName)} as {QuoteSqlServer(column.PropertyName)}"));
            var mySqlSelectableColumns = string.Join(", ", entity.Columns.Select(static column => $"{QuoteMySql(column.ColumnName)} as {QuoteMySql(column.PropertyName)}"));
            var novaDbSelectableColumns = string.Join(", ", entity.Columns.Select(static column => $"{QuoteNovaDb(column.ColumnName)} as {QuoteNovaDb(column.PropertyName)}"));
            var sqliteInsertColumnNames = string.Join(", ", insertColumns.Select(static column => QuoteSqlite(column.ColumnName)));
            var sqlServerInsertColumnNames = string.Join(", ", insertColumns.Select(static column => QuoteSqlServer(column.ColumnName)));
            var mySqlInsertColumnNames = string.Join(", ", insertColumns.Select(static column => QuoteMySql(column.ColumnName)));
            var novaDbInsertColumnNames = string.Join(", ", insertColumns.Select(static column => QuoteNovaDb(column.ColumnName)));
            var insertParameterNames = string.Join(", ", insertColumns.Select(static column => "@" + column.PropertyName));
            var sqliteUpdateAssignments = string.Join(", ", updateColumns.Select(static column => $"{QuoteSqlite(column.ColumnName)} = @{column.PropertyName}"));
            var sqlServerUpdateAssignments = string.Join(", ", updateColumns.Select(static column => $"{QuoteSqlServer(column.ColumnName)} = @{column.PropertyName}"));
            var mySqlUpdateAssignments = string.Join(", ", updateColumns.Select(static column => $"{QuoteMySql(column.ColumnName)} = @{column.PropertyName}"));
            var novaDbUpdateAssignments = string.Join(", ", updateColumns.Select(static column => $"{QuoteNovaDb(column.ColumnName)} = @{column.PropertyName}"));

            builder.AppendLine();
            builder.AppendLine($"{entity.Accessibility} static class {entity.TypeName}Crud");
            builder.AppendLine("{");
            builder.AppendLine($"    private const string SqliteSelectByIdSql = {Literal($"select {sqliteSelectableColumns} from {QuoteSqlite(entity.TableName)} where {QuoteSqlite(key.ColumnName)} = @{key.PropertyName}")};");
            builder.AppendLine($"    private const string SqlServerSelectByIdSql = {Literal($"select {sqlServerSelectableColumns} from {QuoteSqlServer(entity.TableName)} where {QuoteSqlServer(key.ColumnName)} = @{key.PropertyName}")};");
            builder.AppendLine($"    private const string MySqlSelectByIdSql = {Literal($"select {mySqlSelectableColumns} from {QuoteMySql(entity.TableName)} where {QuoteMySql(key.ColumnName)} = @{key.PropertyName}")};");
            builder.AppendLine($"    private const string NovaDbSelectByIdSql = {Literal($"select {novaDbSelectableColumns} from {QuoteNovaDb(entity.TableName)} where {QuoteNovaDb(key.ColumnName)} = @{key.PropertyName}")};");
            builder.AppendLine($"    private const string SqliteListAllSql = {Literal($"select {sqliteSelectableColumns} from {QuoteSqlite(entity.TableName)}")};");
            builder.AppendLine($"    private const string SqlServerListAllSql = {Literal($"select {sqlServerSelectableColumns} from {QuoteSqlServer(entity.TableName)}")};");
            builder.AppendLine($"    private const string MySqlListAllSql = {Literal($"select {mySqlSelectableColumns} from {QuoteMySql(entity.TableName)}")};");
            builder.AppendLine($"    private const string NovaDbListAllSql = {Literal($"select {novaDbSelectableColumns} from {QuoteNovaDb(entity.TableName)}")};");
            builder.AppendLine($"    private const string SqliteExistsSql = {Literal($"select case when exists (select 1 from {QuoteSqlite(entity.TableName)} where {QuoteSqlite(key.ColumnName)} = @{key.PropertyName}) then 1 else 0 end")};");
            builder.AppendLine($"    private const string SqlServerExistsSql = {Literal($"select case when exists (select 1 from {QuoteSqlServer(entity.TableName)} where {QuoteSqlServer(key.ColumnName)} = @{key.PropertyName}) then 1 else 0 end")};");
            builder.AppendLine($"    private const string MySqlExistsSql = {Literal($"select case when exists (select 1 from {QuoteMySql(entity.TableName)} where {QuoteMySql(key.ColumnName)} = @{key.PropertyName}) then 1 else 0 end")};");
            // NovaDb has no EXISTS (subquery): 1 IN (SELECT 1 …) evaluates identically.
            builder.AppendLine($"    private const string NovaDbExistsSql = {Literal($"select case when 1 in (select 1 from {QuoteNovaDb(entity.TableName)} where {QuoteNovaDb(key.ColumnName)} = @{key.PropertyName}) then 1 else 0 end")};");
            builder.AppendLine($"    private const string SqliteCountSql = {Literal($"select count(1) from {QuoteSqlite(entity.TableName)}")};");
            builder.AppendLine($"    private const string SqlServerCountSql = {Literal($"select count(1) from {QuoteSqlServer(entity.TableName)}")};");
            builder.AppendLine($"    private const string MySqlCountSql = {Literal($"select count(1) from {QuoteMySql(entity.TableName)}")};");
            builder.AppendLine($"    private const string NovaDbCountSql = {Literal($"select count(1) from {QuoteNovaDb(entity.TableName)}")};");
            builder.AppendLine($"    private const string SqliteDeleteByIdSql = {Literal($"delete from {QuoteSqlite(entity.TableName)} where {QuoteSqlite(key.ColumnName)} = @{key.PropertyName}")};");
            builder.AppendLine($"    private const string SqlServerDeleteByIdSql = {Literal($"delete from {QuoteSqlServer(entity.TableName)} where {QuoteSqlServer(key.ColumnName)} = @{key.PropertyName}")};");
            builder.AppendLine($"    private const string MySqlDeleteByIdSql = {Literal($"delete from {QuoteMySql(entity.TableName)} where {QuoteMySql(key.ColumnName)} = @{key.PropertyName}")};");
            builder.AppendLine($"    private const string NovaDbDeleteByIdSql = {Literal($"delete from {QuoteNovaDb(entity.TableName)} where {QuoteNovaDb(key.ColumnName)} = @{key.PropertyName}")};");
            if (insertColumns.Length > 0)
            {
                builder.AppendLine($"    private const string SqliteInsertSql = {Literal($"insert into {QuoteSqlite(entity.TableName)} ({sqliteInsertColumnNames}) values ({insertParameterNames})")};");
                builder.AppendLine($"    private const string SqlServerInsertSql = {Literal($"insert into {QuoteSqlServer(entity.TableName)} ({sqlServerInsertColumnNames}) values ({insertParameterNames})")};");
                builder.AppendLine($"    private const string MySqlInsertSql = {Literal($"insert into {QuoteMySql(entity.TableName)} ({mySqlInsertColumnNames}) values ({insertParameterNames})")};");
                builder.AppendLine($"    private const string NovaDbInsertSql = {Literal($"insert into {QuoteNovaDb(entity.TableName)} ({novaDbInsertColumnNames}) values ({insertParameterNames})")};");
                builder.AppendLine($"    private const string SqliteInsertReturningSql = {Literal($"insert into {QuoteSqlite(entity.TableName)} ({sqliteInsertColumnNames}) values ({insertParameterNames}) returning {sqliteSelectableColumns}")};");
            }
            if (updateColumns.Length > 0)
            {
                builder.AppendLine($"    private const string SqliteUpdateSql = {Literal($"update {QuoteSqlite(entity.TableName)} set {sqliteUpdateAssignments} where {QuoteSqlite(key.ColumnName)} = @{key.PropertyName}")};");
                builder.AppendLine($"    private const string SqlServerUpdateSql = {Literal($"update {QuoteSqlServer(entity.TableName)} set {sqlServerUpdateAssignments} where {QuoteSqlServer(key.ColumnName)} = @{key.PropertyName}")};");
                builder.AppendLine($"    private const string MySqlUpdateSql = {Literal($"update {QuoteMySql(entity.TableName)} set {mySqlUpdateAssignments} where {QuoteMySql(key.ColumnName)} = @{key.PropertyName}")};");
                builder.AppendLine($"    private const string NovaDbUpdateSql = {Literal($"update {QuoteNovaDb(entity.TableName)} set {novaDbUpdateAssignments} where {QuoteNovaDb(key.ColumnName)} = @{key.PropertyName}")};");
            }
            builder.AppendLine();
            builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<{entity.FullTypeName}?> GetByIdAsync(global::System.Data.Common.DbConnection connection, {key.TypeName} {ToParameterName(key.PropertyName)}, global::System.Data.Common.DbTransaction? transaction = null, global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine($"        return await GetByIdAsync(connection, {ToParameterName(key.PropertyName)}, global::FlyAotOrm.DbDialectKind.Sqlite, transaction, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<{entity.FullTypeName}?> GetByIdAsync(global::System.Data.Common.DbConnection connection, {key.TypeName} {ToParameterName(key.PropertyName)}, global::FlyAotOrm.DbDialectKind dialect, global::System.Data.Common.DbTransaction? transaction = null, global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine("        await using var command = CreateCommand(connection, transaction, GetSelectByIdSql(dialect));");
            builder.AppendLine($"        AddParameter(command, {Literal(key.PropertyName)}, {ToParameterName(key.PropertyName)});");
            builder.AppendLine("        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);");
            builder.AppendLine($"        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? {entity.TypeName}Table.Materialize(reader) : null;");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<{entity.FullTypeName}>> ListAllAsync(global::System.Data.Common.DbConnection connection, global::System.Data.Common.DbTransaction? transaction = null, global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine("        return await ListAllAsync(connection, global::FlyAotOrm.DbDialectKind.Sqlite, transaction, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<{entity.FullTypeName}>> ListAllAsync(global::System.Data.Common.DbConnection connection, global::FlyAotOrm.DbDialectKind dialect, global::System.Data.Common.DbTransaction? transaction = null, global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine("        await using var command = CreateCommand(connection, transaction, GetListAllSql(dialect));");
            builder.AppendLine("        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);");
            builder.AppendLine($"        var rows = new global::System.Collections.Generic.List<{entity.FullTypeName}>();");
            builder.AppendLine("        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))");
            builder.AppendLine("        {");
            builder.AppendLine($"            rows.Add({entity.TypeName}Table.Materialize(reader));");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        return rows;");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    public static async global::System.Threading.Tasks.Task<bool> ExistsAsync(global::System.Data.Common.DbConnection connection, " + key.TypeName + " " + ToParameterName(key.PropertyName) + ", global::System.Data.Common.DbTransaction? transaction = null, global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine($"        return await ExistsAsync(connection, {ToParameterName(key.PropertyName)}, global::FlyAotOrm.DbDialectKind.Sqlite, transaction, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    public static async global::System.Threading.Tasks.Task<bool> ExistsAsync(global::System.Data.Common.DbConnection connection, " + key.TypeName + " " + ToParameterName(key.PropertyName) + ", global::FlyAotOrm.DbDialectKind dialect, global::System.Data.Common.DbTransaction? transaction = null, global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine("        await using var command = CreateCommand(connection, transaction, GetExistsSql(dialect));");
            builder.AppendLine($"        AddParameter(command, {Literal(key.PropertyName)}, {ToParameterName(key.PropertyName)});");
            builder.AppendLine("        var exists = await ExecuteInt64ScalarAsync(command, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("        return exists != 0;");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    public static async global::System.Threading.Tasks.Task<long> CountAsync(global::System.Data.Common.DbConnection connection, global::System.Data.Common.DbTransaction? transaction = null, global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine("        return await CountAsync(connection, global::FlyAotOrm.DbDialectKind.Sqlite, transaction, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    public static async global::System.Threading.Tasks.Task<long> CountAsync(global::System.Data.Common.DbConnection connection, global::FlyAotOrm.DbDialectKind dialect, global::System.Data.Common.DbTransaction? transaction = null, global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine("        await using var command = CreateCommand(connection, transaction, GetCountSql(dialect));");
            builder.AppendLine("        return await ExecuteInt64ScalarAsync(command, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<int> DeleteByIdAsync(global::System.Data.Common.DbConnection connection, {key.TypeName} {ToParameterName(key.PropertyName)}, global::System.Data.Common.DbTransaction? transaction = null, global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine($"        return await DeleteByIdAsync(connection, {ToParameterName(key.PropertyName)}, global::FlyAotOrm.DbDialectKind.Sqlite, transaction, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<int> DeleteByIdAsync(global::System.Data.Common.DbConnection connection, {key.TypeName} {ToParameterName(key.PropertyName)}, global::FlyAotOrm.DbDialectKind dialect, global::System.Data.Common.DbTransaction? transaction = null, global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine("        await using var command = CreateCommand(connection, transaction, GetDeleteByIdSql(dialect));");
            builder.AppendLine($"        AddParameter(command, {Literal(key.PropertyName)}, {ToParameterName(key.PropertyName)});");
            builder.AppendLine("        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("    }");
            if (insertColumns.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<int> InsertAsync(global::System.Data.Common.DbConnection connection, {entity.FullTypeName} entity, global::System.Data.Common.DbTransaction? transaction = null, global::System.Threading.CancellationToken cancellationToken = default)");
                builder.AppendLine("    {");
                builder.AppendLine("        return await InsertAsync(connection, entity, global::FlyAotOrm.DbDialectKind.Sqlite, transaction, cancellationToken).ConfigureAwait(false);");
                builder.AppendLine("    }");
                builder.AppendLine();
                builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<int> InsertAsync(global::System.Data.Common.DbConnection connection, {entity.FullTypeName} entity, global::FlyAotOrm.DbDialectKind dialect, global::System.Data.Common.DbTransaction? transaction = null, global::System.Threading.CancellationToken cancellationToken = default)");
                builder.AppendLine("    {");
                builder.AppendLine("        await using var command = CreateCommand(connection, transaction, GetInsertSql(dialect));");
                foreach (var column in insertColumns)
                {
                    builder.AppendLine($"        AddParameter(command, {Literal(column.PropertyName)}, entity.{column.PropertyName});");
                }
                builder.AppendLine("        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);");
                builder.AppendLine("    }");
                builder.AppendLine();
                builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<{entity.FullTypeName}> InsertReturningAsync(global::System.Data.Common.DbConnection connection, {entity.FullTypeName} entity, global::System.Data.Common.DbTransaction? transaction = null, global::System.Threading.CancellationToken cancellationToken = default)");
                builder.AppendLine("    {");
                builder.AppendLine("        return await InsertReturningAsync(connection, entity, global::FlyAotOrm.DbDialectKind.Sqlite, transaction, cancellationToken).ConfigureAwait(false);");
                builder.AppendLine("    }");
                builder.AppendLine();
                builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<{entity.FullTypeName}> InsertReturningAsync(global::System.Data.Common.DbConnection connection, {entity.FullTypeName} entity, global::FlyAotOrm.DbDialectKind dialect, global::System.Data.Common.DbTransaction? transaction = null, global::System.Threading.CancellationToken cancellationToken = default)");
                builder.AppendLine("    {");
                builder.AppendLine("        await using var command = CreateCommand(connection, transaction, GetInsertReturningSql(dialect));");
                foreach (var column in insertColumns)
                {
                    builder.AppendLine($"        AddParameter(command, {Literal(column.PropertyName)}, entity.{column.PropertyName});");
                }
                builder.AppendLine("        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);");
                builder.AppendLine("        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))");
                builder.AppendLine("        {");
                builder.AppendLine($"            return {entity.TypeName}Table.Materialize(reader);");
                builder.AppendLine("        }");
                builder.AppendLine();
                builder.AppendLine("        throw new global::System.InvalidOperationException(\"InsertReturningAsync did not return a row.\");");
                builder.AppendLine("    }");
            }
            if (updateColumns.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<int> UpdateAsync(global::System.Data.Common.DbConnection connection, {entity.FullTypeName} entity, global::System.Data.Common.DbTransaction? transaction = null, global::System.Threading.CancellationToken cancellationToken = default)");
                builder.AppendLine("    {");
                builder.AppendLine("        return await UpdateAsync(connection, entity, global::FlyAotOrm.DbDialectKind.Sqlite, transaction, cancellationToken).ConfigureAwait(false);");
                builder.AppendLine("    }");
                builder.AppendLine();
                builder.AppendLine($"    public static async global::System.Threading.Tasks.Task<int> UpdateAsync(global::System.Data.Common.DbConnection connection, {entity.FullTypeName} entity, global::FlyAotOrm.DbDialectKind dialect, global::System.Data.Common.DbTransaction? transaction = null, global::System.Threading.CancellationToken cancellationToken = default)");
                builder.AppendLine("    {");
                builder.AppendLine("        await using var command = CreateCommand(connection, transaction, GetUpdateSql(dialect));");
                foreach (var column in updateColumns)
                {
                    builder.AppendLine($"        AddParameter(command, {Literal(column.PropertyName)}, entity.{column.PropertyName});");
                }
                builder.AppendLine($"        AddParameter(command, {Literal(key.PropertyName)}, entity.{key.PropertyName});");
                builder.AppendLine("        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);");
                builder.AppendLine("    }");
            }
            builder.AppendLine();
            builder.AppendLine("    private static string GetSelectByIdSql(global::FlyAotOrm.DbDialectKind dialect) => dialect switch");
            builder.AppendLine("    {");
            builder.AppendLine("        global::FlyAotOrm.DbDialectKind.SqlServer => SqlServerSelectByIdSql,");
            builder.AppendLine("        global::FlyAotOrm.DbDialectKind.MySql => MySqlSelectByIdSql,");
            builder.AppendLine("        global::FlyAotOrm.DbDialectKind.NovaDb => NovaDbSelectByIdSql,");
            builder.AppendLine("        _ => SqliteSelectByIdSql,");
            builder.AppendLine("    };");
            builder.AppendLine();
            builder.AppendLine("    private static string GetListAllSql(global::FlyAotOrm.DbDialectKind dialect) => dialect switch");
            builder.AppendLine("    {");
            builder.AppendLine("        global::FlyAotOrm.DbDialectKind.SqlServer => SqlServerListAllSql,");
            builder.AppendLine("        global::FlyAotOrm.DbDialectKind.MySql => MySqlListAllSql,");
            builder.AppendLine("        global::FlyAotOrm.DbDialectKind.NovaDb => NovaDbListAllSql,");
            builder.AppendLine("        _ => SqliteListAllSql,");
            builder.AppendLine("    };");
            builder.AppendLine();
            builder.AppendLine("    private static string GetExistsSql(global::FlyAotOrm.DbDialectKind dialect) => dialect switch");
            builder.AppendLine("    {");
            builder.AppendLine("        global::FlyAotOrm.DbDialectKind.SqlServer => SqlServerExistsSql,");
            builder.AppendLine("        global::FlyAotOrm.DbDialectKind.MySql => MySqlExistsSql,");
            builder.AppendLine("        global::FlyAotOrm.DbDialectKind.NovaDb => NovaDbExistsSql,");
            builder.AppendLine("        _ => SqliteExistsSql,");
            builder.AppendLine("    };");
            builder.AppendLine();
            builder.AppendLine("    private static string GetCountSql(global::FlyAotOrm.DbDialectKind dialect) => dialect switch");
            builder.AppendLine("    {");
            builder.AppendLine("        global::FlyAotOrm.DbDialectKind.SqlServer => SqlServerCountSql,");
            builder.AppendLine("        global::FlyAotOrm.DbDialectKind.MySql => MySqlCountSql,");
            builder.AppendLine("        global::FlyAotOrm.DbDialectKind.NovaDb => NovaDbCountSql,");
            builder.AppendLine("        _ => SqliteCountSql,");
            builder.AppendLine("    };");
            builder.AppendLine();
            builder.AppendLine("    private static string GetDeleteByIdSql(global::FlyAotOrm.DbDialectKind dialect) => dialect switch");
            builder.AppendLine("    {");
            builder.AppendLine("        global::FlyAotOrm.DbDialectKind.SqlServer => SqlServerDeleteByIdSql,");
            builder.AppendLine("        global::FlyAotOrm.DbDialectKind.MySql => MySqlDeleteByIdSql,");
            builder.AppendLine("        global::FlyAotOrm.DbDialectKind.NovaDb => NovaDbDeleteByIdSql,");
            builder.AppendLine("        _ => SqliteDeleteByIdSql,");
            builder.AppendLine("    };");
            if (insertColumns.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine("    private static string GetInsertSql(global::FlyAotOrm.DbDialectKind dialect) => dialect switch");
                builder.AppendLine("    {");
                builder.AppendLine("        global::FlyAotOrm.DbDialectKind.SqlServer => SqlServerInsertSql,");
                builder.AppendLine("        global::FlyAotOrm.DbDialectKind.MySql => MySqlInsertSql,");
                builder.AppendLine("        global::FlyAotOrm.DbDialectKind.NovaDb => NovaDbInsertSql,");
                builder.AppendLine("        _ => SqliteInsertSql,");
                builder.AppendLine("    };");
                builder.AppendLine();
                builder.AppendLine("    private static string GetInsertReturningSql(global::FlyAotOrm.DbDialectKind dialect) => dialect switch");
                builder.AppendLine("    {");
                builder.AppendLine("        global::FlyAotOrm.DbDialectKind.Sqlite => SqliteInsertReturningSql,");
                builder.AppendLine("        global::FlyAotOrm.DbDialectKind.PostgreSql => SqliteInsertReturningSql,");
                builder.AppendLine("        _ => throw new global::System.NotSupportedException(\"InsertReturningAsync is currently generated only for SQLite and PostgreSQL.\"),");
                builder.AppendLine("    };");
            }
            if (updateColumns.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine("    private static string GetUpdateSql(global::FlyAotOrm.DbDialectKind dialect) => dialect switch");
                builder.AppendLine("    {");
                builder.AppendLine("        global::FlyAotOrm.DbDialectKind.SqlServer => SqlServerUpdateSql,");
                builder.AppendLine("        global::FlyAotOrm.DbDialectKind.MySql => MySqlUpdateSql,");
                builder.AppendLine("        global::FlyAotOrm.DbDialectKind.NovaDb => NovaDbUpdateSql,");
                builder.AppendLine("        _ => SqliteUpdateSql,");
                builder.AppendLine("    };");
            }
            builder.AppendLine();
            builder.AppendLine("    private static global::System.Data.Common.DbCommand CreateCommand(global::System.Data.Common.DbConnection connection, global::System.Data.Common.DbTransaction? transaction, string sql)");
            builder.AppendLine("    {");
            builder.AppendLine("        var command = connection.CreateCommand();");
            builder.AppendLine("        command.CommandText = sql;");
            builder.AppendLine("        if (transaction is not null)");
            builder.AppendLine("        {");
            builder.AppendLine("            command.Transaction = transaction;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        return command;");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private static void AddParameter(global::System.Data.Common.DbCommand command, string name, object? value)");
            builder.AppendLine("    {");
            builder.AppendLine("        var parameter = command.CreateParameter();");
            builder.AppendLine("        parameter.ParameterName = \"@\" + name;");
            builder.AppendLine("        parameter.Value = ConvertParameter(value) ?? global::System.DBNull.Value;");
            builder.AppendLine("        command.Parameters.Add(parameter);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private static object? ConvertParameter(object? value)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (value is null)");
            builder.AppendLine("        {");
            builder.AppendLine("            return null;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        return global::FlyAotOrm.FlyDbValueConverter.ToDatabase(value);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private static async global::System.Threading.Tasks.Task<long> ExecuteInt64ScalarAsync(global::System.Data.Common.DbCommand command, global::System.Threading.CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.AppendLine("        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("        return global::System.Convert.ToInt64(value, global::System.Globalization.CultureInfo.InvariantCulture);");
            builder.AppendLine("    }");
            builder.AppendLine("}");
        }
    }

    private static class DbContextEmitter
    {
        public static string Emit(string ns, IReadOnlyList<EntityModel> entities)
        {
            var accessibility = entities.All(static entity => entity.Accessibility == "public")
                ? "public"
                : "internal";
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("#nullable enable");
            builder.AppendLine();
            AppendNamespaceStart(builder, ns);
            builder.AppendLine($"{accessibility} sealed class FlyDbContext");
            builder.AppendLine("{");
            builder.AppendLine("    private readonly global::FlyAotOrm.FlyDb _db;");
            builder.AppendLine();
            builder.AppendLine("    public FlyDbContext(global::FlyAotOrm.FlyDb db)");
            builder.AppendLine("    {");
            builder.AppendLine("        _db = db ?? throw new global::System.ArgumentNullException(nameof(db));");
            builder.AppendLine("    }");
            builder.AppendLine();

            foreach (var (entity, propertyName) in GetContextProperties(entities))
            {
                var queryExpression = GetDefaultFilterExpression(entity) is { } defaultFilter
                    ? $"_db.Select(Tables.{entity.TypeName}){defaultFilter}"
                    : $"_db.Select(Tables.{entity.TypeName})";
                builder.AppendLine($"    public global::FlyAotOrm.Query<{entity.FullTypeName}> {propertyName} => {queryExpression};");
            }

            builder.AppendLine("}");
            AppendNamespaceEnd(builder, ns);
            return builder.ToString();
        }

        private static IEnumerable<(EntityModel Entity, string PropertyName)> GetContextProperties(IReadOnlyList<EntityModel> entities)
        {
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entity in entities)
            {
                var propertyName = ToContextPropertyName(entity.TypeName);
                if (!usedNames.Add(propertyName))
                {
                    propertyName = entity.TypeName + "Query";
                    var suffix = 2;
                    while (!usedNames.Add(propertyName))
                    {
                        propertyName = entity.TypeName + "Query" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        suffix++;
                    }
                }

                yield return (entity, propertyName);
            }
        }

        private static string ToContextPropertyName(string typeName)
        {
            if (typeName.EndsWith("y", StringComparison.Ordinal) && typeName.Length > 1 && !IsVowel(typeName[typeName.Length - 2]))
            {
                return typeName.Substring(0, typeName.Length - 1) + "ies";
            }

            if (typeName.EndsWith("s", StringComparison.Ordinal))
            {
                return typeName;
            }

            return typeName + "s";
        }

        private static bool IsVowel(char value)
            => value is 'a' or 'e' or 'i' or 'o' or 'u' or 'A' or 'E' or 'I' or 'O' or 'U';

        private static string? GetDefaultFilterExpression(EntityModel entity)
        {
            var filters = ImmutableArray.CreateBuilder<string>();
            var softDeleteColumn = entity.Columns.FirstOrDefault(static column => column.IsSoftDelete);
            if (softDeleteColumn is not null)
            {
                filters.Add($"Tables.{entity.TypeName}.{softDeleteColumn.PropertyName}.Eq(false)");
            }

            var tenantColumn = entity.Columns.FirstOrDefault(static column => column.IsTenant);
            if (tenantColumn is not null)
            {
                filters.Add($"Tables.{entity.TypeName}.{tenantColumn.PropertyName}.Eq(global::FlyAotOrm.FlyTenant.Get<{tenantColumn.TypeName}>())");
            }

            foreach (var filterExpression in entity.DefaultFilterExpressions)
            {
                filters.Add(filterExpression);
            }

            return BuildDefaultFilterChain(filters);
        }

        private static string? BuildDefaultFilterChain(IEnumerable<string> filters)
        {
            using var enumerator = filters.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                return null;
            }

            var expression = $".Where({enumerator.Current})";
            while (enumerator.MoveNext())
            {
                expression = $"{expression}.And({enumerator.Current})";
            }

            return expression;
        }
    }

    private static class RepositoryEmitter
    {
        public static string Emit(RepositoryModel repository)
        {
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("#nullable enable");
            builder.AppendLine();
            builder.AppendLine("using System.Data.Common;");
            builder.AppendLine();
            AppendNamespaceStart(builder, repository.Namespace);
            builder.AppendLine($"{repository.Accessibility} sealed class {repository.ImplementationName} : {repository.InterfaceName}");
            builder.AppendLine("{");
            builder.AppendLine("    private readonly DbConnection _connection;");
            builder.AppendLine("    private readonly DbTransaction? _transaction;");
            builder.AppendLine("    private readonly global::FlyAotOrm.FlyDb _db;");
            builder.AppendLine();
            builder.AppendLine($"    public {repository.ImplementationName}(DbConnection connection, DbTransaction? transaction = null)");
            builder.AppendLine("        : this(connection, global::FlyAotOrm.DbDialectKind.Sqlite, transaction)");
            builder.AppendLine("    {");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine($"    public {repository.ImplementationName}(DbConnection connection, global::FlyAotOrm.DbDialectKind dialect, DbTransaction? transaction = null)");
            builder.AppendLine("    {");
            builder.AppendLine("        _connection = connection;");
            builder.AppendLine("        _transaction = transaction;");
            builder.AppendLine("        _db = new global::FlyAotOrm.FlyDb(connection, dialect, transaction);");
            builder.AppendLine("    }");
            builder.AppendLine();

            foreach (var method in repository.Methods)
            {
                EmitMethod(builder, method);
                builder.AppendLine();
            }

            EmitHelpers(builder);
            builder.AppendLine("}");
            AppendNamespaceEnd(builder, repository.Namespace);
            return builder.ToString();
        }

        private static void EmitMethod(StringBuilder builder, RepositoryMethodModel method)
        {
            var parameters = string.Join(", ", method.Parameters.Select(static p => $"{p.TypeName} {p.Name}"));
            if (method.Kind == SqlMethodKind.QueryDsl)
            {
                builder.AppendLine($"    public {method.ReturnType} {method.Name}({parameters})");
                builder.AppendLine("    {");
                var queryExpression = method.QueryDefaultFilterExpression is null
                    ? $"_db.{method.QueryFactoryMethod}({method.QueryTableReference})"
                    : $"_db.{method.QueryFactoryMethod}({method.QueryTableReference}){method.QueryDefaultFilterExpression}";
                builder.AppendLine($"        return {queryExpression};");
                builder.AppendLine("    }");
                return;
            }

            var cancellationToken = method.Parameters.FirstOrDefault(static p => p.IsCancellationToken)?.Name ?? "default(global::System.Threading.CancellationToken)";
            builder.AppendLine($"    public async {method.ReturnType} {method.Name}({parameters})");
            builder.AppendLine("    {");
            builder.AppendLine($"        const string sql = {Literal(method.Sql)};");
            builder.AppendLine("        await using var command = CreateCommand(sql);");
            foreach (var parameter in method.Parameters.Where(static parameter => !parameter.IsCancellationToken))
            {
                builder.AppendLine($"        AddParameter(command, {Literal(parameter.Name)}, {parameter.Name});");
            }

            switch (method.Kind)
            {
                case SqlMethodKind.Execute:
                    builder.AppendLine($"        return await command.ExecuteNonQueryAsync({cancellationToken}).ConfigureAwait(false);");
                    break;
                case SqlMethodKind.Scalar:
                    builder.AppendLine($"        var scalar = await command.ExecuteScalarAsync({cancellationToken}).ConfigureAwait(false);");
                    builder.AppendLine($"        return ConvertScalar<{method.ResultType}>(scalar);");
                    break;
                default:
                    if (method.IsCollectionResult)
                    {
                        builder.AppendLine($"        await using var reader = await command.ExecuteReaderAsync({cancellationToken}).ConfigureAwait(false);");
                        builder.AppendLine($"        var rows = new global::System.Collections.Generic.List<{method.ResultType}>();");
                        builder.AppendLine($"        while (await reader.ReadAsync({cancellationToken}).ConfigureAwait(false))");
                        builder.AppendLine("        {");
                        builder.AppendLine($"            rows.Add(Materialize<{method.ResultType}>(reader));");
                        builder.AppendLine("        }");
                        builder.AppendLine();
                        builder.AppendLine("        return rows;");
                    }
                    else
                    {
                        builder.AppendLine($"        await using var reader = await command.ExecuteReaderAsync({cancellationToken}).ConfigureAwait(false);");
                        builder.AppendLine($"        return await reader.ReadAsync({cancellationToken}).ConfigureAwait(false) ? Materialize<{method.ResultType}>(reader) : default;");
                    }
                    break;
            }

            builder.AppendLine("    }");
        }

        private static void EmitHelpers(StringBuilder builder)
        {
            builder.AppendLine("    private DbCommand CreateCommand(string sql)");
            builder.AppendLine("    {");
            builder.AppendLine("        var command = _connection.CreateCommand();");
            builder.AppendLine("        command.CommandText = sql;");
            builder.AppendLine("        if (_transaction is not null)");
            builder.AppendLine("        {");
            builder.AppendLine("            command.Transaction = _transaction;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        return command;");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private static void AddParameter(DbCommand command, string name, object? value)");
            builder.AppendLine("    {");
            builder.AppendLine("        var parameter = command.CreateParameter();");
            builder.AppendLine("        parameter.ParameterName = \"@\" + name;");
            builder.AppendLine("        parameter.Value = ConvertParameter(value) ?? global::System.DBNull.Value;");
            builder.AppendLine("        command.Parameters.Add(parameter);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private static object? ConvertParameter(object? value)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (value is null)");
            builder.AppendLine("        {");
            builder.AppendLine("            return null;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        return global::FlyAotOrm.FlyDbValueConverter.ToDatabase(value);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private static T Materialize<T>(DbDataReader reader)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (global::FlyAotOrm.FlyProjectionMaterializerRegistry.TryGet<T>(out var generated))");
            builder.AppendLine("        {");
            builder.AppendLine("            return generated(reader);");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        throw new global::System.InvalidOperationException(\"No generated materializer is registered for \" + typeof(T).FullName + \". Mark the DTO with [DbProjection] or use a repository query return type that the generator can materialize.\");");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private static T ConvertScalar<T>(object? value)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (value is null || value is global::System.DBNull)");
            builder.AppendLine("        {");
            builder.AppendLine("            return default!;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        return global::FlyAotOrm.FlyDbValueConverter.Read<T>(value);");
            builder.AppendLine("    }");
        }
    }

    private static class ProjectionEmitter
    {
        public static string Emit(ProjectionModel projection)
        {
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine("#nullable enable");
            builder.AppendLine();
            AppendNamespaceStart(builder, projection.Namespace);
            builder.AppendLine($"{projection.Accessibility} static class {projection.MaterializerTypeName}");
            builder.AppendLine("{");
            builder.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
            builder.AppendLine("    internal static void Register()");
            builder.AppendLine("    {");
            builder.AppendLine($"        global::FlyAotOrm.FlyProjectionMaterializerRegistry.Register<{projection.FullTypeName}>(Materialize);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine($"    internal static {projection.FullTypeName} Materialize(global::System.Data.Common.DbDataReader reader)");
            builder.AppendLine("    {");
            builder.AppendLine($"        var value = new {projection.FullTypeName}();");
            builder.AppendLine("        for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)");
            builder.AppendLine("        {");
            builder.AppendLine("            var name = reader.GetName(ordinal);");
            foreach (var property in projection.Properties)
            {
                builder.AppendLine($"            if (global::System.StringComparer.OrdinalIgnoreCase.Equals(name, {Literal(property.PropertyName)}))");
                builder.AppendLine("            {");
                builder.AppendLine($"                value.{property.PropertyName} = Read<{property.TypeName}>(reader, ordinal);");
                builder.AppendLine("                continue;");
                builder.AppendLine("            }");
            }
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        return value;");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private static T Read<T>(global::System.Data.Common.DbDataReader reader, int ordinal)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (reader.IsDBNull(ordinal))");
            builder.AppendLine("        {");
            builder.AppendLine("            return default!;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        var value = reader.GetValue(ordinal);");
            builder.AppendLine("        return global::FlyAotOrm.FlyDbValueConverter.Read<T>(value);");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            AppendNamespaceEnd(builder, projection.Namespace);
            return builder.ToString();
        }
    }

    private static void AppendNamespaceStart(StringBuilder builder, string ns)
    {
        if (string.IsNullOrEmpty(ns))
        {
            return;
        }

        builder.AppendLine($"namespace {ns}");
        builder.AppendLine("{");
    }

    private static void AppendNamespaceEnd(StringBuilder builder, string ns)
    {
        if (!string.IsNullOrEmpty(ns))
        {
            builder.AppendLine("}");
        }
    }

    private static string Literal(string value)
        => "\"" + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n") + "\"";

    private static string SanitizeSourceName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }

    private static string QuoteSqlite(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static string QuoteSqlServer(string identifier)
        => "[" + identifier.Replace("]", "]]") + "]";

    private static string QuoteMySql(string identifier)
        => "`" + identifier.Replace("`", "``") + "`";

    private static string QuoteNovaDb(string identifier)
        => "`" + identifier.Replace("`", "``") + "`";

    private static string ToCSharpBool(bool value)
        => value ? "true" : "false";

    private static string ToParameterName(string propertyName)
        => propertyName.Length == 0
            ? propertyName
            : char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
}

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
{
    private readonly ImmutableArray<T> _array;

    public EquatableArray(ImmutableArray<T> array) => _array = array;

    public int Count => _array.Length;

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_array).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(EquatableArray<T> other) => _array.SequenceEqual(other._array);

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in _array)
        {
            hash = (hash * 31) + (item?.GetHashCode() ?? 0);
        }

        return hash;
    }
}
