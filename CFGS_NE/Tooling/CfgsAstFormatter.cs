using System.Globalization;
using System.Text;
using CFGS_VM.Analytic.Core;
using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.Analytic.Tokens;

namespace CFGS_VM.Tooling;

internal static class CfgsAstFormatter
{
    public static string? TryFormat(string sourceText, CfgsFormatOptions options)
    {
        if (ContainsLoweredScopeSugar(sourceText))
            return null;

        try
        {
            Lexer lexer = new("<formatter>", sourceText);
            Parser parser = new(lexer, Parser.TopLevelMode.Script);
            List<Stmt> statements = parser.Parse();
            return new Writer(options, sourceText, parser.Tokens).Format(statements);
        }
        catch (Exception ex) when (ex is ParserException or LexerException or CfgsAstFormatUnsupportedException)
        {
            return null;
        }
    }

    private static bool ContainsComments(string sourceText)
    {
        bool inString = false;
        bool inChar = false;
        bool escape = false;

        for (int i = 0; i < sourceText.Length; i++)
        {
            char ch = sourceText[i];

            if (inString || inChar)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escape = true;
                    continue;
                }

                if (inString && ch == '"')
                    inString = false;
                else if (inChar && ch == '\'')
                    inChar = false;

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '\'')
            {
                inChar = true;
                continue;
            }

