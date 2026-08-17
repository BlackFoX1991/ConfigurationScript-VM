using System.Collections;
using System.Reflection;

namespace CFGS_VM.Analytic.Tree
{
    /// <summary>
    /// Generic structural traversal over the AST.
    ///
    /// The tree has no hand-written child accessors, and adding them to every node type would be a
    /// standing invitation to forget one. Reflecting over public fields and properties instead is
    /// exhaustive by construction, which is what callers here need: a scan that misses a child would
    /// produce a wrong answer rather than a slow one. All current callers run once per declaration at
    /// analysis or compile time, never per executed instruction.
    /// </summary>
    public static class NodeWalker
    {
        /// <summary>
        /// Yields the direct child nodes of <paramref name="node"/>, including nodes held in
        /// collections. Strings are skipped because they are never children.
        /// </summary>
        public static IEnumerable<object> EnumerateChildren(object node)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;

            foreach (PropertyInfo property in node.GetType().GetProperties(flags))
            {
                if (property.GetIndexParameters().Length > 0 || property.PropertyType == typeof(string))
                    continue;

                foreach (object child in YieldNodes(property.GetValue(node)))
                    yield return child;
            }

            foreach (FieldInfo field in node.GetType().GetFields(flags))
            {
                if (field.FieldType == typeof(string))
                    continue;

                foreach (object child in YieldNodes(field.GetValue(node)))
                    yield return child;
            }
        }

        /// <summary>
        /// Returns whether <paramref name="node"/> or any node below it satisfies
        /// <paramref name="predicate"/>.
        /// </summary>
        public static bool AnyDescendantOrSelf(Node node, Func<Node, bool> predicate)
        {
            if (predicate(node))
                return true;

            foreach (object child in EnumerateChildren(node))
            {
                if (child is Node childNode && AnyDescendantOrSelf(childNode, predicate))
                    return true;
            }

            return false;
        }

        private static IEnumerable<object> YieldNodes(object? value)
        {
            switch (value)
            {
                case null:
                    yield break;
                case Node child:
                    yield return child;
                    yield break;
                case IEnumerable enumerable:
                    foreach (object? item in enumerable)
                    {
                        if (item is Node itemNode)
                            yield return itemNode;
                    }
                    yield break;
            }
        }
    }
}
