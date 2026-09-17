using System;
using System.Web.Script.Serialization;

namespace PromptGenerator
{
    /// <summary>
    /// JSON 序列化/反序列化薄封装，基于 System.Web.Extensions 的 JavaScriptSerializer。
    /// </summary>
    internal static class JsonUtil
    {
        /// <summary>把对象序列化为 JSON 字符串。</summary>
        public static string Serialize(object value)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            serializer.RecursionLimit = 100;
            return serializer.Serialize(value);
        }

        /// <summary>把 JSON 字符串反序列化为指定类型。</summary>
        public static T Deserialize<T>(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException("json");
            }
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            serializer.RecursionLimit = 100;
            return serializer.Deserialize<T>(json);
        }

        /// <summary>尝试反序列化到 Dictionary，失败时返回 null（不抛异常）。</summary>
        public static bool TryDeserializeObject(string json, out System.Collections.Generic.Dictionary<string, object> result)
        {
            result = null;
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }
            try
            {
                result = Deserialize<System.Collections.Generic.Dictionary<string, object>>(json);
                return result != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>把 JSON 中可能出现的任意标量安全地转为字符串。</summary>
        public static string AsString(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            string s = value as string;
            if (s != null)
            {
                return s;
            }
            // 复合类型（数组/字典等）没有可用的标量语义，返回空串，
            // 避免把 "System.Collections.ArrayList" 这类类型名泄露到界面
            if (value is System.Collections.IEnumerable || value is System.Collections.IDictionary)
            {
                return string.Empty;
            }
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>从字典中按键取字符串值，键不存在时返回空字符串。</summary>
        public static string GetString(System.Collections.Generic.Dictionary<string, object> dict, string key)
        {
            if (dict == null)
            {
                return string.Empty;
            }
            object value;
            if (dict.TryGetValue(key, out value))
            {
                return AsString(value);
            }
            return string.Empty;
        }
    }
}
