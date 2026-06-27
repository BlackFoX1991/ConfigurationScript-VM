// Public native-interop surface exercised by the native-interop conversion tests.
// Declared in the GLOBAL namespace on purpose so a CFGS script can reach it as
// `Fx.NativeFixture` after `import native "CFGS.Tooling.Tests.dll" as Fx;`.
//
// Each method targets one of the NativeInterop conversion findings:
//   B1 -> Apply  (CFGS closure passed as a CLR delegate; cache must dispatch by identity)
//   B4 -> AsDouble (a CFGS BigInteger flowing into a native numeric parameter)
//   B5 -> JoinStrings (a CFGS array flowing into a generic List<T> parameter)
//   B3 -> SelfReferencingList (cyclic CLR graph returned to CFGS must not crash the process)

public static class NativeFixture
{
    public static string Apply(Func<string, string> f, string arg) => f(arg);

    public static double AsDouble(double value) => value;

    public static string JoinStrings(List<string> items) => string.Join(",", items);

    public static object SelfReferencingList()
    {
        List<object> list = [];
        list.Add(list);
        return list;
    }
}
