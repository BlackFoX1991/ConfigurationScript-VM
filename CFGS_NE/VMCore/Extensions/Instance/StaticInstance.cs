namespace CFGS_VM.VMCore.Extensions.Instance
{
    public sealed class StaticInstance
    {
        public string ClassName { get; }
        public Dictionary<string, object> Fields { get; }

        public StaticInstance(string className)
        {
            ClassName = className;
            Fields = new Dictionary<string, object>();
        }
    }
}
