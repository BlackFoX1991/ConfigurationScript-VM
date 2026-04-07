using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;

namespace CFGS_VM.Analytic.Semantics
{
    internal sealed class InterfaceCatalog
    {
        public void ValidateAllKnownInterfaces(Compiler compiler)
        {
            List<InterfaceDeclStmt> sortedInterfaces = OrderByInheritance(
                compiler,
                compiler.Context.QualifiedInterfaceDecls.Values.Distinct().ToList());

            foreach (InterfaceDeclStmt iface in sortedInterfaces)
            {
                foreach (string baseName in iface.BaseInterfaces)
                {
                    if (compiler.TryResolveClassDeclFromInterfaceBaseName(iface, baseName, out ClassDeclStmt classDecl))
                    {
                        throw new CompilerException(
                            $"invalid base type '{baseName}' in interface '{iface.Name}': '{classDecl.Name}' is a class",
                            iface.Line,
                            iface.Col,
                            iface.OriginFile);
                    }
                }

                _ = compiler.GetOrBuildInterfaceContract(iface);
            }
        }

        public List<InterfaceDeclStmt> OrderByInheritance(Compiler compiler, List<InterfaceDeclStmt> interfaceDecls)
        {
            HashSet<InterfaceDeclStmt> knownInterfaces = new(interfaceDecls);
            List<InterfaceDeclStmt> result = new();
            HashSet<InterfaceDeclStmt> permMark = new();
            HashSet<InterfaceDeclStmt> tempMark = new();

            void Visit(InterfaceDeclStmt iface)
            {
                if (permMark.Contains(iface))
                    return;

                if (tempMark.Contains(iface))
                {
                    throw new CompilerException(
                        $"cyclic inheritance involving interface '{iface.Name}'",
                        iface.Line,
                        iface.Col,
                        iface.OriginFile);
                }

                tempMark.Add(iface);

                foreach (string baseName in iface.BaseInterfaces)
                {
                    if (compiler.TryResolveBaseInterfaceDecl(iface, baseName, out InterfaceDeclStmt baseIface) &&
                        knownInterfaces.Contains(baseIface))
                    {
                        Visit(baseIface);
                    }
                }

                tempMark.Remove(iface);
                permMark.Add(iface);
                result.Add(iface);
            }

            foreach (InterfaceDeclStmt iface in interfaceDecls)
                Visit(iface);

            return result;
        }
    }
}
