using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Codegen;
using CFGS_VM.VMCore.Extensions;

namespace CFGS_VM.Analytic.Semantics
{
    internal sealed class TopLevelValidator
    {
        public void Validate(CompilationContext context)
        {
            ValidateReservedInterfaceDeclarations(context.QualifiedInterfaceDecls.Values.Distinct());
            ValidateReservedClassDeclarations(context.QualifiedClassDecls.Values.Distinct());
        }

        private static void ValidateReservedInterfaceDeclarations(IEnumerable<InterfaceDeclStmt> interfaceDecls)
        {
            foreach (InterfaceDeclStmt iface in interfaceDecls)
            {
                HashSet<string> seenMethods = new(StringComparer.Ordinal);
                HashSet<string> seenBases = new(StringComparer.Ordinal);

                foreach (string baseName in iface.BaseInterfaces)
                {
                    if (!seenBases.Add(baseName))
                    {
                        throw new CompilerException(
                            $"duplicate base interface '{baseName}' in interface '{iface.Name}'",
                            iface.Line,
                            iface.Col,
                            iface.OriginFile);
                    }
                }

                foreach (InterfaceMethodDecl method in iface.Methods)
                {
                    if (!seenMethods.Add(method.Name))
                    {
                        throw new CompilerException(
                            $"duplicate method '{method.Name}' in interface '{iface.Name}'",
                            method.Line,
                            method.Col,
                            method.OriginFile);
                    }

                    if (Compiler.IsReservedRuntimeMemberName(method.Name) || Compiler.IsReservedInternalMemberName(method.Name))
                    {
                        throw new CompilerException(
                            $"invalid member declaration '{method.Name}' in interface '{iface.Name}': reserved member name",
                            method.Line,
                            method.Col,
                            method.OriginFile);
                    }
                }
            }
        }

        private static void ValidateReservedClassDeclarations(IEnumerable<ClassDeclStmt> classDecls)
        {
            foreach (ClassDeclStmt cls in classDecls)
            {
                foreach (string parameter in cls.Parameters)
                {
                    if (Compiler.IsReservedRuntimeMemberName(parameter) || Compiler.IsReservedInternalMemberName(parameter))
                    {
                        throw new CompilerException(
                            $"invalid constructor parameter '{parameter}' in class '{cls.Name}': reserved member name",
                            cls.Line,
                            cls.Col,
                            cls.OriginFile);
                    }
                }

                foreach (string fieldName in cls.Fields.Keys)
                {
                    if (Compiler.IsReservedRuntimeMemberName(fieldName) || Compiler.IsReservedInternalMemberName(fieldName))
                    {
                        throw new CompilerException(
                            $"invalid member declaration '{fieldName}' in class '{cls.Name}': reserved member name",
                            cls.Line,
                            cls.Col,
                            cls.OriginFile);
                    }
                }

                foreach (string fieldName in cls.StaticFields.Keys)
                {
                    if (Compiler.IsReservedRuntimeMemberName(fieldName) || Compiler.IsReservedInternalMemberName(fieldName))
                    {
                        throw new CompilerException(
                            $"invalid member declaration '{fieldName}' in class '{cls.Name}': reserved member name",
                            cls.Line,
                            cls.Col,
                            cls.OriginFile);
                    }
                }

                foreach (FuncDeclStmt method in cls.Methods)
                {
                    if (Compiler.IsReservedRuntimeMemberName(method.Name) || Compiler.IsReservedInternalMemberName(method.Name))
                    {
                        throw new CompilerException(
                            $"invalid member declaration '{method.Name}' in class '{cls.Name}': reserved member name",
                            method.Line,
                            method.Col,
                            method.OriginFile);
                    }
                }

                foreach (FuncDeclStmt method in cls.StaticMethods)
                {
                    if (Compiler.IsReservedRuntimeMemberName(method.Name) || Compiler.IsReservedInternalMemberName(method.Name))
                    {
                        throw new CompilerException(
                            $"invalid member declaration '{method.Name}' in class '{cls.Name}': reserved member name",
                            method.Line,
                            method.Col,
                            method.OriginFile);
                    }
                }
            }
        }
    }
}
