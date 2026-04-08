using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Semantics
{
    internal static class MethodShapeRules
    {
        public static string DescribeArity(int parameterCount, int minArgs, bool hasRest)
            => hasRest ? $"{minArgs}..*" : $"{minArgs}..{parameterCount}";

        public static string DescribeArity(FuncDeclStmt method)
            => DescribeArity(method.Parameters.Count, method.MinArgs, !string.IsNullOrWhiteSpace(method.RestParameter));

        public static string DescribeArity(InterfaceMethodDecl method)
            => DescribeArity(method.Parameters.Count, method.MinArgs, !string.IsNullOrWhiteSpace(method.RestParameter));

        public static bool HaveCompatibleShapes(
            int expectedParamCount,
            int expectedMinArgs,
            string? expectedRestParameter,
            bool expectedIsAsync,
            int actualParamCount,
            int actualMinArgs,
            string? actualRestParameter,
            bool actualIsAsync)
        {
            return expectedMinArgs == actualMinArgs &&
                   expectedParamCount == actualParamCount &&
                   !string.IsNullOrWhiteSpace(expectedRestParameter) == !string.IsNullOrWhiteSpace(actualRestParameter) &&
                   expectedIsAsync == actualIsAsync;
        }

        public static bool HaveCompatibleShapes(InterfaceMethodDecl expected, InterfaceMethodDecl actual)
            => HaveCompatibleShapes(
                expected.Parameters.Count,
                expected.MinArgs,
                expected.RestParameter,
                expected.IsAsync,
                actual.Parameters.Count,
                actual.MinArgs,
                actual.RestParameter,
                actual.IsAsync);

        public static bool HaveCompatibleShapes(InterfaceMethodDecl expected, FuncDeclStmt actual)
            => HaveCompatibleShapes(
                expected.Parameters.Count,
                expected.MinArgs,
                expected.RestParameter,
                expected.IsAsync,
                actual.Parameters.Count,
                actual.MinArgs,
                actual.RestParameter,
                actual.IsAsync);
    }
}
