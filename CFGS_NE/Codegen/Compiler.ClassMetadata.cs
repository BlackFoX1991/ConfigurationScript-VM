using CFGS_VM.Analytic.Semantics;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
        /// <summary>
        /// Defines reserved runtime member names.
        /// </summary>
        private static readonly HashSet<string> ReservedRuntimeMemberNames = new(StringComparer.Ordinal)
        {
            "__type",
            "__base",
            "__interfaces",
            "__is_interface",
            "__outer",
            "new"
        };

        /// <summary>
        /// The IsReservedRuntimeMemberName
        /// </summary>
        internal static bool IsReservedRuntimeMemberName(string name)
            => ReservedRuntimeMemberNames.Contains(name);

        /// <summary>
        /// The IsReservedInternalMemberName
        /// </summary>
        internal static bool IsReservedInternalMemberName(string name)
            => name.StartsWith("__", StringComparison.Ordinal);

        /// <summary>
        /// The GetConstructorVisibility
        /// </summary>
        private static MemberVisibility GetConstructorVisibility(ClassDeclStmt cls)
        {
            if (cls.Methods.Any(m => string.Equals(m.Name, "init", StringComparison.Ordinal)))
                return GetOrDefaultVisibility(cls.MethodVisibility, "init");

            return MemberVisibility.Public;
        }

        /// <summary>
        /// The GetOrDefaultVisibility
        /// </summary>
        private static MemberVisibility GetOrDefaultVisibility(Dictionary<string, MemberVisibility> map, string name)
            => map.TryGetValue(name, out MemberVisibility visibility) ? visibility : MemberVisibility.Public;

        /// <summary>
        /// The ToVisibilityCode
        /// </summary>
        private static int ToVisibilityCode(MemberVisibility visibility)
        {
            return visibility switch
            {
                MemberVisibility.Public => 0,
                MemberVisibility.Private => 1,
                MemberVisibility.Protected => 2,
                _ => 0
            };
        }

        /// <summary>
        /// The EnumerateDeclaredInstanceVisibilityEntries
        /// </summary>
        private static List<(string Name, int Code)> EnumerateDeclaredInstanceVisibilityEntries(ClassDeclStmt decl)
        {
            List<(string Name, int Code)> entries = new();

            foreach (string name in decl.Fields.Keys)
                entries.Add((name, ToVisibilityCode(GetOrDefaultVisibility(decl.FieldVisibility, name))));

            foreach (FuncDeclStmt method in decl.Methods)
                entries.Add((method.Name, ToVisibilityCode(GetOrDefaultVisibility(decl.MethodVisibility, method.Name))));

            return entries;
        }

        /// <summary>
        /// The EnumerateDeclaredStaticVisibilityEntries
        /// </summary>
        private static List<(string Name, int Code)> EnumerateDeclaredStaticVisibilityEntries(ClassDeclStmt decl)
        {
            List<(string Name, int Code)> entries = new()
            {
                ("new", ToVisibilityCode(GetConstructorVisibility(decl)))
            };

            foreach (string name in decl.StaticFields.Keys)
                entries.Add((name, ToVisibilityCode(GetOrDefaultVisibility(decl.StaticFieldVisibility, name))));

            foreach (FuncDeclStmt method in decl.StaticMethods)
                entries.Add((method.Name, ToVisibilityCode(GetOrDefaultVisibility(decl.StaticMethodVisibility, method.Name))));

            foreach (EnumDeclStmt enumDecl in decl.Enums)
                entries.Add((enumDecl.Name, ToVisibilityCode(GetOrDefaultVisibility(decl.EnumVisibility, enumDecl.Name))));

            foreach (ClassDeclStmt nested in decl.NestedClasses)
                entries.Add((nested.Name, ToVisibilityCode(GetOrDefaultVisibility(decl.NestedClassVisibility, nested.Name))));

            return entries;
        }

        /// <summary>
        /// The AddDeclaredMembersToClassInfo
        /// </summary>
        private static void AddDeclaredMembersToClassInfo(ClassDeclStmt decl, ClassInfo classInfo)
        {
            foreach (KeyValuePair<string, Expr?> field in decl.Fields)
            {
                classInfo.InstanceMembers.Add(field.Key);
                classInfo.InstanceVisibility[field.Key] = GetOrDefaultVisibility(decl.FieldVisibility, field.Key);
            }

            foreach (FuncDeclStmt method in decl.Methods)
            {
                classInfo.InstanceMembers.Add(method.Name);
                classInfo.InstanceVisibility[method.Name] = GetOrDefaultVisibility(decl.MethodVisibility, method.Name);
            }

            foreach (KeyValuePair<string, Expr?> field in decl.StaticFields)
            {
                classInfo.StaticMembers.Add(field.Key);
                classInfo.StaticVisibility[field.Key] = GetOrDefaultVisibility(decl.StaticFieldVisibility, field.Key);
            }

            foreach (FuncDeclStmt method in decl.StaticMethods)
            {
                classInfo.StaticMembers.Add(method.Name);
                classInfo.StaticVisibility[method.Name] = GetOrDefaultVisibility(decl.StaticMethodVisibility, method.Name);
            }

            classInfo.StaticMembers.Add("new");
            classInfo.StaticVisibility["new"] = GetConstructorVisibility(decl);

            foreach (EnumDeclStmt enumDecl in decl.Enums)
            {
                classInfo.StaticMembers.Add(enumDecl.Name);
                classInfo.StaticVisibility[enumDecl.Name] = GetOrDefaultVisibility(decl.EnumVisibility, enumDecl.Name);
            }

            foreach (ClassDeclStmt nested in decl.NestedClasses)
            {
                classInfo.StaticMembers.Add(nested.Name);
                classInfo.StaticVisibility[nested.Name] = GetOrDefaultVisibility(decl.NestedClassVisibility, nested.Name);
            }
        }

        /// <summary>
        /// The MergeInheritedVisibleMembers
        /// </summary>
        private static void MergeInheritedVisibleMembers(ClassInfo target, ClassInfo baseInfo)
        {
            foreach (KeyValuePair<string, MemberVisibility> entry in baseInfo.InstanceVisibility)
            {
                if (entry.Value == MemberVisibility.Private || target.InstanceMembers.Contains(entry.Key))
                    continue;

                target.InstanceMembers.Add(entry.Key);
                target.InstanceVisibility[entry.Key] = entry.Value;
            }

            foreach (KeyValuePair<string, MemberVisibility> entry in baseInfo.StaticVisibility)
            {
                if (entry.Value == MemberVisibility.Private || target.StaticMembers.Contains(entry.Key))
                    continue;

                target.StaticMembers.Add(entry.Key);
                target.StaticVisibility[entry.Key] = entry.Value;
            }
        }

        /// <summary>
        /// The BuildClassInfos
        /// </summary>
        internal void BuildClassInfos(IReadOnlyList<BoundClass> sortedClasses)
        {
            _classInfos.Clear();

            foreach (BoundClass boundClass in sortedClasses)
            {
                ClassDeclStmt classDecl = boundClass.Declaration;
                string? baseName = string.IsNullOrEmpty(classDecl.BaseName) ? null : classDecl.BaseName;
                ClassInfo classInfo = new(classDecl.Name, baseName, classDecl.IsNested);
                AddDeclaredMembersToClassInfo(classDecl, classInfo);

                if (TryResolveBaseClassDecl(classDecl, out ClassDeclStmt baseDecl) &&
                    _classInfos.TryGetValue(baseDecl, out ClassInfo? baseClassInfo))
                {
                    MergeInheritedVisibleMembers(classInfo, baseClassInfo);
                }

                _classInfos[classDecl] = classInfo;
            }
        }

        /// <summary>
        /// The BuildAdHocClassInfo
        /// </summary>
        private ClassInfo BuildAdHocClassInfo(ClassDeclStmt classDecl)
        {
            string? baseName = string.IsNullOrEmpty(classDecl.BaseName) ? null : classDecl.BaseName;
            ClassInfo classInfo = new(classDecl.Name, baseName, classDecl.IsNested);

            AddDeclaredMembersToClassInfo(classDecl, classInfo);

            if (TryResolveBaseClassDecl(classDecl, out ClassDeclStmt baseDecl))
            {
                if (!_classInfos.TryGetValue(baseDecl, out ClassInfo? baseClassInfo))
                {
                    baseClassInfo = BuildAdHocClassInfo(baseDecl);
                    _classInfos[baseDecl] = baseClassInfo;
                }

                MergeInheritedVisibleMembers(classInfo, baseClassInfo);
            }

            return classInfo;
        }
    }
}