            if (ch == '#')
                return true;
        }

        return false;
    }

    private static bool ContainsLoweredScopeSugar(string sourceText)
    {
        int index = 0;
        while (index < sourceText.Length)
        {
            if (!TryReadIdentifier(sourceText, ref index, out string? identifier))
            {
                index++;
                continue;
            }

            if (string.Equals(identifier, "defer", StringComparison.Ordinal))
                return true;

            if (!string.Equals(identifier, "using", StringComparison.Ordinal))
                continue;

            int probe = index;
            while (probe < sourceText.Length && char.IsWhiteSpace(sourceText[probe]))
                probe++;

            if (probe < sourceText.Length && sourceText[probe] == '(')
                continue;

            if (TryReadIdentifier(sourceText, ref probe, out string? usingModifier) &&
                (string.Equals(usingModifier, "var", StringComparison.Ordinal) ||
                 string.Equals(usingModifier, "const", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadIdentifier(string text, ref int index, out string? identifier)
    {
        identifier = null;
        if (index < 0 || index >= text.Length || !IsIdentifierStart(text[index]))
            return false;

        int start = index;
        index++;
        while (index < text.Length && IsIdentifierPart(text[index]))
            index++;

        identifier = text[start..index];
        return true;
    }

    private static bool IsIdentifierStart(char ch)
        => char.IsLetter(ch) || ch == '_';

    private static bool IsIdentifierPart(char ch)
        => char.IsLetterOrDigit(ch) || ch == '_';

    private sealed class Writer
    {
        private readonly StringBuilder _sb = new();
        private readonly string _indentUnit;
        private readonly int _maxLineWidth;
        private readonly string _sourceText;
        private readonly IReadOnlyList<Token> _tokens;
        private int _indentLevel;

        public Writer(CfgsFormatOptions options, string sourceText, IReadOnlyList<Token> tokens)
        {
            _indentUnit = options.UseTabs
                ? "\t"
                : new string(' ', Math.Max(1, options.IndentSize));
            _maxLineWidth = options.MaxLineWidth > 0 ? options.MaxLineWidth : int.MaxValue;
            _sourceText = sourceText;
            _tokens = tokens;
        }

        public string Format(IReadOnlyList<Stmt> statements)
        {
            if (statements.Count == 0)
            {
                WriteStandaloneGap(0, _sourceText.Length);
                return _sb.ToString();
            }

            WriteNodeList(statements, 0, _sourceText.Length, WriteStatement);

            return _sb.ToString();
        }

        private void WriteStatement(Stmt stmt)
        {
            switch (stmt)
            {
                case EmptyStmt:
                    WriteIndentedLine(";");
                    break;

                case ErrorStmt:
                    throw new CfgsAstFormatUnsupportedException();

                case BlockStmt block:
                    WriteIndent();
                    WriteBlock(block);
                    AppendLine();
                    break;

                case VarDecl varDecl:
                    if (varDecl.Value is null)
                        WriteIndentedLine($"var {varDecl.Name};");
                    else
                        WriteExpressionStatement($"var {varDecl.Name} = ", varDecl.Value);
                    break;

                case ConstDecl constDecl:
                    WriteExpressionStatement($"const {constDecl.Name} = ", constDecl.Value);
                    break;

                case DestructureDeclStmt destructureDecl:
                    WriteExpressionStatement($"{(destructureDecl.IsConst ? "const" : "var")} {FormatPattern(destructureDecl.Pattern)} = ", destructureDecl.Value);
                    break;

                case DestructureAssignStmt destructureAssign:
                    WriteExpressionStatement($"{FormatPattern(destructureAssign.Pattern)} = ", destructureAssign.Value);
                    break;

                case ExportStmt exportStmt:
                    WriteExport(exportStmt);
                    break;

                case AssignStmt assignStmt:
                    WriteExpressionStatement($"{assignStmt.Name} = ", assignStmt.Value);
                    break;

                case AssignIndexExprStmt assignIndex:
                    WriteExpressionStatement($"{FormatExpr(assignIndex.Target)} = ", assignIndex.Value);
                    break;

                case AssignExprStmt assignExpr:
                    WriteExpressionStatement($"{FormatExpr(assignExpr.Target)} = ", assignExpr.Value);
                    break;

                case CompoundAssignStmt compoundAssign:
                    WriteExpressionStatement($"{FormatExpr(compoundAssign.Target)} {MapOperator(compoundAssign.Op)} ", compoundAssign.Value);
                    break;

                case PushStmt pushStmt:
                    WriteExpressionStatement($"{FormatExpr(pushStmt.Target)}[] = ", pushStmt.Value);
                    break;

                case DeleteVarStmt deleteVar:
                    WriteIndentedLine($"delete {deleteVar.Name};");
                    break;

                case DeleteIndexStmt deleteIndex:
                    WriteIndentedLine($"delete {deleteIndex.Name}[{FormatExpr(deleteIndex.Index)}];");
                    break;

                case DeleteAllStmt deleteAll:
                    WriteIndentedLine($"delete {FormatExpr(deleteAll.Target)}[];");
                    break;

                case DeleteExprStmt deleteExpr:
                    WriteDelete(deleteExpr);
                    break;

                case FuncDeclStmt funcDecl:
                    WriteFunctionDeclaration(funcDecl, exported: false);
                    break;

                case ReturnStmt returnStmt:
                    if (returnStmt.Value is null)
                        WriteIndentedLine("return;");
                    else
                        WriteExpressionStatement("return ", returnStmt.Value);
                    break;

                case IfStmt ifStmt:
                    WriteIf(ifStmt);
                    break;

                case WhileStmt whileStmt:
                    WriteLoop("while", whileStmt.Condition, whileStmt.Body);
                    break;

                case DoWhileStmt doWhileStmt:
                    WriteDoWhile(doWhileStmt);
                    break;

                case ForStmt forStmt:
                    WriteFor(forStmt);
                    break;

                case ForeachStmt foreachStmt:
                    WriteForeach(foreachStmt);
                    break;

                case BreakStmt:
                    WriteIndentedLine("break;");
                    break;

                case ContinueStmt:
                    WriteIndentedLine("continue;");
                    break;

                case MatchStmt matchStmt:
                    WriteMatch(matchStmt);
                    break;

                case TryStmt tryStmt:
                    WriteTry(tryStmt);
                    break;

                case UsingStmt usingStmt:
                    WriteUsing(usingStmt);
                    break;

                case ThrowStmt throwStmt:
                    WriteExpressionStatement("throw ", throwStmt.Value);
                    break;

                case YieldStmt:
                    WriteIndentedLine("yield;");
                    break;

                case BareImportSyntaxStmt bareImport:
                    WriteIndentedLine($"import {FormatStringLiteral(bareImport.Path)};");
                    break;

                case NamespaceImportSyntaxStmt namespaceImport:
                    WriteIndentedLine($"import * as {namespaceImport.Alias} from {FormatStringLiteral(namespaceImport.Path)};");
                    break;

                case NamedImportSyntaxStmt namedImport:
                    WriteNamedImport(namedImport);
                    break;

                case DefaultImportSyntaxStmt defaultImport:
                    WriteIndentedLine($"import {defaultImport.ImportName} from {FormatStringLiteral(defaultImport.Path)};");
                    break;

                case NativeImportSyntaxStmt nativeImport:
                    WriteIndentedLine($"import native {FormatStringLiteral(nativeImport.Path)} as {nativeImport.Alias};");
                    break;

                case UseNamespaceStmt useNamespace:
                    WriteIndentedLine($"use {useNamespace.QualifiedPath};");
                    break;

                case NamespaceDeclStmt namespaceDecl:
                    WriteNamespace(namespaceDecl);
                    break;

                case ClassDeclStmt classDecl:
                    WriteClassDeclaration(classDecl, exported: false);
                    break;

                case InterfaceDeclStmt interfaceDecl:
                    WriteInterfaceDeclaration(interfaceDecl, exported: false);
                    break;

                case EnumDeclStmt enumDecl:
                    WriteEnumDeclaration(enumDecl, exported: false);
                    break;

                case NamespaceImportAliasStmt:
                case ImportAliasDeclStmt:
                    throw new CfgsAstFormatUnsupportedException();

                case SetFieldStmt setField:
                    WriteExpressionStatement($"{FormatExpr(setField.Target)}.{setField.Field} = ", setField.Value);
                    break;

                case ExprStmt exprStmt:
                    WriteExpressionStatement(string.Empty, exprStmt.Expression);
                    break;

                default:
                    throw new CfgsAstFormatUnsupportedException();
            }
        }

        private void WriteExport(ExportStmt exportStmt)
        {
            switch (exportStmt.Inner)
            {
                case FuncDeclStmt funcDecl:
                    WriteFunctionDeclaration(funcDecl, exported: true);
                    break;

                case VarDecl varDecl:
                    if (varDecl.Value is null)
                        WriteIndentedLine($"export var {varDecl.Name};");
                    else
                        WriteExpressionStatement($"export var {varDecl.Name} = ", varDecl.Value);
                    break;

                case ConstDecl constDecl:
                    WriteExpressionStatement("export const " + constDecl.Name + " = ", constDecl.Value);
                    break;

                case ClassDeclStmt classDecl:
                    WriteClassDeclaration(classDecl, exported: true);
                    break;

                case InterfaceDeclStmt interfaceDecl:
                    WriteInterfaceDeclaration(interfaceDecl, exported: true);
                    break;

                case EnumDeclStmt enumDecl:
                    WriteEnumDeclaration(enumDecl, exported: true);
                    break;

                default:
                    throw new CfgsAstFormatUnsupportedException();
            }
        }

        private void WriteFunctionDeclaration(FuncDeclStmt funcDecl, bool exported, bool includeMemberModifiers = false)
        {
            WriteIndent();
            if (exported)
                _sb.Append("export ");
            if (includeMemberModifiers)
            {
                WriteVisibilityPrefix(funcDecl.Visibility, funcDecl.HasExplicitVisibility);
                if (funcDecl.IsStaticMember)
                    _sb.Append("static ");
            }
            if (funcDecl.IsAsync)
                _sb.Append("async ");
            _sb.Append("func ");
            if (!string.IsNullOrWhiteSpace(funcDecl.ExplicitInterfaceName))
            {
                _sb.Append(funcDecl.ExplicitInterfaceName);
                _sb.Append('.');
            }
            _sb.Append(funcDecl.Name);
            _sb.Append('(');
            _sb.Append(FormatParameters(funcDecl.ParameterSpecs, funcDecl.Parameters, funcDecl.RestParameter));
            _sb.Append(')');
            WriteEmbeddedBody(funcDecl.Body);
            AppendLine();
        }

        private void WriteClassDeclaration(ClassDeclStmt classDecl, bool exported, bool includeNamespaceVisibility = false)
        {
            if (classDecl.SyntaxMembers.Count == 0 &&
                (classDecl.Fields.Count > 0 ||
                 classDecl.StaticFields.Count > 0 ||
                 classDecl.Methods.Count > 0 ||
                 classDecl.StaticMethods.Count > 0 ||
                 classDecl.Properties.Count > 0 ||
                 classDecl.StaticProperties.Count > 0 ||
                 classDecl.Enums.Count > 0 ||
                 classDecl.NestedClasses.Count > 0))
            {
                throw new CfgsAstFormatUnsupportedException();
            }

            WriteIndent();
            if (exported)
                _sb.Append("export ");
            if (classDecl.IsNested || includeNamespaceVisibility)
                WriteVisibilityPrefix(classDecl.Visibility, classDecl.HasExplicitVisibility);

            _sb.Append("class ");
            _sb.Append(classDecl.Name);
            _sb.Append('(');
            _sb.Append(FormatParameters(classDecl.ConstructorParameterSpecs, classDecl.Parameters, classDecl.ConstructorRestParameter));
            _sb.Append(')');

            List<string> inheritanceParts = new();
            if (!string.IsNullOrWhiteSpace(classDecl.BaseName))
            {
                string baseDecl = classDecl.BaseName!;
                if (classDecl.BaseCtorArgs.Count > 0)
                    baseDecl += $"({string.Join(", ", classDecl.BaseCtorArgs.Select(arg => FormatExpr(arg)))})";
                inheritanceParts.Add(baseDecl);
            }

            inheritanceParts.AddRange(classDecl.ImplementedInterfaces);
            if (inheritanceParts.Count > 0)
            {
                _sb.Append(" : ");
                _sb.Append(string.Join(", ", inheritanceParts));
            }

            _sb.Append('{');
            AppendLine();
            _indentLevel++;
            WriteNodeList(classDecl.SyntaxMembers, GetDelimitedBodyStartOffset(classDecl), GetDelimitedBodyEndOffset(classDecl), WriteClassMember, skipInitialRenderedLineBreak: true);
            _indentLevel--;
            WriteIndent();
            _sb.Append('}');
            AppendLine();
        }

        private void WriteClassMember(Node member)
        {
            switch (member)
            {
                case ClassFieldDeclSyntax field:
                    WriteClassField(field);
                    break;
                case PropertyDeclStmt property:
                    WritePropertyDeclaration(property);
                    break;
                case FuncDeclStmt funcDecl:
                    WriteFunctionDeclaration(funcDecl, exported: false, includeMemberModifiers: true);
                    break;
                case EnumDeclStmt enumDecl:
                    WriteEnumDeclaration(enumDecl, exported: false);
                    break;
                case ClassDeclStmt classDecl:
                    WriteClassDeclaration(classDecl, exported: false);
                    break;
                default:
                    throw new CfgsAstFormatUnsupportedException();
            }
        }

        private void WriteClassField(ClassFieldDeclSyntax field)
        {
            WriteIndent();
            WriteVisibilityPrefix(field.Visibility, field.HasExplicitVisibility);
            if (field.IsStatic)
                _sb.Append("static ");
            _sb.Append(field.IsConst ? "const " : "var ");
            _sb.Append(field.Name);
            if (field.Initializer is not null)
            {
                _sb.Append(" = ");
                _sb.Append(FormatExpr(field.Initializer));
            }
            _sb.Append(';');
            AppendLine();
        }

        private void WriteInterfaceDeclaration(InterfaceDeclStmt interfaceDecl, bool exported, bool includeNamespaceVisibility = false)
        {
            if (interfaceDecl.SyntaxMembers.Count == 0 &&
                (interfaceDecl.Methods.Count > 0 || interfaceDecl.Properties.Count > 0 || interfaceDecl.Indexers.Count > 0))
            {
                throw new CfgsAstFormatUnsupportedException();
            }

            WriteIndent();
            if (exported)
                _sb.Append("export ");
            if (includeNamespaceVisibility)
                WriteVisibilityPrefix(interfaceDecl.Visibility, interfaceDecl.HasExplicitVisibility);
            _sb.Append("interface ");
            _sb.Append(interfaceDecl.Name);
            if (interfaceDecl.BaseInterfaces.Count > 0)
            {
                _sb.Append(" : ");
                _sb.Append(string.Join(", ", interfaceDecl.BaseInterfaces));
            }

            _sb.Append('{');
            AppendLine();
            _indentLevel++;
            WriteNodeList(interfaceDecl.SyntaxMembers, GetDelimitedBodyStartOffset(interfaceDecl), GetDelimitedBodyEndOffset(interfaceDecl), WriteInterfaceMember, skipInitialRenderedLineBreak: true);
            _indentLevel--;
            WriteIndent();
            _sb.Append('}');
            AppendLine();
        }

        private void WriteInterfaceMember(Node member)
        {
            switch (member)
            {
                case InterfacePropertyDecl property:
                    WriteInterfaceProperty(property);
                    break;
                case InterfaceIndexerDecl indexer:
                    WriteInterfaceIndexer(indexer);
                    break;
                case InterfaceMethodDecl method:
                    WriteInterfaceMethod(method);
                    break;
                default:
                    throw new CfgsAstFormatUnsupportedException();
            }
        }

        private void WriteInterfaceProperty(InterfacePropertyDecl property)
        {
            WriteIndent();
            if (property.IsStatic)
                _sb.Append("static ");
            _sb.Append("property ");
            _sb.Append(property.Name);
            _sb.Append(" { ");
            _sb.Append(string.Join(" ", FormatInterfacePropertyAccessors(property)));
            _sb.Append(" }");
            AppendLine();
        }

        private void WriteInterfaceIndexer(InterfaceIndexerDecl indexer)
        {
            WriteIndent();
            _sb.Append("index ");
            _sb.Append(indexer.IsSetter ? "set" : "get");
            _sb.Append('(');
            _sb.Append(FormatParameters([], indexer.Parameters, indexer.RestParameter));
            _sb.Append(");");
            AppendLine();
        }

        private void WriteInterfaceMethod(InterfaceMethodDecl method)
        {
            WriteIndent();
            if (method.IsStatic)
                _sb.Append("static ");
            if (method.IsAsync)
                _sb.Append("async ");
            _sb.Append("func ");
            _sb.Append(method.Name);
            _sb.Append('(');
            _sb.Append(FormatParameters(method.ParameterSpecs, method.Parameters, method.RestParameter));
            _sb.Append(')');

            if (method.Body is null)
            {
                _sb.Append(';');
                AppendLine();
                return;
            }

            WriteEmbeddedBody(method.Body);
            AppendLine();
        }

        private void WritePropertyDeclaration(PropertyDeclStmt property)
        {
            bool inlineAccessors = property.Accessors.All(accessor => accessor.IsAuto);

            WriteIndent();
            WriteVisibilityPrefix(property.Visibility, property.HasExplicitVisibility);
            if (property.IsStatic)
                _sb.Append("static ");
            _sb.Append("property ");
            _sb.Append(property.Name);

            if (inlineAccessors)
            {
                _sb.Append(" { ");
                _sb.Append(string.Join(" ", property.Accessors.Select(FormatInlinePropertyAccessor)));
                _sb.Append(" }");
                if (property.Initializer is not null)
                {
                    _sb.Append(" = ");
                    _sb.Append(FormatExpr(property.Initializer));
                    _sb.Append(';');
                }
                AppendLine();
                return;
            }

            _sb.Append('{');
            AppendLine();
            _indentLevel++;
            WriteNodeList(property.Accessors, GetDelimitedBodyStartOffset(property), GetDelimitedBodyEndOffset(property), WritePropertyAccessor, skipInitialRenderedLineBreak: true);
            _indentLevel--;
            WriteIndent();
            _sb.Append('}');
            if (property.Initializer is not null)
            {
                _sb.Append(" = ");
                _sb.Append(FormatExpr(property.Initializer));
                _sb.Append(';');
            }
            AppendLine();
        }

        private void WritePropertyAccessor(PropertyAccessorDecl accessor)
        {
            WriteIndent();
            if (accessor.HasExplicitVisibility)
            {
                _sb.Append(FormatVisibilityKeyword(accessor.Visibility));
                _sb.Append(' ');
            }
            _sb.Append(FormatPropertyAccessorKeyword(accessor.Kind));
            if (accessor.Kind != PropertyAccessorKind.Get &&
                !string.IsNullOrWhiteSpace(accessor.ValueParameterName))
            {
                _sb.Append('(');
                _sb.Append(accessor.ValueParameterName);
                _sb.Append(')');
            }

            if (accessor.IsAuto)
            {
                _sb.Append(';');
                AppendLine();
                return;
            }

            if (accessor.Body is null)
                throw new CfgsAstFormatUnsupportedException();

            WriteEmbeddedBody(accessor.Body);
            AppendLine();
        }

        private void WriteEnumDeclaration(EnumDeclStmt enumDecl, bool exported, bool includeVisibility = false)
        {
            WriteIndent();
            if (exported)
                _sb.Append("export ");
            if (!exported && includeVisibility)
                WriteVisibilityPrefix(enumDecl.Visibility, enumDecl.HasExplicitVisibility);
            else if (!exported)
                WriteVisibilityPrefix(enumDecl.Visibility, enumDecl.HasExplicitVisibility);
            _sb.Append("enum ");
            _sb.Append(enumDecl.Name);
            _sb.Append('{');
            AppendLine();

            _indentLevel++;
            WriteNodeList(enumDecl.Members, GetDelimitedBodyStartOffset(enumDecl), GetDelimitedBodyEndOffset(enumDecl), member => WriteEnumMember(enumDecl.Members, member), skipInitialRenderedLineBreak: true);
            _indentLevel--;
            WriteIndent();
            _sb.Append('}');
            AppendLine();
        }

        private void WriteEnumMember(IReadOnlyList<EnumMemberNode> members, EnumMemberNode member)
        {
            int index = -1;
            for (int i = 0; i < members.Count; i++)
            {
                if (ReferenceEquals(members[i], member))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
                throw new CfgsAstFormatUnsupportedException();

            WriteIndent();
            _sb.Append(member.Name);
            if (ShouldWriteEnumMemberValue(members, index))
            {
                _sb.Append(" = ");
                _sb.Append(FormatNumber(member.Value));
            }
            if (index < members.Count - 1)
                _sb.Append(',');
            AppendLine();
        }

        private void WriteIf(IfStmt ifStmt)
        {
            WriteIndent();
            WriteInlineExpression("if(", ifStmt.Condition, ")");
            WriteEmbeddedBody(ifStmt.ThenBlock);

            if (ifStmt.ElseBranch is null)
            {
                AppendLine();
                return;
            }

            if (ifStmt.ThenBlock is BlockStmt)
            {
                _sb.Append(" else");
            }
            else
            {
                AppendLine();
                WriteIndent();
                _sb.Append("else");
            }

            if (ifStmt.ElseBranch is IfStmt elseIf)
            {
                _sb.Append(' ');
                WriteIfInline(elseIf);
                AppendLine();
                return;
            }

            WriteEmbeddedBody(ifStmt.ElseBranch);
            AppendLine();
        }

        private void WriteIfInline(IfStmt ifStmt)
        {
            WriteInlineExpression("if(", ifStmt.Condition, ")");
            WriteEmbeddedBody(ifStmt.ThenBlock);

            if (ifStmt.ElseBranch is null)
                return;

            if (ifStmt.ThenBlock is BlockStmt)
            {
                _sb.Append(" else");
            }
            else
            {
                AppendLine();
                WriteIndent();
                _sb.Append("else");
            }

            if (ifStmt.ElseBranch is IfStmt nested)
            {
                _sb.Append(' ');
                WriteIfInline(nested);
                return;
            }

            WriteEmbeddedBody(ifStmt.ElseBranch);
        }

        private void WriteLoop(string keyword, Expr condition, Stmt body)
        {
            WriteIndent();
            WriteInlineExpression($"{keyword}(", condition, ")");
            WriteEmbeddedBody(body);
            AppendLine();
        }

        private void WriteDoWhile(DoWhileStmt doWhileStmt)
        {
            WriteIndent();
            _sb.Append("do");
            WriteEmbeddedBody(doWhileStmt.Body);
            if (doWhileStmt.Body is BlockStmt)
                _sb.Append(' ');
            else
                WriteIndent();
            WriteInlineExpression("while(", doWhileStmt.Condition, ");");
            AppendLine();
        }

        private void WriteFor(ForStmt forStmt)
        {
            WriteIndent();
            _sb.Append("for(");
            _sb.Append(FormatForClause(forStmt.Init));
            _sb.Append("; ");
            _sb.Append(forStmt.Condition is null ? string.Empty : FormatExpr(forStmt.Condition));
            _sb.Append("; ");
            _sb.Append(FormatForClause(forStmt.Increment));
            _sb.Append(')');
            WriteEmbeddedBody(forStmt.Body);
            AppendLine();
        }

        private void WriteForeach(ForeachStmt foreachStmt)
        {
            WriteIndent();
            string prefix = "foreach(";
            if (foreachStmt.DeclareLocal)
                prefix += "var ";
            prefix += $"{FormatForeachTarget(foreachStmt)} in ";
            WriteInlineExpression(prefix, foreachStmt.Iterable, ")");
            WriteEmbeddedBody(foreachStmt.Body);
            AppendLine();
        }

        private void WriteMatch(MatchStmt matchStmt)
        {
            WriteIndent();
            _sb.Append("match(");
            _sb.Append(FormatExpr(matchStmt.Expression));
            _sb.Append(')');
            _sb.Append('{');
            AppendLine();

            _indentLevel++;
            foreach (CaseClause caseClause in matchStmt.Cases)
            {
                WriteIndent();
                _sb.Append("case ");
                _sb.Append(FormatPattern(caseClause.Pattern));
                if (caseClause.Guard is not null)
                {
                    _sb.Append(" if ");
                    _sb.Append(FormatExpr(caseClause.Guard));
                }
                _sb.Append(':');
                WriteEmbeddedBody(caseClause.Body);
                AppendLine();
            }

            if (matchStmt.DefaultCase is not null)
            {
                WriteIndent();
                _sb.Append("default:");
                WriteEmbeddedBody(matchStmt.DefaultCase);
                AppendLine();
            }

            _indentLevel--;
            WriteIndent();
            _sb.Append('}');
            AppendLine();
        }

        private void WriteTry(TryStmt tryStmt)
        {
            WriteIndent();
            _sb.Append("try");
            WriteEmbeddedBody(tryStmt.TryBlock);

            if (tryStmt.CatchBlock is not null)
            {
                _sb.Append(" catch");
                if (!string.IsNullOrWhiteSpace(tryStmt.CatchIdent))
                {
                    _sb.Append('(');
                    _sb.Append(tryStmt.CatchIdent);
                    _sb.Append(')');
                }
                WriteEmbeddedBody(tryStmt.CatchBlock);
            }

            if (tryStmt.FinallyBlock is not null)
            {
                _sb.Append(" finally");
                WriteEmbeddedBody(tryStmt.FinallyBlock);
            }

            AppendLine();
        }

        private void WriteUsing(UsingStmt usingStmt)
        {
            WriteIndent();
            string prefix = "using(";
            if (!string.IsNullOrWhiteSpace(usingStmt.BindingName))
            {
                prefix += usingStmt.BindingIsConst ? "const " : "var ";
                prefix += usingStmt.BindingName;
                prefix += " = ";
            }
            WriteInlineExpression(prefix, usingStmt.Resource, ")");
            WriteEmbeddedBody(usingStmt.Body);
            AppendLine();
        }

        private void WriteNamedImport(NamedImportSyntaxStmt namedImport)
        {
            WriteIndent();
            _sb.Append("import { ");
            for (int i = 0; i < namedImport.Imports.Count; i++)
            {
                if (i > 0)
                    _sb.Append(", ");

                ImportBindingSpec binding = namedImport.Imports[i];
                _sb.Append(binding.ImportName);
                if (!string.Equals(binding.ImportName, binding.LocalName, StringComparison.Ordinal))
                {
                    _sb.Append(" as ");
                    _sb.Append(binding.LocalName);
                }
            }
            _sb.Append(" } from ");
            _sb.Append(FormatStringLiteral(namedImport.Path));
            _sb.Append(';');
            AppendLine();
        }

        private void WriteNamespace(NamespaceDeclStmt namespaceDecl)
        {
            WriteIndent();
            _sb.Append("namespace ");
            _sb.Append(string.Join(".", namespaceDecl.Parts));

            if (namespaceDecl.IsFileScoped)
            {
                _sb.Append(';');
                AppendLine();
                WriteNodeList(namespaceDecl.BodyStatements, GetNodeEndOffset(namespaceDecl), _sourceText.Length, WriteNamespaceMember, skipInitialRenderedLineBreak: true);
                return;
            }

            _sb.Append('{');
            AppendLine();
            _indentLevel++;
            WriteNodeList(namespaceDecl.BodyStatements, GetDelimitedBodyStartOffset(namespaceDecl), GetDelimitedBodyEndOffset(namespaceDecl), WriteNamespaceMember, skipInitialRenderedLineBreak: true);
            _indentLevel--;
            WriteIndent();
            _sb.Append('}');
            AppendLine();
        }

        private void WriteNamespaceMember(Stmt stmt)
        {
            switch (stmt)
            {
                case FuncDeclStmt funcDecl:
                    WriteFunctionDeclaration(funcDecl, exported: false, includeMemberModifiers: funcDecl.HasExplicitVisibility);
                    return;

                case VarDecl varDecl:
                    WriteIndent();
                    WriteVisibilityPrefix(varDecl.Visibility, varDecl.HasExplicitVisibility);
                    if (varDecl.Value is null)
                    {
                        _sb.Append("var ");
                        _sb.Append(varDecl.Name);
                        _sb.Append(';');
                        AppendLine();
                    }
                    else
                    {
                        WriteInlineExpression($"var {varDecl.Name} = ", varDecl.Value, ";");
                        AppendLine();
                    }
                    return;

                case ConstDecl constDecl:
                    WriteIndent();
                    WriteVisibilityPrefix(constDecl.Visibility, constDecl.HasExplicitVisibility);
                    WriteInlineExpression($"const {constDecl.Name} = ", constDecl.Value, ";");
                    AppendLine();
                    return;

                case ClassDeclStmt classDecl:
                    WriteClassDeclaration(classDecl, exported: false, includeNamespaceVisibility: true);
                    return;

                case InterfaceDeclStmt interfaceDecl:
                    WriteInterfaceDeclaration(interfaceDecl, exported: false, includeNamespaceVisibility: true);
                    return;

                case EnumDeclStmt enumDecl:
                    WriteEnumDeclaration(enumDecl, exported: false, includeVisibility: true);
                    return;

                default:
                    WriteStatement(stmt);
                    return;
            }
        }

        private void WriteDelete(DeleteExprStmt deleteExpr)
        {
            string target = FormatExpr(deleteExpr.Target);
            if (deleteExpr.DeleteAll && deleteExpr.Target is not VarExpr)
                target += "[]";

            WriteIndentedLine($"delete {target};");
        }

        private void WriteExpressionStatement(string prefix, Expr expr, string suffix = ";")
        {
            WriteIndent();
            WriteInlineExpression(prefix, expr, suffix);
            AppendLine();
        }

        private void WriteInlineExpression(string prefix, Expr expr, string suffix)
        {
            string inline = FormatExpr(expr);
            if (!ShouldWriteMultilineExpression(prefix, expr, suffix, inline))
            {
                _sb.Append(prefix);
                _sb.Append(inline);
                _sb.Append(suffix);
                return;
            }

            _sb.Append(prefix);
            WriteMultilineExpression(expr);
            _sb.Append(suffix);
        }

        private bool ShouldWriteMultilineExpression(string prefix, Expr expr, string suffix, string? inlineText = null)
        {
            if (_maxLineWidth == int.MaxValue || !IsMultilineCandidate(expr))
                return false;

            string text = inlineText ?? FormatExpr(expr);
            return GetCurrentColumn() + prefix.Length + text.Length + suffix.Length > _maxLineWidth;
        }

        private bool IsMultilineCandidate(Expr expr)
        {
            return expr switch
            {
                AwaitExpr awaitExpr => IsMultilineCandidate(awaitExpr.Inner),
                ArrayExpr array => array.Elements.Count > 1,
                DictExpr dict => dict.Pairs.Count > 1,
                CallExpr call => call.Args.Count > 1,
                MethodCallExpr methodCall => methodCall.Args.Count > 1,
                NewExpr newExpr => newExpr.Args.Count > 1 || HasMultilineObjectInitializer(newExpr.Initializers),
                ObjectInitExpr objectInit => HasMultilineObjectInitializer(objectInit.Inits),
                BinaryExpr binary when binary.Op is TokenType.AndAnd or TokenType.OrOr => HasLogicalChain(binary),
                _ => false
            };
        }

        private void WriteMultilineExpression(Expr expr)
        {
            switch (expr)
            {
                case AwaitExpr awaitExpr when IsMultilineCandidate(awaitExpr.Inner):
                    _sb.Append("await ");
                    WriteMultilineExpression(awaitExpr.Inner);
                    return;

                case ArrayExpr array:
                    WriteMultilineArray(array);
                    return;

                case DictExpr dict:
                    WriteMultilineDictionary(dict);
                    return;

                case CallExpr call:
                    WriteMultilineCall(call);
                    return;

                case MethodCallExpr methodCall:
                    WriteMultilineMethodCall(methodCall);
                    return;

                case NewExpr newExpr:
                    WriteMultilineNew(newExpr);
                    return;

                case ObjectInitExpr objectInit:
                    WriteMultilineObjectInitializer(objectInit);
                    return;

                case BinaryExpr binary when (binary.Op is TokenType.AndAnd or TokenType.OrOr) && HasLogicalChain(binary):
                    WriteMultilineLogicalChain(binary);
                    return;

                default:
                    _sb.Append(FormatExpr(expr));
                    return;
            }
        }

        private void WriteMultilineArray(ArrayExpr array)
        {
            _sb.Append('[');
            WriteMultilineItems(array.Elements, element => _sb.Append(FormatExpr(element)), ']');
        }

        private void WriteMultilineDictionary(DictExpr dict)
        {
            _sb.Append('{');
            WriteMultilineItems(dict.Pairs, pair =>
            {
                _sb.Append(FormatExpr(pair.Item1));
                _sb.Append(": ");
                _sb.Append(FormatExpr(pair.Item2));
            }, '}');
        }

        private void WriteMultilineCall(CallExpr call)
        {
            _sb.Append(FormatExpr(call.Target ?? throw new CfgsAstFormatUnsupportedException(), GetPrecedence(call)));
            _sb.Append('(');
            WriteMultilineItems(call.Args, arg => _sb.Append(FormatExpr(arg)), ')');
        }

        private void WriteMultilineMethodCall(MethodCallExpr methodCall)
        {
            _sb.Append(FormatExpr(methodCall.Target, GetPrecedence(methodCall)));
            _sb.Append('.');
            _sb.Append(methodCall.Method);
            _sb.Append('(');
            WriteMultilineItems(methodCall.Args, arg => _sb.Append(FormatExpr(arg)), ')');
        }

        private void WriteMultilineNew(NewExpr newExpr)
        {
            _sb.Append("new ");
            _sb.Append(newExpr.ClassName);
            _sb.Append('(');
            if (newExpr.Args.Count > 1 && ShouldWriteMultilineItems(newExpr.Args.Select(arg => FormatExpr(arg))))
            {
                WriteMultilineItems(newExpr.Args, arg => _sb.Append(FormatExpr(arg)), ')');
            }
            else
            {
                _sb.Append(string.Join(", ", newExpr.Args.Select(arg => FormatExpr(arg))));
                _sb.Append(')');
            }

            if (newExpr.Initializers.Count == 0)
                return;

            _sb.Append(" {");
            WriteMultilineItems(newExpr.Initializers, WriteObjectInitializerEntry, '}');
        }

        private void WriteMultilineObjectInitializer(ObjectInitExpr objectInit)
        {
            _sb.Append(FormatExpr(objectInit.Target, GetPrecedence(objectInit)));
            _sb.Append(" {");
            WriteMultilineItems(objectInit.Inits, WriteObjectInitializerEntry, '}');
        }

        private void WriteMultilineLogicalChain(BinaryExpr binary)
        {
            List<Expr> terms = [];
            CollectLogicalChainTerms(binary, binary.Op, terms);
            int precedence = GetPrecedence(binary);

            _sb.Append(FormatExpr(terms[0], precedence));
            if (terms.Count == 1)
                return;

            _indentLevel++;
            for (int i = 1; i < terms.Count; i++)
            {
                _sb.Append(' ');
                _sb.Append(MapOperator(binary.Op));
                AppendLine();
                WriteIndent();
                _sb.Append(FormatExpr(terms[i], precedence));
            }
            _indentLevel--;
        }

        private void WriteMultilineItems<T>(IReadOnlyList<T> items, Action<T> writeItem, char closingToken)
        {
            if (items.Count == 0)
            {
                _sb.Append(closingToken);
                return;
            }

            AppendLine();
            _indentLevel++;
            for (int i = 0; i < items.Count; i++)
            {
                WriteIndent();
                writeItem(items[i]);
                if (i < items.Count - 1)
                    _sb.Append(',');
                AppendLine();
            }
            _indentLevel--;
            WriteIndent();
            _sb.Append(closingToken);
        }

        private bool ShouldWriteMultilineItems(IEnumerable<string> items)
        {
            if (_maxLineWidth == int.MaxValue)
                return false;

            int width = GetCurrentColumn() + string.Join(", ", items).Length + 1;
            return width > _maxLineWidth;
        }

        private bool HasMultilineObjectInitializer(IReadOnlyList<ObjectInitializerEntry> entries)
        {
            if (entries.Count > 1)
                return true;

            return entries.Any(IsMultilineObjectInitializerEntryCandidate);
        }

        private bool IsMultilineObjectInitializerEntryCandidate(ObjectInitializerEntry entry)
        {
            return entry switch
            {
                ObjectMemberInitializerEntry member when member.IsNested => HasMultilineObjectInitializer(member.NestedInitializers),
                ObjectMemberInitializerEntry member when member.Value is not null => IsMultilineCandidate(member.Value),
                ObjectIndexerInitializerEntry indexer => IsMultilineCandidate(indexer.Index) || IsMultilineCandidate(indexer.Value),
                _ => false
            };
        }

        private void WriteObjectInitializerEntry(ObjectInitializerEntry entry)
        {
            switch (entry)
            {
                case ObjectMemberInitializerEntry member when member.IsNested:
                    _sb.Append(member.Name);
                    _sb.Append(" = ");
                    WriteInitializerBlock(member.NestedInitializers);
                    return;

                case ObjectMemberInitializerEntry member:
                    _sb.Append(member.Name);
                    _sb.Append(": ");
                    _sb.Append(FormatExpr(member.Value ?? throw new CfgsAstFormatUnsupportedException()));
                    return;

                case ObjectIndexerInitializerEntry indexer:
                    _sb.Append('[');
                    _sb.Append(FormatExpr(indexer.Index));
                    _sb.Append("] = ");
                    _sb.Append(FormatExpr(indexer.Value));
                    return;

                default:
                    throw new CfgsAstFormatUnsupportedException();
            }
        }

        private void WriteInitializerBlock(IReadOnlyList<ObjectInitializerEntry> entries)
        {
            if (HasMultilineObjectInitializer(entries))
            {
                _sb.Append('{');
                WriteMultilineItems(entries, WriteObjectInitializerEntry, '}');
                return;
            }

            _sb.Append("{ ");
            _sb.Append(string.Join(", ", entries.Select(FormatObjectInitializerEntry)));
            _sb.Append(" }");
        }

        private static bool HasLogicalChain(BinaryExpr binary)
        {
            List<Expr> terms = [];
            CollectLogicalChainTerms(binary, binary.Op, terms);
            return terms.Count > 1;
        }

        private static void CollectLogicalChainTerms(Expr expr, TokenType op, List<Expr> terms)
        {
            if (expr is BinaryExpr binary && binary.Op == op)
            {
                CollectLogicalChainTerms(binary.Left, op, terms);
                CollectLogicalChainTerms(binary.Right, op, terms);
                return;
            }

            terms.Add(expr);
        }

        private string FormatForClause(Stmt? stmt)
        {
            return stmt switch
            {
                null => string.Empty,
                VarDecl varDecl => $"var {varDecl.Name}{FormatInitializer(varDecl.Value)}",
                ConstDecl constDecl => $"const {constDecl.Name} = {FormatExpr(constDecl.Value)}",
                DestructureDeclStmt destructureDecl => $"{(destructureDecl.IsConst ? "const" : "var")} {FormatPattern(destructureDecl.Pattern)} = {FormatExpr(destructureDecl.Value)}",
                DestructureAssignStmt destructureAssign => $"{FormatPattern(destructureAssign.Pattern)} = {FormatExpr(destructureAssign.Value)}",
                AssignStmt assignStmt => $"{assignStmt.Name} = {FormatExpr(assignStmt.Value)}",
                AssignIndexExprStmt assignIndex => $"{FormatExpr(assignIndex.Target)} = {FormatExpr(assignIndex.Value)}",
                AssignExprStmt assignExpr => $"{FormatExpr(assignExpr.Target)} = {FormatExpr(assignExpr.Value)}",
                CompoundAssignStmt compoundAssign => $"{FormatExpr(compoundAssign.Target)} {MapOperator(compoundAssign.Op)} {FormatExpr(compoundAssign.Value)}",
                ExprStmt exprStmt => FormatExpr(exprStmt.Expression),
                _ => throw new CfgsAstFormatUnsupportedException()
            };
        }

        private string FormatForeachTarget(ForeachStmt foreachStmt)
        {
            if (foreachStmt.UseIndexValuePair)
            {
                if (foreachStmt.TargetPattern is not ArrayMatchPattern pairPattern || pairPattern.Elements.Count != 2)
                    throw new CfgsAstFormatUnsupportedException();

                return $"{FormatPattern(pairPattern.Elements[0])}, {FormatPattern(pairPattern.Elements[1])}";
            }

            if (foreachStmt.TargetPattern is not null)
                return FormatPattern(foreachStmt.TargetPattern);

            return foreachStmt.VarName;
        }

        private string FormatInitializer(Expr? value)
            => value is null ? string.Empty : $" = {FormatExpr(value)}";

        private static IEnumerable<string> FormatInterfacePropertyAccessors(InterfacePropertyDecl property)
        {
            if (property.HasGetter)
                yield return "get;";
            if (property.HasSetter)
                yield return "set;";
            if (property.HasInit)
                yield return "init;";
        }

        private static string FormatInlinePropertyAccessor(PropertyAccessorDecl accessor)
        {
            StringBuilder rendered = new();
            if (accessor.HasExplicitVisibility)
            {
                rendered.Append(FormatVisibilityKeyword(accessor.Visibility));
                rendered.Append(' ');
            }
            rendered.Append(FormatPropertyAccessorKeyword(accessor.Kind));
            rendered.Append(';');
            return rendered.ToString();
        }

        private string FormatParameters(IReadOnlyList<FunctionParameterSpec> specs, IReadOnlyList<string> parameters, string? restParameter)
        {
            if (specs.Count > 0)
                return string.Join(", ", specs.Select(FormatParameterSpec));

            List<string> rendered = parameters.ToList();
            if (!string.IsNullOrWhiteSpace(restParameter))
                rendered.Add($"*{restParameter}");
            return string.Join(", ", rendered);
        }

        private string FormatParameterSpec(FunctionParameterSpec spec)
        {
            string target = spec.DestructurePattern is null
                ? spec.Name
                : FormatPattern(spec.DestructurePattern);

            if (spec.IsRest)
                target = $"*{target}";

            if (spec.DefaultValue is null)
                return target;

            return $"{target} = {FormatExpr(spec.DefaultValue)}";
        }

        private string FormatPattern(MatchPattern pattern)
        {
            return pattern switch
            {
                WildcardMatchPattern => "_",
                BindingMatchPattern binding => binding.Name,
                ValueMatchPattern value => FormatExpr(value.Value),
                ArrayMatchPattern array => $"[{string.Join(", ", array.Elements.Select(element => FormatPattern(element)))}]",
                DictMatchPattern dict => "{" + string.Join(", ", dict.Entries.Select(FormatDictPatternEntry)) + "}",
                _ => throw new CfgsAstFormatUnsupportedException()
            };
        }

        private string FormatDictPatternEntry((string Key, MatchPattern Pattern) entry)
        {
            if (entry.Pattern is BindingMatchPattern binding &&
                string.Equals(binding.Name, entry.Key, StringComparison.Ordinal))
            {
                return entry.Key;
            }

            return $"{entry.Key}: {FormatPattern(entry.Pattern)}";
        }

        private string FormatExpr(Expr expr, int parentPrecedence = 0, bool isRightChild = false)
        {
            if (TryGetPreservedExpressionText(expr, out string? preservedExpression))
                return preservedExpression;

            int precedence = GetPrecedence(expr);
            string text = expr switch
            {
                NullExpr => "null",
                NumberExpr number => FormatNumber(number.Value),
                StringExpr str => FormatStringLiteral(str.Value),
                InterpolatedStringExpr interpolated => FormatInterpolatedString(interpolated),
                CharExpr ch => FormatCharLiteral(ch.Value),
                BoolExpr boolean => boolean.Value ? "true" : "false",
                VarExpr variable => variable.Name,
                BinaryExpr binary => FormatBinary(binary, precedence),
                UnaryExpr unary => $"{MapOperator(unary.Op)}{FormatExpr(unary.Right, precedence)}",
                PrefixExpr prefix => $"{MapOperator(prefix.Op)}{FormatExpr(prefix.Target ?? throw new CfgsAstFormatUnsupportedException(), precedence)}",
                PostfixExpr postfix => $"{FormatExpr(postfix.Target ?? throw new CfgsAstFormatUnsupportedException(), precedence)}{MapOperator(postfix.Op)}",
                ArrayExpr array => $"[{string.Join(", ", array.Elements.Select(element => FormatExpr(element)))}]",
                DictExpr dict => "{" + string.Join(", ", dict.Pairs.Select(pair => $"{FormatExpr(pair.Key)}: {FormatExpr(pair.Value)}")) + "}",
                IndexExpr index => FormatIndex(index, precedence),
                SliceExpr slice => FormatSlice(slice, precedence),
                CallExpr call => $"{FormatExpr(call.Target ?? throw new CfgsAstFormatUnsupportedException(), precedence)}({string.Join(", ", call.Args.Select(arg => FormatExpr(arg)))})",
                MethodCallExpr methodCall => $"{FormatExpr(methodCall.Target, precedence)}.{methodCall.Method}({string.Join(", ", methodCall.Args.Select(arg => FormatExpr(arg)))})",
                ConditionalExpr conditional => $"{FormatExpr(conditional.Condition, precedence + 1)} ? {FormatExpr(conditional.ThenExpr, precedence)}: {FormatExpr(conditional.ElseExpr, precedence, isRightChild: true)}",
                AwaitExpr awaitExpr => $"await {FormatExpr(awaitExpr.Inner, precedence)}",
                MatchExpr matchExpr => FormatMatchExpr(matchExpr, precedence),
                FuncExpr funcExpr => FormatFunctionExpr(funcExpr),
                OutExpr outExpr => FormatOutExpr(outExpr),
                NewExpr newExpr => FormatNewExpr(newExpr),
                ObjectInitExpr objectInit => $"{FormatExpr(objectInit.Target, precedence)} {{ {string.Join(", ", objectInit.Inits.Select(FormatObjectInitializerEntry))} }}",
                NativeAssemblyExpr nativeAssembly => $"import native {FormatStringLiteral(nativeAssembly.Path)} as {nativeAssembly.Alias}",
                GetFieldExpr getField => $"{FormatExpr(getField.Target, precedence)}.{getField.Field}",
                NamedArgExpr namedArg => $"{namedArg.Name}: {FormatExpr(namedArg.Value)}",
                SpreadArgExpr spreadArg => $"*{FormatExpr(spreadArg.Value)}",
                TryUnwrapExpr => throw new CfgsAstFormatUnsupportedException(),
                _ => throw new CfgsAstFormatUnsupportedException()
            };

            if (precedence < parentPrecedence || (isRightChild && precedence == parentPrecedence && expr is BinaryExpr or ConditionalExpr))
                return $"({text})";

            return text;
        }

        private string FormatBinary(BinaryExpr binary, int precedence)
        {
            bool isRightAssociative = binary.Op == TokenType.Expo;
            string left = FormatExpr(binary.Left, isRightAssociative ? precedence + 1 : precedence);
            string right = FormatExpr(binary.Right, precedence, isRightChild: true);
            return $"{left} {MapOperator(binary.Op)} {right}";
        }

        private string FormatIndex(IndexExpr index, int precedence)
        {
            if (index.IsDotAccess &&
                index.Index is StringExpr member &&
                IsSimpleMemberName(member.Value))
            {
                return $"{FormatExpr(index.Target ?? throw new CfgsAstFormatUnsupportedException(), precedence)}.{member.Value}";
            }

            return $"{FormatExpr(index.Target ?? throw new CfgsAstFormatUnsupportedException(), precedence)}[{FormatExpr(index.Index ?? throw new CfgsAstFormatUnsupportedException())}]";
        }

        private string FormatSlice(SliceExpr slice, int precedence)
        {
            string start = slice.Start is null ? string.Empty : FormatExpr(slice.Start);
            string end = slice.End is null ? string.Empty : FormatExpr(slice.End);
            return $"{FormatExpr(slice.Target ?? throw new CfgsAstFormatUnsupportedException(), precedence)}[{start}..{end}]";
        }

        private string FormatMatchExpr(MatchExpr matchExpr, int precedence)
        {
            List<string> arms = [];
            foreach (CaseExprArm arm in matchExpr.Arms)
            {
                string rendered = FormatPattern(arm.Pattern);
                if (arm.Guard is not null)
                    rendered += $" if {FormatExpr(arm.Guard)}";
                rendered += $": {FormatExpr(arm.Body)}";
                arms.Add(rendered);
            }

            if (matchExpr.DefaultArm is not null)
                arms.Add($"_: {FormatExpr(matchExpr.DefaultArm)}");

            return $"{FormatExpr(matchExpr.Scrutinee, precedence)} match {{ {string.Join(", ", arms)} }}";
        }

        private string FormatFunctionExpr(FuncExpr funcExpr)
        {
            StringBuilder nested = new();
            if (funcExpr.IsAsync)
                nested.Append("async ");
            nested.Append("func(");
            nested.Append(FormatParameters(funcExpr.ParameterSpecs, funcExpr.Parameters, funcExpr.RestParameter));
            nested.Append(')');
            nested.Append(FormatInlineBlock(funcExpr.Body));
            return nested.ToString();
        }

        private string FormatOutExpr(OutExpr outExpr)
            => $"out {FormatInlineBlock(outExpr.Body)}";

        private string FormatNewExpr(NewExpr newExpr)
        {
            string text = $"new {newExpr.ClassName}({string.Join(", ", newExpr.Args.Select(arg => FormatExpr(arg)))})";
            if (newExpr.Initializers.Count == 0)
                return text;

            return text + " { " + string.Join(", ", newExpr.Initializers.Select(FormatObjectInitializerEntry)) + " }";
        }

        private string FormatObjectInitializerEntry(ObjectInitializerEntry entry)
        {
            return entry switch
            {
                ObjectMemberInitializerEntry member when member.IsNested =>
                    $"{member.Name} = {{ {string.Join(", ", member.NestedInitializers.Select(FormatObjectInitializerEntry))} }}",
                ObjectMemberInitializerEntry member =>
                    $"{member.Name}: {FormatExpr(member.Value ?? throw new CfgsAstFormatUnsupportedException())}",
                ObjectIndexerInitializerEntry indexer =>
                    $"[{FormatExpr(indexer.Index)}] = {FormatExpr(indexer.Value)}",
                _ => throw new CfgsAstFormatUnsupportedException()
            };
        }

        private string FormatInlineBlock(BlockStmt block)
        {
            StringBuilder nested = new();
            nested.Append('{');
            nested.AppendLine();
            _indentLevel++;
            foreach (Stmt stmt in block.Statements)
                WriteNestedStatement(nested, stmt);
            _indentLevel--;
            nested.Append(GetIndent());
            nested.Append('}');
            return nested.ToString();
        }

        private void WriteNestedStatement(StringBuilder target, Stmt stmt)
        {
            string? original = null;
            try
            {
                original = _sb.ToString();
                _sb.Clear();
                WriteStatement(stmt);
                target.Append(_sb);
            }
            finally
            {
                _sb.Clear();
                if (original is not null)
                    _sb.Append(original);
            }
        }

        private void WriteEmbeddedBody(Stmt stmt)
        {
            if (stmt is BlockStmt block)
            {
                WriteBlock(block);
                return;
            }

            AppendLine();
            _indentLevel++;
            WriteStatement(stmt);
            _indentLevel--;
        }

        private void WriteBlock(BlockStmt block)
        {
            _sb.Append('{');
            AppendLine();
            _indentLevel++;
            WriteNodeList(block.Statements, GetDelimitedBodyStartOffset(block), GetDelimitedBodyEndOffset(block), WriteStatement, skipInitialRenderedLineBreak: true);
            _indentLevel--;
            WriteIndent();
            _sb.Append('}');
        }

        private void WriteNodeList<T>(IReadOnlyList<T> nodes, int startOffset, int endOffset, Action<T> writeNode, bool skipInitialRenderedLineBreak = false)
            where T : Node
        {
            int cursor = skipInitialRenderedLineBreak
                ? SkipRenderedLineBreak(startOffset)
                : startOffset;

            foreach (T node in nodes)
            {
                if (HasSpan(node))
                    WriteStandaloneGap(cursor, GetNodeStartOffset(node));
                writeNode(node);
                cursor = HasSpan(node) ? GetCursorAfterNode(node) : cursor;
            }

            WriteStandaloneGap(cursor, endOffset);
        }

        private int GetCursorAfterNode(Node node)
        {
            if (!HasSpan(node))
                return 0;

            int cursor = Math.Min(_sourceText.Length, GetNodeEndOffset(node));
            int probe = cursor;
            while (probe < _sourceText.Length && (_sourceText[probe] == ' ' || _sourceText[probe] == '\t'))
                probe++;

            if (TryReadLineComment(probe, out string? commentText, out int commentEnd))
            {
                AppendTrailingComment(commentText);
                return SkipSingleLineBreak(commentEnd);
            }

            if (probe < _sourceText.Length && _sourceText[probe] == '\n')
                return probe + 1;

            return cursor;
        }

        private int SkipRenderedLineBreak(int startOffset)
        {
            int probe = Math.Min(startOffset, _sourceText.Length);
            while (probe < _sourceText.Length && (_sourceText[probe] == ' ' || _sourceText[probe] == '\t'))
                probe++;

            if (TryReadLineComment(probe, out string? commentText, out int commentEnd))
            {
                AppendTrailingComment(commentText);
                return SkipSingleLineBreak(commentEnd);
            }

            if (probe < _sourceText.Length && _sourceText[probe] == '\n')
                return probe + 1;

            return startOffset;
        }

        private int SkipSingleLineBreak(int offset)
        {
            if (offset < _sourceText.Length && _sourceText[offset] == '\n')
                return offset + 1;

            return offset;
        }

        private void AppendTrailingComment(string commentText)
        {
            if (_sb.Length > 0 && _sb[^1] == '\n')
                _sb.Insert(_sb.Length - 1, $" {commentText.Trim()}");
            else
                _sb.Append(' ').Append(commentText.Trim());
        }

        private void WriteStandaloneGap(int startOffset, int endOffset)
        {
            if (endOffset <= startOffset || startOffset < 0 || endOffset > _sourceText.Length)
                return;

            string segment = _sourceText[startOffset..endOffset];
            if (segment.Length == 0)
                return;

            string[] lines = segment.Split('\n');
            bool endsWithNewline = segment.EndsWith('\n');
            int fullLineCount = endsWithNewline ? lines.Length - 1 : Math.Max(0, lines.Length - 1);
            bool inBlockComment = false;
            for (int i = 0; i < fullLineCount; i++)
            {
                WriteStandaloneGapLine(lines[i], ref inBlockComment, allowBlankLine: true);
            }

            if (endsWithNewline)
                return;

            WriteStandaloneGapLine(lines[^1], ref inBlockComment, allowBlankLine: false);
        }

        private void WriteStandaloneGapLine(string rawLine, ref bool inBlockComment, bool allowBlankLine)
        {
            string trimmed = rawLine.Trim();
            if (trimmed.Length == 0)
            {
                if (allowBlankLine)
                    AppendLine();
                return;
            }

            if (inBlockComment)
            {
                WriteIndent();
                _sb.Append(trimmed);
                AppendLine();
                if (trimmed.Contains("+#", StringComparison.Ordinal))
                    inBlockComment = false;
                return;
            }

            if (trimmed.StartsWith("#+", StringComparison.Ordinal))
            {
                inBlockComment = !trimmed.Contains("+#", StringComparison.Ordinal);
                WriteIndent();
                _sb.Append(trimmed);
                AppendLine();
                return;
            }

            if (!trimmed.StartsWith('#') && !trimmed.StartsWith("+#", StringComparison.Ordinal))
                return;

            WriteIndent();
            _sb.Append(trimmed);
            AppendLine();
        }

        private bool TryReadLineComment(int offset, out string commentText, out int commentEnd)
        {
            commentText = string.Empty;
            commentEnd = offset;
            if (offset < 0 || offset >= _sourceText.Length || _sourceText[offset] != '#')
                return false;

            if (offset + 1 < _sourceText.Length && _sourceText[offset + 1] == '+')
                return false;

            int probe = offset;
            while (probe < _sourceText.Length && _sourceText[probe] != '\n')
                probe++;

            commentText = _sourceText[offset..probe];
            commentEnd = probe;
            return true;
        }

        private bool TryGetPreservedExpressionText(Expr expr, out string preservedExpression)
        {
            preservedExpression = string.Empty;
            if (!NodeContainsComment(expr))
                return false;

            if (!TryGetNodeText(expr, out string text))
                return false;

            preservedExpression = text.Trim();
            return preservedExpression.Length > 0;
        }

        private bool NodeContainsComment(Node node)
        {
            if (!TryGetNodeText(node, out string text))
                return false;

            return ContainsComments(text);
        }

        private bool TryGetNodeText(Node node, out string text)
        {
            text = string.Empty;
            if (!HasSpan(node))
                return false;

            int startOffset = GetNodeStartOffset(node);
            int endOffset = GetNodeEndOffset(node);
            if (startOffset < 0 || endOffset < startOffset || endOffset > _sourceText.Length)
                return false;

            text = _sourceText[startOffset..endOffset];
            return true;
        }

        private bool HasSpan(Node node)
            => node.SyntaxStartTokenIndex >= 0 &&
               node.SyntaxEndTokenIndex >= node.SyntaxStartTokenIndex &&
               node.SyntaxEndTokenIndex < _tokens.Count;

        private int GetNodeStartOffset(Node node)
            => HasSpan(node) ? _tokens[node.SyntaxStartTokenIndex].StartOffset : 0;

        private int GetNodeEndOffset(Node node)
            => HasSpan(node) ? _tokens[node.SyntaxEndTokenIndex].EndOffset : 0;

        private int GetDelimitedBodyStartOffset(Node node)
        {
            int openIndex = FindFirstTokenIndex(node, TokenType.LBrace);
            return openIndex >= 0 ? _tokens[openIndex].EndOffset : GetNodeStartOffset(node);
        }

        private int GetDelimitedBodyEndOffset(Node node)
        {
            int openIndex = FindFirstTokenIndex(node, TokenType.LBrace);
            if (openIndex < 0)
                return GetNodeEndOffset(node);

            int closeIndex = FindMatchingTokenIndex(openIndex, TokenType.LBrace, TokenType.RBrace);
            return closeIndex >= 0 ? _tokens[closeIndex].StartOffset : GetNodeEndOffset(node);
        }

        private int FindFirstTokenIndex(Node node, TokenType type)
        {
            if (!HasSpan(node))
                return -1;

            for (int i = node.SyntaxStartTokenIndex; i <= node.SyntaxEndTokenIndex && i < _tokens.Count; i++)
            {
                if (_tokens[i].Type == type)
                    return i;
            }

            return -1;
        }

        private int FindMatchingTokenIndex(int openIndex, TokenType openType, TokenType closeType)
        {
            int depth = 0;
            for (int i = openIndex; i < _tokens.Count; i++)
            {
                if (_tokens[i].Type == openType)
                    depth++;
                else if (_tokens[i].Type == closeType)
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        private static int GetPrecedence(Expr expr)
        {
            return expr switch
            {
                ConditionalExpr => 1,
                BinaryExpr { Op: TokenType.QQNull } => 2,
                BinaryExpr { Op: TokenType.OrOr } => 3,
                BinaryExpr { Op: TokenType.AndAnd } => 4,
                BinaryExpr { Op: TokenType.Eq or TokenType.Neq or TokenType.Lt or TokenType.Gt or TokenType.Le or TokenType.Ge or TokenType.Is } => 5,
                BinaryExpr { Op: TokenType.bOr } => 6,
                BinaryExpr { Op: TokenType.bXor } => 7,
                BinaryExpr { Op: TokenType.bAnd } => 8,
                BinaryExpr { Op: TokenType.MergeLeft or TokenType.MergeRight } => 9,
                BinaryExpr { Op: TokenType.bShiftL or TokenType.bShiftR } => 10,
                BinaryExpr { Op: TokenType.Plus or TokenType.Minus } => 11,
                BinaryExpr { Op: TokenType.Star or TokenType.Slash or TokenType.Modulo } => 12,
                BinaryExpr { Op: TokenType.Expo } => 13,
                UnaryExpr or PrefixExpr or AwaitExpr => 14,
                CallExpr or MethodCallExpr or IndexExpr or SliceExpr or PostfixExpr => 15,
                _ => 16
            };
        }

        private static string MapOperator(TokenType tokenType)
        {
            return tokenType switch
            {
                TokenType.Plus => "+",
                TokenType.Minus => "-",
                TokenType.Star => "*",
                TokenType.Slash => "/",
                TokenType.Expo => "**",
                TokenType.Modulo => "%",
                TokenType.bShiftL => "<<",
                TokenType.bShiftR => ">>",
                TokenType.MergeLeft => "<<<",
                TokenType.MergeRight => ">>>",
                TokenType.bOr => "|",
                TokenType.bAnd => "&",
                TokenType.bXor => "^",
                TokenType.PlusPlus => "++",
                TokenType.MinusMinus => "--",
                TokenType.PlusAssign => "+=",
                TokenType.MinusAssign => "-=",
                TokenType.StarAssign => "*=",
                TokenType.SlashAssign => "/=",
                TokenType.ModAssign => "%=",
                TokenType.Assign => "=",
                TokenType.Question => "?",
                TokenType.QQNull => "??",
                TokenType.Eq => "==",
                TokenType.Neq => "!=",
                TokenType.Lt => "<",
                TokenType.Gt => ">",
                TokenType.Le => "<=",
                TokenType.Ge => ">=",
                TokenType.Not => "!",
                TokenType.AndAnd => "&&",
                TokenType.OrOr => "||",
                TokenType.Is => "is",
                _ => throw new CfgsAstFormatUnsupportedException()
            };
        }

        private static string FormatNumber(object? value)
        {
            return value switch
            {
                null => "0",
                double dbl => dbl.ToString("R", CultureInfo.InvariantCulture),
                float flt => flt.ToString("R", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? "0"
            };
        }

        private static string FormatStringLiteral(string value)
        {
            StringBuilder sb = new(value.Length + 2);
            sb.Append('"');
            foreach (char ch in value)
            {
                sb.Append(ch switch
                {
                    '\\' => "\\\\",
                    '"' => "\\\"",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    _ when char.IsControl(ch) => $"\\u{(int)ch:x4}",
                    _ => ch.ToString()
                });
            }
            sb.Append('"');
            return sb.ToString();
        }

        private string FormatInterpolatedString(InterpolatedStringExpr expr)
        {
            StringBuilder sb = new();
            sb.Append('$');
            sb.Append('"');

            foreach (InterpolatedStringPart part in expr.Parts)
            {
                switch (part)
                {
                    case InterpolatedStringTextPart textPart:
                        foreach (char ch in textPart.Value)
                        {
                            switch (ch)
                            {
                                case '{':
                                    sb.Append("{{");
                                    break;
                                case '}':
                                    sb.Append("}}");
                                    break;
                                case '\\':
                                    sb.Append("\\\\");
                                    break;
                                case '"':
                                    sb.Append("\\\"");
                                    break;
                                case '\n':
                                    sb.Append("\\n");
                                    break;
                                case '\r':
                                    sb.Append("\\r");
                                    break;
                                case '\t':
                                    sb.Append("\\t");
                                    break;
                                default:
                                    if (char.IsControl(ch))
                                        sb.Append($"\\u{(int)ch:x4}");
                                    else
                                        sb.Append(ch);
                                    break;
                            }
                        }
                        break;
                    case InterpolatedStringExprPart exprPart:
                        sb.Append('{');
                        sb.Append(FormatInterpolatedExpression(exprPart.Expression));
                        if (exprPart.Alignment.HasValue)
                        {
                            sb.Append(',');
                            sb.Append(exprPart.Alignment.Value.ToString(CultureInfo.InvariantCulture));
                        }

                        if (exprPart.Format is not null)
                        {
                            sb.Append(':');
                            sb.Append(exprPart.Format);
                        }

                        sb.Append('}');
                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }

        private string FormatInterpolatedExpression(Expr expr)
        {
            string formatted = FormatExpr(expr);
            return expr is ConditionalExpr ? $"({formatted})" : formatted;
        }

        private static string FormatCharLiteral(char value)
        {
            string escaped = value switch
            {
                '\\' => "\\\\",
                '\'' => "\\'",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(value) => $"\\u{(int)value:x4}",
                _ => value.ToString()
            };
            return $"'{escaped}'";
        }

        private static bool IsSimpleMemberName(string value)
            => !string.IsNullOrWhiteSpace(value) &&
               IsIdentifierStart(value[0]) &&
               value.Skip(1).All(IsIdentifierPart);

        private int GetCurrentColumn()
        {
            for (int i = _sb.Length - 1; i >= 0; i--)
            {
                if (_sb[i] == '\n')
                    return _sb.Length - i - 1;
            }

            return _sb.Length;
        }

        private void WriteVisibilityPrefix(MemberVisibility visibility, bool hasExplicitVisibility)
        {
            if (!hasExplicitVisibility)
                return;

            _sb.Append(FormatVisibilityKeyword(visibility));
            _sb.Append(' ');
        }

        private static string FormatVisibilityKeyword(MemberVisibility visibility)
        {
            return visibility switch
            {
                MemberVisibility.Private => "private",
                MemberVisibility.Protected => "protected",
                _ => "public"
            };
        }

        private static string FormatPropertyAccessorKeyword(PropertyAccessorKind kind)
        {
            return kind switch
            {
                PropertyAccessorKind.Get => "get",
                PropertyAccessorKind.Set => "set",
                PropertyAccessorKind.Init => "init",
                _ => throw new CfgsAstFormatUnsupportedException()
            };
        }

        private static bool ShouldWriteEnumMemberValue(IReadOnlyList<EnumMemberNode> members, int index)
        {
            if (!TryGetEnumIntValue(members[index], out int current))
                return true;

            if (index == 0)
                return current != 0;

            if (!TryGetEnumIntValue(members[index - 1], out int previous))
                return true;

            return current != previous + 1;
        }

        private static bool TryGetEnumIntValue(EnumMemberNode member, out int value)
        {
            switch (member.Value)
            {
                case int intValue:
                    value = intValue;
                    return true;
                case null:
                    value = 0;
                    return false;
                default:
                    try
                    {
                        value = Convert.ToInt32(member.Value, CultureInfo.InvariantCulture);
                        return true;
                    }
                    catch
                    {
                        value = 0;
                        return false;
                    }
            }
        }

        private void WriteIndentedLine(string text)
        {
            WriteIndent();
            _sb.Append(text);
            AppendLine();
        }

        private void WriteIndent()
            => _sb.Append(GetIndent());

        private string GetIndent()
        {
            if (_indentLevel <= 0)
                return string.Empty;

            StringBuilder indent = new(_indentUnit.Length * _indentLevel);
            for (int i = 0; i < _indentLevel; i++)
                indent.Append(_indentUnit);
            return indent.ToString();
        }

        private void AppendLine()
            => _sb.Append('\n');
    }

    private sealed class CfgsAstFormatUnsupportedException : Exception
    {
    }
}
