// Shim for NullableAttribute and NullableContextAttribute.
// Il2CppInterop.Common.dll defines these as internal, blocking the compiler from using them.
// Defining them here in the current compilation lets the compiler find and use them.
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false)]
    internal sealed class NullableAttribute : Attribute
    {
        public NullableAttribute(byte P_0) { }
        public NullableAttribute(byte[] P_0) { }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false)]
    internal sealed class NullableContextAttribute : Attribute
    {
        public NullableContextAttribute(byte P_0) { }
    }
}
