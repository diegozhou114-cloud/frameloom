using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ZWebGLFrameWork.Core
{
    internal sealed class ReferenceIdentityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        internal static readonly ReferenceIdentityComparer<T> Instance = new();

        bool IEqualityComparer<T>.Equals(T left, T right) =>
            ReferenceEquals(left, right);

        int IEqualityComparer<T>.GetHashCode(T value) =>
            RuntimeHelpers.GetHashCode(value);
    }
}
