namespace CFGS_VM.VMCore.Extensions.Core
{
    /// <summary>
    /// Runtime metadata for a property declaration.
    /// </summary>
    public sealed class RuntimePropertyDescriptor
    {
        public string Name { get; }

        public bool IsStatic { get; }

        public bool HasGetter { get; }

        public bool HasSetter { get; }

        public bool HasInit { get; }

        public int GetterVisibilityCode { get; }

        public int SetterVisibilityCode { get; }

        public int InitVisibilityCode { get; }

        public string? GetterSlotName { get; }

        public string? SetterSlotName { get; }

        public string? InitSlotName { get; }

        public string? BackingFieldName { get; }

        public bool HasAutoStorage { get; }

        public RuntimePropertyDescriptor(
            string name,
            bool isStatic,
            bool hasGetter,
            bool hasSetter,
            bool hasInit,
            int getterVisibilityCode,
            int setterVisibilityCode,
            int initVisibilityCode,
            string? getterSlotName,
            string? setterSlotName,
            string? initSlotName,
            string? backingFieldName,
            bool hasAutoStorage)
        {
            Name = name;
            IsStatic = isStatic;
            HasGetter = hasGetter;
            HasSetter = hasSetter;
            HasInit = hasInit;
            GetterVisibilityCode = getterVisibilityCode;
            SetterVisibilityCode = setterVisibilityCode;
            InitVisibilityCode = initVisibilityCode;
            GetterSlotName = getterSlotName;
            SetterSlotName = setterSlotName;
            InitSlotName = initSlotName;
            BackingFieldName = backingFieldName;
            HasAutoStorage = hasAutoStorage;
        }

        public override string ToString()
            => $"Property({Name}, static={IsStatic}, auto={HasAutoStorage})";
    }
}
