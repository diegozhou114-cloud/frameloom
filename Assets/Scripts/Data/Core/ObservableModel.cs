using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ZWebGLFrameWork.Data
{
    /// <summary>
    /// 数据模型基类 — 自动收集所有 public Observable&lt;T&gt; 字段。
    /// 每个字段变化时触发 OnAnyChanged（字段名, 旧值, 新值）。
    /// 支持快照/恢复。
    /// </summary>
    public abstract class ObservableModel
    {
        /// <summary>模型名称</summary>
        public string ModelName => GetType().Name;

        /// <summary>任意字段变化时触发（字段名, 旧值, 新值）</summary>
        public event Action<string, object, object> OnAnyChanged;

        private readonly List<string> _fieldNames = new List<string>();
        private readonly Dictionary<string, object> _fieldMap = new Dictionary<string, object>();
        private readonly List<Action> _subscriptions = new List<Action>();

        protected ObservableModel()
        {
            CollectObservableFields();
        }

        // ==================== 反射收集 ====================

        private void CollectObservableFields()
        {
            var fields = GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);

            foreach (var field in fields)
            {
                if (!field.FieldType.IsGenericType) continue;
                if (field.FieldType.GetGenericTypeDefinition() != typeof(Observable<>)) continue;

                var fieldName = field.Name;
                _fieldNames.Add(fieldName);

                var observable = field.GetValue(this);
                _fieldMap[fieldName] = observable;

                // 通过反射调用 SubscribeField<T>，闭包捕获 fieldName
                var valueType = field.FieldType.GetGenericArguments()[0];
                var method = typeof(ObservableModel).GetMethod(
                    nameof(SubscribeField), BindingFlags.Instance | BindingFlags.NonPublic);
                method.MakeGenericMethod(valueType).Invoke(this, new[] { observable, fieldName });
            }
        }

        private void SubscribeField<T>(Observable<T> observable, string fieldName)
        {
            Action<T, T> handler = (oldVal, newVal) =>
            {
                OnAnyChanged?.Invoke(fieldName, oldVal, newVal);
            };
            observable.OnChanged += handler;
            _subscriptions.Add(() => observable.OnChanged -= handler);
        }

        /// <summary>取消所有字段订阅</summary>
        public void ClearSubscriptions()
        {
            foreach (var unsubscribe in _subscriptions)
                unsubscribe();
            _subscriptions.Clear();
        }

        // ==================== 公共方法 ====================

        /// <summary>获取字段</summary>
        public Observable<T> GetField<T>(string fieldName)
        {
            _fieldMap.TryGetValue(fieldName, out var o);
            return o as Observable<T>;
        }

        /// <summary>所有字段名</summary>
        public IReadOnlyList<string> GetFieldNames() => _fieldNames;

        /// <summary>生成快照字典（通过 IObservableField 接口，零反射）</summary>
        public Dictionary<string, object> GetSnapshot()
        {
            var snapshot = new Dictionary<string, object>();
            foreach (var fieldName in _fieldNames)
            {
                if (_fieldMap.TryGetValue(fieldName, out var observable) &&
                    observable is IObservableField field)
                {
                    snapshot[fieldName] = field.GetValue();
                }
            }
            return snapshot;
        }

        /// <summary>从快照静默恢复（不触发回调，通过 IObservableField 接口）</summary>
        public void RestoreSnapshot(Dictionary<string, object> snapshot)
        {
            foreach (var kv in snapshot)
            {
                if (_fieldMap.TryGetValue(kv.Key, out var observable) &&
                    observable is IObservableField field)
                {
                    try
                    {
                        if (field.ValueType.IsEnum && kv.Value is string enumStr)
                            field.SetSilent(Enum.Parse(field.ValueType, enumStr));
                        else
                            field.SetSilent(kv.Value);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError(
                            $"[ObservableModel.RestoreSnapshot] 字段 {kv.Key} 类型转换失败: " +
                            $"value='{kv.Value}', targetType={field.ValueType.Name}. {ex.Message}");
                    }
                }
            }
        }

        /// <summary>重置所有字段为默认值（通过 IObservableField 接口）</summary>
        public virtual void Reset()
        {
            foreach (var fieldName in _fieldNames)
            {
                if (_fieldMap.TryGetValue(fieldName, out var observable) &&
                    observable is IObservableField field)
                {
                    object defaultValue = field.ValueType.IsValueType
                        ? Activator.CreateInstance(field.ValueType) : null;
                    field.SetSilent(defaultValue);
                }
            }
        }

        /// <summary>打印所有值</summary>
        public void DebugPrint()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"--- {ModelName} ---");
            foreach (var kv in GetSnapshot())
                sb.AppendLine($"  {kv.Key}: {kv.Value}");
            Debug.Log(sb.ToString());
        }
    }
}
