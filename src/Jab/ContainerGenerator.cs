namespace Jab;

using System.Linq;

[Generator]
#pragma warning disable RS1001 // We don't want this to be discovered as analyzer but it simplifies testing
public partial class ContainerGenerator : DiagnosticAnalyzer
#pragma warning restore RS1001 // We don't want this to be discovered as analyzer but it simplifies testing
{
    /// <summary>Code for a [GeneratedCode] attribute to put on the top-level generated members.</summary>
    private static readonly string _generatedCodeAttribute = $"[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"{typeof(ContainerGenerator).Assembly.GetName().Name}\", \"{typeof(ContainerGenerator).Assembly.GetName().Version}\")]";

    private void GenerateCallSiteWithCache(
        CodeWriter codeWriter,
        string rootReference,
        ServiceCallSite serviceCallSite,
        Action<CodeWriter, CodeWriterDelegate> valueCallback,
        Dictionary<INamedTypeSymbol, string> typeBaseNameMap)
    {
        if (serviceCallSite is ErrorCallSite errorCallSite)
        {
            codeWriter.Line($"// There was an error while building the container, please refer to the compiler diagnostics");
            foreach (var diagnostic in errorCallSite.Diagnostic)
            {
                codeWriter.Line($"// {diagnostic.ToString()}");
            }
            codeWriter.Line($"return default!;");
            return;
        }

        if (serviceCallSite.Lifetime != ServiceLifetime.Transient)
        {
            var cacheLocation = GetCacheLocation(serviceCallSite.Identity, typeBaseNameMap);
            codeWriter.Line($"if ({cacheLocation} == null)");
            codeWriter.Line($"lock (this)");
            using (codeWriter.Scope($"if ({cacheLocation} == null)"))
            {
                GenerateCallSite(
                    codeWriter,
                    rootReference,
                    serviceCallSite,
                    (w, v) =>
                    {
                        w.Line($"{cacheLocation} = {v};");
                    },
                    typeBaseNameMap);
            }

            if (serviceCallSite.ImplementationType.IsValueType)
            {
                valueCallback(codeWriter, w => w.Append($"{cacheLocation}.Value"));
            }
            else
            {
                valueCallback(codeWriter, w => w.Append($"{cacheLocation}"));
            }
        }
        else if (serviceCallSite.IsDisposable != false)
        {
            GenerateCallSite(codeWriter, rootReference, serviceCallSite, (w, v) =>
            {
                w.Line($"{serviceCallSite.ImplementationType} service = {v};");
            }, typeBaseNameMap);
            codeWriter.Line($"TryAddDisposable(service);");
            valueCallback(codeWriter, w => w.Append($"service"));
        }
        else
        {
            GenerateCallSite(codeWriter, rootReference, serviceCallSite, valueCallback, typeBaseNameMap);
        }
    }

    private void WriteResolutionCall(CodeWriter codeWriter, ServiceIdentity other, string reference, Dictionary<INamedTypeSymbol, string> typeBaseNameMap)
    {
        if (other.IsMainImplementation)
        {
            codeWriter.Append($"{reference}.GetService<{other.Type}>()");
        }
        else
        {
            codeWriter.Append($"{reference}.{GetResolutionServiceName(other, typeBaseNameMap)}()");
        }
    }

    private static void AppendMemberReference(CodeWriter codeWriter, ISymbol method, MemberLocation memberLocation, string rootReference)
    {
        if (method.IsStatic)
        {
            if (memberLocation == MemberLocation.Module)
            {
                codeWriter.Append($"{method.ContainingType}.");
            }
        }
        else
        {
            switch (memberLocation)
            {
                case MemberLocation.Module:
                case MemberLocation.Root:
                    codeWriter.Append($"this.");
                    break;
                case MemberLocation.Scope:
                    codeWriter.Append($"{rootReference}.");
                    break;
            }
        }

        codeWriter.Append($"{method.Name}");
    }

    private static void AppendMemberGenericParameters(CodeWriter codeWriter, ISymbol symbol)
    {
        if (symbol is IMethodSymbol method)
        {
            if (method.TypeArguments.Length > 0)
            {
                codeWriter.AppendRaw("<");
                foreach (var typeArgument in method.TypeArguments)
                {
                    codeWriter.Append($"{typeArgument}, ");
                }
                codeWriter.RemoveTrailingComma();
                codeWriter.AppendRaw(">");
            }
        }
    }

    private void AppendParameters(
        CodeWriter codeWriter,
        ServiceCallSite[] parameters,
        KeyValuePair<IParameterSymbol, ServiceCallSite>[] optionalParameters,
        Dictionary<INamedTypeSymbol, string> typeBaseNameMap)
    {
        foreach (var parameter in parameters)
        {
            WriteResolutionCall(codeWriter, parameter.Identity, "this", typeBaseNameMap);
            codeWriter.AppendRaw(", ");
        }

        foreach (var pair in optionalParameters)
        {
            codeWriter.Append($"{pair.Key.Name}: ");
            WriteResolutionCall(codeWriter, pair.Value.Identity, "this", typeBaseNameMap);
            codeWriter.AppendRaw(", ");
        }
        codeWriter.RemoveTrailingComma();
    }

    private void GenerateCallSite(
        CodeWriter codeWriter,
        string rootReference,
        ServiceCallSite serviceCallSite,
        Action<CodeWriter, CodeWriterDelegate> valueCallback,
        Dictionary<INamedTypeSymbol, string> typeBaseNameMap)
    {
        switch (serviceCallSite)
        {
            case ConstructorCallSite transientCallSite:
                valueCallback(codeWriter, w =>
                {
                    w.Append($"new {transientCallSite.ImplementationType}(");
                    AppendParameters(w, transientCallSite.Parameters, transientCallSite.OptionalParameters, typeBaseNameMap);
                    w.Append($")");
                });
                break;
            case MemberCallSite memberCallSite:
                valueCallback(codeWriter, w =>
                {
                    AppendMemberReference(w, memberCallSite.Member, memberCallSite.MemberLocation, rootReference);
                });
                break;
            case FactoryCallSite methodCallSite:
                valueCallback(codeWriter, w =>
                {
                    AppendMemberReference(w, methodCallSite.Member, methodCallSite.MemberLocation, rootReference);
                    AppendMemberGenericParameters(w, methodCallSite.Member);

                    w.AppendRaw("(");
                    AppendParameters(w, methodCallSite.Parameters, methodCallSite.OptionalParameters, typeBaseNameMap);
                    w.Append($")");
                });
                break;
            case ArrayServiceCallSite arrayServiceCallSite:
                valueCallback(codeWriter, w =>
                {
                    using (w.Scope($"new {arrayServiceCallSite.ItemType}[]", newLine: false))
                    {
                        foreach (var item in arrayServiceCallSite.Items)
                        {
                            WriteResolutionCall(codeWriter, item.Identity, "this", typeBaseNameMap);
                            w.LineRaw(", ");
                        }
                    }
                });
                break;
            case ServiceProviderCallSite:
                valueCallback(codeWriter, w => w.AppendRaw("this"));
                break;
            case ServiceProviderIsServiceCallSite:
            case ScopeFactoryCallSite:
                valueCallback(codeWriter, w => w.AppendRaw(rootReference));
                break;
        }
    }

    private void Execute(GeneratorContext context)
    {
        try
        {
            var roots = new ServiceProviderBuilder(context).BuildRoots();

            // Pre-compute unique base names for all service types across all roots
            var typeBaseNameMap = BuildTypeBaseNameMap(roots);

            foreach (var root in roots)
            {
                var codeWriter = new CodeWriter();
                codeWriter.UseNamespace("Jab");
                codeWriter.UseNamespace("System");
                codeWriter.UseNamespace("System.Diagnostics");
                codeWriter.UseNamespace("System.Diagnostics.CodeAnalysis");
                codeWriter.Line($"using static Jab.JabHelpers;");
                using (root.Type.ContainingNamespace.IsGlobalNamespace ?
                           default :
                           codeWriter.Namespace($"{root.Type.ContainingNamespace.ToDisplayString()}"))
                {
                    // TODO: implement infinite nesting
                    using CodeWriter.CodeWriterScope? parentTypeScope = root.Type.ContainingType is {} containingType ?
                        codeWriter.Scope($"{SyntaxFacts.GetText(containingType.DeclaredAccessibility)} partial class {containingType.Name}") :
                        null;

                    codeWriter.LineRaw(_generatedCodeAttribute);
                    codeWriter.Append($"{SyntaxFacts.GetText(root.Type.DeclaredAccessibility)} partial class {root.Type.Name}");
                    WriteInterfaces(codeWriter, root, false);
                    using (codeWriter.Scope())
                    {
                        codeWriter.Line($"private Scope? _rootScope;");
                        WriteCacheLocations(root, codeWriter, isScope: false, typeBaseNameMap);

                        foreach (var rootService in root.RootCallSites)
                        {
                            var rootServiceType = rootService.Identity.Type;
                            if (rootService.Identity.IsMainImplementation)
                            {
                                codeWriter.Append($"{rootServiceType} IServiceProvider<{rootServiceType}>.GetService()");
                            }
                            else
                            {
                                codeWriter.Append($"private {rootServiceType} {GetResolutionServiceName(rootService.Identity, typeBaseNameMap)}()");
                            }

                            if (rootService.Lifetime == ServiceLifetime.Scoped)
                            {
                                codeWriter.Line($" => GetRootScope().GetService<{rootServiceType}>();");
                            }
                            else
                            {
                                codeWriter.Line();
                                using (codeWriter.Scope())
                                {
                                    GenerateCallSiteWithCache(codeWriter,
                                        "this",
                                        rootService,
                                        (w, v) => w.Line($"return {v};"),
                                        typeBaseNameMap);
                                }
                            }

                            codeWriter.Line();
                        }

                        WriteNamedServiceProvider(codeWriter, root, typeBaseNameMap);
                        WriteServiceProvider(codeWriter, root, typeBaseNameMap);
                        WriteDispose(codeWriter, root, isScoped: false, typeBaseNameMap);
                        WritePublicGetServiceMethods(codeWriter);

                        codeWriter.Line($"public Scope CreateScope() => new Scope(this);");
                        codeWriter.Line();

                        if (root.KnownTypes.IServiceScopeFactoryType != null)
                        {
                            codeWriter.Line($"{root.KnownTypes.IServiceScopeType} {root.KnownTypes.IServiceScopeFactoryType}.CreateScope() => this.CreateScope();");
                            codeWriter.Line();
                        }

                        if (root.KnownTypes.IServiceProviderIsServiceType != null)
                        {
                            using var _ = codeWriter.Scope($"bool {root.KnownTypes.IServiceProviderIsServiceType}.IsService(Type service) => ", "", "");
                            bool first = true;
                            foreach (var rootService in root.RootCallSites)
                            {
                                if (first)
                                {
                                    first = false;
                                }
                                else
                                {
                                    codeWriter.Line($" ||");
                                }
                                codeWriter.Append($"typeof({rootService.Identity.Type}) == service");
                            }
                            if (first)
                            {
                                codeWriter.Append($"false");
                            }
                            codeWriter.Line($";");
                        }

                        codeWriter.Append($"public partial class Scope");
                        WriteInterfaces(codeWriter, root, true);
                        using (codeWriter.Scope())
                        {
                            WriteCacheLocations(root, codeWriter, isScope: true, typeBaseNameMap);
                            codeWriter.Line($"private {root.Type} _root;");
                            codeWriter.Line();

                            using (codeWriter.Scope($"public Scope({root.Type} root)"))
                            {
                                codeWriter.Line($"_root = root;");
                            }
                            codeWriter.Line();

                            WritePublicGetServiceMethods(codeWriter);

                            foreach (var rootService in root.RootCallSites)
                            {
                                var rootServiceType = rootService.Identity.Type;

                                using (rootService.Identity.IsMainImplementation ?
                                           codeWriter.Scope($"{rootServiceType} IServiceProvider<{rootServiceType}>.GetService()") :
                                           codeWriter.Scope($"private {rootServiceType} {GetResolutionServiceName(rootService.Identity, typeBaseNameMap)}()"))
                                {
                                    if (rootService.Lifetime == ServiceLifetime.Singleton)
                                    {
                                        codeWriter.Append($"return ");
                                        WriteResolutionCall(codeWriter, rootService.Identity, "_root", typeBaseNameMap);
                                        codeWriter.Line($";");
                                    }
                                    else
                                    {
                                        GenerateCallSiteWithCache(codeWriter,
                                            "_root",
                                            rootService,
                                            (w, v) => w.Line($"return {v};"),
                                            typeBaseNameMap);
                                    }
                                }
                                codeWriter.Line();
                            }

                            WriteServiceProvider(codeWriter, root, typeBaseNameMap);
                            WriteNamedServiceProvider(codeWriter, root, typeBaseNameMap);

                            if (root.KnownTypes.IServiceScopeType != null)
                            {
                                codeWriter.Line($"{root.KnownTypes.IServiceProviderType} {root.KnownTypes.IServiceScopeType}.ServiceProvider => this;");
                                codeWriter.Line();
                            }
                            WriteDispose(codeWriter, root, isScoped: true, typeBaseNameMap);
                        }

                        using (codeWriter.Scope($"private Scope GetRootScope()"))
                        {
                            codeWriter.Line($"if (_rootScope == default)");
                            codeWriter.Line($"lock (this)");
                            using (codeWriter.Scope($"if (_rootScope == default)"))
                            {
                                codeWriter.Line($"_rootScope = CreateScope();");
                            }
                            codeWriter.Line($"return _rootScope;");
                        }
                    }
                }
                context.AddSource($"{root.Type.Name}.Generated.cs", codeWriter.ToString());
            }
        }
        catch (Exception e)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UnexpectedErrorDescriptor, Location.None, e.ToString().Replace(Environment.NewLine, " ")));
        }
    }

    private Dictionary<INamedTypeSymbol, string> BuildTypeBaseNameMap(IEnumerable<ServiceProvider> roots)
    {
        var allTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var r in roots)
        {
            foreach (var cs in r.RootCallSites)
            {
                if (cs.Identity.Type is INamedTypeSymbol nts)
                {
                    allTypes.Add(nts);
                }
            }
        }

        // Group by raw base name
        var groups = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);
        foreach (var t in allTypes)
        {
            var baseName = BuildRawBaseName(t);
            if (!groups.TryGetValue(baseName, out var list))
            {
                list = new List<INamedTypeSymbol>();
                groups[baseName] = list;
            }
            list.Add(t);
        }

        var result = new Dictionary<INamedTypeSymbol, string>(SymbolEqualityComparer.Default);

        foreach (var kvp in groups)
        {
            var baseName = kvp.Key;
            var list = kvp.Value;

            if (list.Count == 1)
            {
                result[list[0]] = baseName;
            }
            else
            {
                var ordered = list
                    .OrderBy(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                    .ToList();

                for (int i = 0; i < ordered.Count; i++)
                {
                    result[ordered[i]] = baseName + "_" + i;
                }
            }
        }

        return result;
    }

    private static string BuildRawBaseName(INamedTypeSymbol symbol)
    {
        var sb = new StringBuilder();

        void Traverse(ITypeSymbol s)
        {
            sb.Append(s.Name);
            if (s is INamedTypeSymbol { IsGenericType: true } g)
            {
                sb.Append("_");
                foreach (var ta in g.TypeArguments)
                {
                    Traverse(ta);
                }
            }
        }

        Traverse(symbol);
        return sb.ToString();
    }

    private IEnumerable<IGrouping<ITypeSymbol, ServiceCallSite>> GroupNamedServices(ServiceProvider root)
    {
        return root.RootCallSites
            .Where(static s => s.Identity.IsMainNamedImplementation)
            .GroupBy<ServiceCallSite, ITypeSymbol>(static s => s.Identity.Type, SymbolEqualityComparer.Default);
    }

    private void WriteNamedServiceProvider(CodeWriter codeWriter, ServiceProvider root, Dictionary<INamedTypeSymbol, string> typeBaseNameMap)
    {
        foreach (var serviceGroup in GroupNamedServices(root))
        {
            var groupType = serviceGroup.Key;
            using (codeWriter.Scope($"{groupType} INamedServiceProvider<{groupType}>.GetService(string name)"))
            {
                using (codeWriter.Scope($"switch (name)"))
                {
                    foreach (var callSite in serviceGroup)
                    {
                        codeWriter.Append($"case \"{callSite.Identity.Name}\": return ");
                        WriteResolutionCall(codeWriter, callSite.Identity, "this", typeBaseNameMap);
                        codeWriter.Line($";");
                    }

                    codeWriter.Line($"default: throw CreateServiceNotFoundException<{groupType}>(name);");
                }
            }
            codeWriter.Line();
        }
    }

    private void WriteServiceProvider(CodeWriter codeWriter, ServiceProvider root, Dictionary<INamedTypeSymbol, string> typeBaseNameMap)
    {
        using (codeWriter.Scope($"{typeof(object)}? {typeof(IServiceProvider)}.GetService({typeof(Type)} type)"))
        {
            foreach (var rootRootCallSite in root.RootCallSites)
            {
                if (rootRootCallSite.Identity.IsMainImplementation)
                {
                    codeWriter.Append($"if (type == typeof({rootRootCallSite.Identity.Type})) return ");
                    WriteResolutionCall(codeWriter, rootRootCallSite.Identity, "this", typeBaseNameMap);
                    codeWriter.Line($";");
                }
            }

            codeWriter.Line($"return null;");
        }

        codeWriter.Line();

        WriteKeyedServiceProvider(codeWriter, root, typeBaseNameMap);
    }

    private void WriteKeyedServiceProvider(CodeWriter codeWriter, ServiceProvider root, Dictionary<INamedTypeSymbol, string> typeBaseNameMap)
    {
        var iface = root.KnownTypes.IKeyedServiceProviderType;
        if (iface == null)
        {
            return;
        }

        using (codeWriter.Scope($"{typeof(object)}? {iface}.GetKeyedService({typeof(Type)} type, object? key)"))
        {
            foreach (var serviceGroup in GroupNamedServices(root))
            {
                var serviceType = serviceGroup.Key;
                using (codeWriter.Scope($"if (type == typeof({serviceType}))"))
                {
                    using (codeWriter.Scope($"switch (key)"))
                    {
                        foreach (var callSite in serviceGroup)
                        {
                            codeWriter.Append($"case \"{callSite.Identity.Name}\": return ");
                            WriteResolutionCall(codeWriter, callSite.Identity, "this", typeBaseNameMap);
                            codeWriter.Line($";");
                        }
                    }
                }
            }

            codeWriter.Line($"return null;");
        }

        codeWriter.Line();

        codeWriter.Line(
            $"{typeof(object)} {iface}.GetRequiredKeyedService({typeof(Type)} type, object? key) => (({iface})this).GetKeyedService(type, key) ?? throw CreateServiceNotFoundException(type, key?.ToString());");

        codeWriter.Line();
    }

    private void WritePublicGetServiceMethods(CodeWriter codeWriter)
    {
        codeWriter.Line($"[DebuggerHidden]");
        codeWriter.Line($"public T GetService<T>() => this is IServiceProvider<T> provider ? provider.GetService() : throw CreateServiceNotFoundException<T>();");
        codeWriter.Line();

        codeWriter.Line($"[DebuggerHidden]");
        codeWriter.Line($"public T GetService<T>(string name) => this is INamedServiceProvider<T> provider ? provider.GetService(name) : throw CreateServiceNotFoundException<T>(name);");
        codeWriter.Line();
    }

    private void WriteDispose(CodeWriter codeWriter, ServiceProvider root, bool isScoped, Dictionary<INamedTypeSymbol, string> typeBaseNameMap)
    {
        codeWriter.Line($"private {typeof(List<object>)}? _disposables;");
        codeWriter.Line();

        using (codeWriter.Scope($"private void TryAddDisposable(object? value)"))
        {
            codeWriter.Append($"if (value is {typeof(IDisposable)}");
            if (root.KnownTypes.IAsyncDisposableType != null)
            {
                codeWriter.Append($" || value is {root.KnownTypes.IAsyncDisposableType}");
            }
            codeWriter.Line($")");
            using (codeWriter.Scope($"lock (this)"))
            {
                codeWriter.Line($"(_disposables ??= new {typeof(List<object>)}()).Add(value);");
            }
        }
        codeWriter.Line();

        using (codeWriter.Scope($"public void Dispose()"))
        {
            codeWriter.LineRaw("void TryDispose(object? value) => (value as IDisposable)?.Dispose();");
            codeWriter.Line();

            foreach (var rootService in root.RootCallSites)
            {
                if (rootService.IsDisposable == false ||
                    (rootService.Lifetime == ServiceLifetime.Singleton && isScoped) ||
                    (rootService.Lifetime == ServiceLifetime.Scoped && !isScoped) ||
                    rootService.Lifetime == ServiceLifetime.Transient) continue;

                codeWriter.Line($"TryDispose({GetCacheLocation(rootService.Identity, typeBaseNameMap)});");
            }

            if (!isScoped)
            {
                codeWriter.Line($"TryDispose(_rootScope);");
            }

            using (codeWriter.Scope($"if (_disposables != null)"))
            using (codeWriter.Scope($"foreach (var service in _disposables)"))
            {
                codeWriter.Line($"TryDispose(service);");
            }
        }

        codeWriter.Line();

        if (root.KnownTypes.IAsyncDisposableType != null)
        {
            using (codeWriter.Scope($"public async {typeof(ValueTask)} DisposeAsync()"))
            {
                using (codeWriter.Scope($"{typeof(ValueTask)} TryDispose(object? value)"))
                {
                    using (codeWriter.Scope($"if (value is System.IAsyncDisposable asyncDisposable)"))
                    {
                        codeWriter.Line($"return asyncDisposable.DisposeAsync();");
                    }
                    using (codeWriter.Scope($"else if (value is {typeof(IDisposable)} disposable)"))
                    {
                        codeWriter.Line($"disposable.Dispose();");
                    }
                    codeWriter.Line($"return default;");
                }
                codeWriter.Line();

                foreach (var rootService in root.RootCallSites)
                {
                    if (rootService.IsDisposable == false ||
                        (rootService.Lifetime == ServiceLifetime.Singleton && isScoped) ||
                        (rootService.Lifetime == ServiceLifetime.Scoped && !isScoped) ||
                        rootService.Lifetime == ServiceLifetime.Transient) continue;

                    codeWriter.Line($"await TryDispose({GetCacheLocation(rootService.Identity, typeBaseNameMap)});");
                }

                if (!isScoped)
                {
                    codeWriter.Line($"await TryDispose(_rootScope);");
                }

                using (codeWriter.Scope($"if (_disposables != null)"))
                using (codeWriter.Scope($"foreach (var service in _disposables)"))
                {
                    codeWriter.Line($"await TryDispose(service);");
                }
            }
        }

        codeWriter.Line();
    }

    private static void WriteInterfaces(CodeWriter codeWriter, ServiceProvider root, bool isScope)
    {
        codeWriter.Line($" : {typeof(IDisposable)},");

        if (root.KnownTypes.IAsyncDisposableType != null)
        {
            codeWriter.Line($"   {root.KnownTypes.IAsyncDisposableType},");
        }

        codeWriter.Line($"   {typeof(IServiceProvider)},");

        if (root.KnownTypes.IKeyedServiceProviderType != null)
        {
            codeWriter.Line($"   {root.KnownTypes.IKeyedServiceProviderType},");
        }

        if (!isScope && root.KnownTypes.IServiceScopeFactoryType != null)
        {
            codeWriter.Line($"   {root.KnownTypes.IServiceScopeFactoryType},");
        }

        if (!isScope && root.KnownTypes.IServiceProviderIsServiceType != null)
        {
            codeWriter.Line($"   {root.KnownTypes.IServiceProviderIsServiceType},");
        }

        if (isScope && root.KnownTypes.IServiceScopeType != null)
        {
            codeWriter.Line($"   {root.KnownTypes.IServiceScopeType},");
        }

        HashSet<ITypeSymbol> seenServices = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        HashSet<ITypeSymbol> seenNamedServices = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var serviceCallSite in root.RootCallSites)
        {
            if (serviceCallSite.Identity.Name == null)
            {
                if (seenServices.Add(serviceCallSite.Identity.Type))
                {
                    codeWriter.Line($"   IServiceProvider<{serviceCallSite.Identity.Type}>,");
                }
            }
            else
            {
                if (seenNamedServices.Add(serviceCallSite.Identity.Type))
                {
                    codeWriter.Line($"   INamedServiceProvider<{serviceCallSite.Identity.Type}>,");
                }
            }
        }

        codeWriter.RemoveTrailingComma();
        codeWriter.Line();
    }

    private void WriteCacheLocations(ServiceProvider root, CodeWriter codeWriter, bool isScope, Dictionary<INamedTypeSymbol, string> typeBaseNameMap)
    {
        foreach (var rootService in root.RootCallSites)
        {
            if ((rootService.Lifetime == ServiceLifetime.Singleton && isScope) ||
                (rootService.Lifetime == ServiceLifetime.Scoped && !isScope) ||
                rootService.Lifetime == ServiceLifetime.Transient) continue;

            codeWriter.Line($"private {rootService.ImplementationType}? {GetCacheLocation(rootService.Identity, typeBaseNameMap)};");
        }
        codeWriter.Line();
    }

    private string GetResolutionServiceName(ServiceIdentity identity, Dictionary<INamedTypeSymbol, string> typeBaseNameMap)
    {
        if (!identity.IsMainImplementation)
        {
            return $"Get{GetServiceExpandedName(identity, typeBaseNameMap)}";
        }

        throw new InvalidOperationException("Main implementation should be resolved via GetService<T> call");
    }

    private string GetCacheLocation(ServiceIdentity identity, Dictionary<INamedTypeSymbol, string> typeBaseNameMap)
    {
        return $"_{GetServiceExpandedName(identity, typeBaseNameMap)}";
    }

    private string GetServiceExpandedName(ServiceIdentity identity, Dictionary<INamedTypeSymbol, string> typeBaseNameMap)
    {
        var typeSymbol = (INamedTypeSymbol)identity.Type;
        string baseName;

        if (typeBaseNameMap.TryGetValue(typeSymbol, out var mapped))
        {
            baseName = mapped;
        }
        else
        {
            baseName = BuildRawBaseName(typeSymbol);
        }

        var builder = new StringBuilder(baseName);

        if (identity.Name != null)
        {
            builder.Append("_");
            builder.Append(identity.Name);
        }

        if (identity.ReverseIndex != null)
        {
            builder.Append("_");
            builder.Append(identity.ReverseIndex);
        }

        return builder.ToString();
    }

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationStartAction(compilationStartAnalysisContext =>
        {
            var syntaxCollector = new SyntaxCollector();
            compilationStartAnalysisContext.RegisterSyntaxNodeAction(analysisContext =>
            {
                syntaxCollector.OnVisitSyntaxNode(analysisContext.Node);
            }, SyntaxKind.ClassDeclaration, SyntaxKind.InterfaceDeclaration, SyntaxKind.InvocationExpression);

            compilationStartAnalysisContext.RegisterCompilationEndAction(compilationContext =>
            {
                Execute(new GeneratorContext(compilationContext, syntaxCollector));
            });
        });
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = new[]
    {
        DiagnosticDescriptors.UnexpectedErrorDescriptor,
        DiagnosticDescriptors.ServiceRequiredToConstructNotRegistered,
        DiagnosticDescriptors.MemberReferencedByInstanceOrFactoryAttributeNotFound,
        DiagnosticDescriptors.MemberReferencedByInstanceOrFactoryAttributeAmbiguous,
        DiagnosticDescriptors.ServiceProviderTypeHasToBePartial,
        DiagnosticDescriptors.ImportedTypeNotMarkedWithModuleAttribute,
        DiagnosticDescriptors.ImplementationTypeRequiresPublicConstructor,
        DiagnosticDescriptors.CyclicDependencyDetected,
        DiagnosticDescriptors.MissingServiceProviderAttribute,
        DiagnosticDescriptors.NoServiceTypeRegistered,
        DiagnosticDescriptors.ImplementationTypeAndFactoryNotAllowed,
        DiagnosticDescriptors.FactoryMemberMustBeAMethodOrHaveDelegateType,
        DiagnosticDescriptors.ServiceNameMustBeAlphanumeric,
        DiagnosticDescriptors.ImplicitIEnumerableNotNamed,
        DiagnosticDescriptors.BuiltInServicesAreNotNamed,
        DiagnosticDescriptors.NoServiceTypeAndNameRegistered,
        DiagnosticDescriptors.NamedServiceRequiredToConstructNotRegistered,
        DiagnosticDescriptors.OnlyStringKeysAreSupported,
        DiagnosticDescriptors.NullableServiceNotRegistered,
        DiagnosticDescriptors.NullableServiceRegistered,
    }.ToImmutableArray();

    private static string ReadAttributesFile()
    {
        using var manifestResourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Jab.Attributes.cs");
        Debug.Assert(manifestResourceStream != null);
        using var reader = new StreamReader(manifestResourceStream);
        return reader.ReadToEnd();
    }
}