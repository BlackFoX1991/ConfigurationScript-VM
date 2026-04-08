using CFGS_VM.Analytic.Semantics;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Extensions;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
        /// <summary>
        /// The TryResolveBaseClassDecl
        /// </summary>
        internal bool TryResolveBaseClassDecl(ClassDeclStmt decl, out ClassDeclStmt baseDecl)
        {
            baseDecl = null!;

            if (string.IsNullOrWhiteSpace(decl.BaseName))
                return false;

            return TryResolveClassDecl(decl, decl.BaseName, out baseDecl);
        }

        /// <summary>
        /// Enumerates containing scope prefixes from inner-most to outer-most.
        /// </summary>
        private static IEnumerable<string> EnumerateContainingScopes(string qualifiedPath)
        {
            string current = qualifiedPath;
            while (true)
            {
                int lastDot = current.LastIndexOf('.');
                if (lastDot <= 0)
                    yield break;

                current = current[..lastDot];
                yield return current;
            }
        }

        /// <summary>
        /// The TryResolveClassDecl
        /// </summary>
        internal bool TryResolveClassDecl(ClassDeclStmt context, string className, out ClassDeclStmt decl)
        {
            decl = null!;
            if (string.IsNullOrWhiteSpace(className))
                return false;

            if (className.Contains('.'))
                return _qualifiedClassDecls.TryGetValue(className, out decl!);

            if (_classQualifiedPaths.TryGetValue(context, out string? currentPath))
            {
                foreach (string scope in EnumerateContainingScopes(currentPath))
                {
                    string scopedPath = $"{scope}.{className}";
                    if (_qualifiedClassDecls.TryGetValue(scopedPath, out decl!))
                        return true;
                }
            }

            return _topLevelClassDecls.TryGetValue(className, out decl!);
        }

        /// <summary>
        /// The TryResolveBaseInterfaceDecl
        /// </summary>
        internal bool TryResolveBaseInterfaceDecl(InterfaceDeclStmt context, string interfaceName, out InterfaceDeclStmt decl)
        {
            decl = null!;
            if (string.IsNullOrWhiteSpace(interfaceName))
                return false;

            if (interfaceName.Contains('.'))
                return _qualifiedInterfaceDecls.TryGetValue(interfaceName, out decl!);

            if (_interfaceQualifiedPaths.TryGetValue(context, out string? currentPath))
            {
                foreach (string scope in EnumerateContainingScopes(currentPath))
                {
                    string scopedPath = $"{scope}.{interfaceName}";
                    if (_qualifiedInterfaceDecls.TryGetValue(scopedPath, out decl!))
                        return true;
                }
            }

            return _topLevelInterfaceDecls.TryGetValue(interfaceName, out decl!);
        }

        /// <summary>
        /// The TryResolveInterfaceDecl
        /// </summary>
        internal bool TryResolveInterfaceDecl(ClassDeclStmt context, string interfaceName, out InterfaceDeclStmt decl)
        {
            decl = null!;
            if (string.IsNullOrWhiteSpace(interfaceName))
                return false;

            if (interfaceName.Contains('.'))
                return _qualifiedInterfaceDecls.TryGetValue(interfaceName, out decl!);

            if (_classQualifiedPaths.TryGetValue(context, out string? currentPath))
            {
                foreach (string scope in EnumerateContainingScopes(currentPath))
                {
                    string scopedPath = $"{scope}.{interfaceName}";
                    if (_qualifiedInterfaceDecls.TryGetValue(scopedPath, out decl!))
                        return true;
                }
            }

            return _topLevelInterfaceDecls.TryGetValue(interfaceName, out decl!);
        }

        /// <summary>
        /// The TryResolveClassDeclFromInterfaceBaseName
        /// </summary>
        internal bool TryResolveClassDeclFromInterfaceBaseName(InterfaceDeclStmt context, string className, out ClassDeclStmt decl)
        {
            decl = null!;
            if (string.IsNullOrWhiteSpace(className))
                return false;

            if (className.Contains('.'))
                return _qualifiedClassDecls.TryGetValue(className, out decl!);

            if (_interfaceQualifiedPaths.TryGetValue(context, out string? currentPath))
            {
                foreach (string scope in EnumerateContainingScopes(currentPath))
                {
                    string scopedPath = $"{scope}.{className}";
                    if (_qualifiedClassDecls.TryGetValue(scopedPath, out decl!))
                        return true;
                }
            }

            return _topLevelClassDecls.TryGetValue(className, out decl!);
        }

        /// <summary>
        /// The GetOrBuildInterfaceContract
        /// </summary>
        internal Dictionary<string, InterfaceMethodDecl> GetOrBuildInterfaceContract(InterfaceDeclStmt iface)
        {
            if (_interfaceContractCache.TryGetValue(iface, out Dictionary<string, InterfaceMethodDecl>? cached))
                return cached;

            Dictionary<string, InterfaceMethodDecl> contract = new(StringComparer.Ordinal);

            foreach (string baseName in iface.BaseInterfaces)
            {
                if (!TryResolveBaseInterfaceDecl(iface, baseName, out InterfaceDeclStmt baseIface))
                    continue;

                Dictionary<string, InterfaceMethodDecl> baseContract = GetOrBuildInterfaceContract(baseIface);
                foreach (KeyValuePair<string, InterfaceMethodDecl> kv in baseContract)
                {
                    if (contract.TryGetValue(kv.Key, out InterfaceMethodDecl? existing) &&
                        !MethodShapeRules.HaveCompatibleShapes(existing, kv.Value))
                    {
                        throw new CompilerException(
                            $"incompatible inherited method '{kv.Key}' in interface '{iface.Name}': expected arity {MethodShapeRules.DescribeArity(existing)}, got {MethodShapeRules.DescribeArity(kv.Value)}",
                            kv.Value.Line, kv.Value.Col, kv.Value.OriginFile);
                    }

                    contract[kv.Key] = kv.Value;
                }
            }

            foreach (InterfaceMethodDecl method in iface.Methods)
            {
                if (contract.TryGetValue(method.Name, out InterfaceMethodDecl? inherited) &&
                    !MethodShapeRules.HaveCompatibleShapes(inherited, method))
                {
                    throw new CompilerException(
                        $"incompatible method '{method.Name}' in interface '{iface.Name}': expected arity {MethodShapeRules.DescribeArity(inherited)}, got {MethodShapeRules.DescribeArity(method)}",
                        method.Line, method.Col, method.OriginFile);
                }

                contract[method.Name] = method;
            }

            _interfaceContractCache[iface] = contract;
            return contract;
        }
    }
}
