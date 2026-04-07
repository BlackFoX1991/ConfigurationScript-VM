using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;

namespace CFGS_VM.Analytic.Semantics
{
    internal sealed class ClassCatalog
    {
        public void NormalizeInheritanceDeclarations(Compiler compiler, IEnumerable<ClassDeclStmt> classDecls)
        {
            foreach (ClassDeclStmt cls in classDecls)
            {
                List<string> normalizedInterfaces = new();
                HashSet<string> seenInterfaces = new(StringComparer.Ordinal);

                if (!string.IsNullOrWhiteSpace(cls.BaseName) &&
                    compiler.TryResolveInterfaceDecl(cls, cls.BaseName, out _))
                {
                    if (cls.BaseCtorArgs.Count > 0)
                    {
                        throw new CompilerException(
                            $"invalid inheritance list in class '{cls.Name}': interface '{cls.BaseName}' cannot receive constructor arguments",
                            cls.Line,
                            cls.Col,
                            cls.OriginFile);
                    }

                    normalizedInterfaces.Add(cls.BaseName);
                    seenInterfaces.Add(cls.BaseName);
                    cls.BaseName = null;
                    cls.BaseCtorArgs = new List<Expr>();
                }

                foreach (string ifaceName in cls.ImplementedInterfaces)
                {
                    if (!seenInterfaces.Add(ifaceName))
                    {
                        throw new CompilerException(
                            $"duplicate interface '{ifaceName}' in class '{cls.Name}'",
                            cls.Line,
                            cls.Col,
                            cls.OriginFile);
                    }

                    if (compiler.TryResolveClassDecl(cls, ifaceName, out _))
                    {
                        throw new CompilerException(
                            $"invalid inheritance list in class '{cls.Name}': base class '{ifaceName}' must appear before interfaces",
                            cls.Line,
                            cls.Col,
                            cls.OriginFile);
                    }

                    normalizedInterfaces.Add(ifaceName);
                }

                cls.ImplementedInterfaces = normalizedInterfaces;
            }
        }

        public List<ClassDeclStmt> OrderByInheritance(Compiler compiler, List<ClassDeclStmt> classDecls)
        {
            HashSet<ClassDeclStmt> knownClasses = new(classDecls);
            List<ClassDeclStmt> result = new();
            HashSet<ClassDeclStmt> permMark = new();
            HashSet<ClassDeclStmt> tempMark = new();

            void Visit(ClassDeclStmt cls)
            {
                if (permMark.Contains(cls))
                    return;

                if (tempMark.Contains(cls))
                {
                    throw new CompilerException(
                        $"cyclic inheritance involving class '{cls.Name}'",
                        cls.Line,
                        cls.Col,
                        cls.OriginFile);
                }

                tempMark.Add(cls);

                if (compiler.TryResolveBaseClassDecl(cls, out ClassDeclStmt baseDecl) && knownClasses.Contains(baseDecl))
                    Visit(baseDecl);

                tempMark.Remove(cls);
                permMark.Add(cls);
                result.Add(cls);
            }

            foreach (ClassDeclStmt cls in classDecls)
                Visit(cls);

            return result;
        }
    }
}
