using System;

namespace ZWebGLFrameWork.Data
{
    /// <summary>
    /// 可观察字段内部接口 — 提供无反射的 GetValue/SetSilent。
    /// Observable&lt;T&gt; 实现此接口，ObservableModel 通过它操作字段。
    /// </summary>
    public interface IObservableField
    {
        object GetValue();
        void SetSilent(object value);
        System.Type ValueType { get; }
    }

    /// <summary>
    /// 响应式可观察值 — 值变化时自动触发 OnChanged 回调。
    /// 零变化跳过（旧值 Equals 新值时不触发）。
    /// 支持隐式转换为 T，可直接当值使用。
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    public class Observable<T> : IEquatable<Observable<T>>, IObservableField
    {
        private T _value;

        /// <summary>当前值</summary>
        public T Value
        {
            get => _value;
            set
            {
                if (Equals(_value, value)) return; // 零变化跳过

                var oldValue = _value;
                _value = value;
                OnChanged?.Invoke(oldValue, value);
            }
        }

        /// <summary>值变化回调（旧值, 新值）</summary>
        public event Action<T, T> OnChanged;

        // ==================== 构造 ====================

        public Observable() => _value = default;

        public Observable(T initialValue) => _value = initialValue;

        // ==================== 隐式转换 ====================

        public static implicit operator T(Observable<T> observable) => observable._value;

        // ==================== Equals ====================

        public bool Equals(Observable<T> other)
        {
            if (other is null) return false;
            return Equals(_value, other._value);
        }

        public override bool Equals(object obj)
            => obj is Observable<T> other && Equals(other);

        public override int GetHashCode() => _value?.GetHashCode() ?? 0;

        public override string ToString() => _value?.ToString() ?? "null";

        // ==================== 便捷方法 ====================

        /// <summary>不触发回调直接设置值（初始化/反序列化用）</summary>
        public void SetSilent(T value)
        {
            _value = value;
        }

        /// <summary>移除所有监听</summary>
        public void ClearListeners()
        {
            OnChanged = null;
        }

        // ==================== IObservableField 实现（避免反射） ====================

        object IObservableField.GetValue() => _value;

        void IObservableField.SetSilent(object value)
        {
            if (value is T typed)
                _value = typed;
            else
                _value = (T)Convert.ChangeType(value, typeof(T));
        }

        System.Type IObservableField.ValueType => typeof(T);
    }
}
