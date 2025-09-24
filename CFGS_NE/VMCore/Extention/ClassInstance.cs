public sealed class ClassInstance
{
    public string ClassName { get; }
    public Dictionary<string, object> Fields { get; }

    public ClassInstance(string className)
    {
        ClassName = className;
        Fields = new Dictionary<string, object>();
    }
}

