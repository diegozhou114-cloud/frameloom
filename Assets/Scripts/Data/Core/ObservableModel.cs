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
            observable.OnChanged += (oldVal, newVal) =>
            {
                OnAnyChanged?.Invoke(fieldName, oldVal, newVal);
            };
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

        /// <summary>生成快照字典</summary>
        public Dictionary<string, object> GetSnapshot()
        {
            var snapshot = new Dictionary<string, object>();
            foreach (var fieldName in _fieldNames)
            {
                var observable = _fieldMap[fieldName];
                var value = observable.GetType().GetProperty("Value")?.GetValue(observable);
                snapshot[fieldName] = value;
            }
            return snapshot;
        }

        /// <summary>从快照静默恢复（不触发回调）</summary>
        public void RestoreSnapshot(Dictionary<string, object> snapshot)
        {
            foreach (var kv in snapshot)
            {
                if (_fieldMap.TryGetValue(kv.Key, out var observable))
                {
                    var type = observable.GetType();
                    var targetType = type.GetGenericArguments()[0];
                    var setSilent = type.GetMethod("SetSilent");
                    try
                    {
                        var val = Convert.ChangeType(kv.Value, targetType);
                        setSilent?.Invoke(observable, new[] { val });
                    }
                    catch { /* 类型不匹配，跳过 */ }
                }
            }
        }

        /// <summary>重置所有字段为默认值</summary>
        public virtual void Reset()
        {
            foreach (var fieldName in _fieldNames)
            {
                var observable = _fieldMap[fieldName];
                var type = observable.GetType();
                var targetType = type.GetGenericArguments()[0];
                var setSilent = type.GetMethod("SetSilent");
                setSilent?.Invoke(observable, new[] {
                    targetType.IsValueType ? Activator.CreateInstance(targetType) : null
                });
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
