using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace PromptGenerator
{
    /// <summary>生成结果。</summary>
    public class GenerateResult
    {
        public bool Success;
        public string Content;
        public string Error;
        public bool Unauthorized;
        public bool Retried;

        public GenerateResult()
        {
            Success = false;
            Content = string.Empty;
            Error = string.Empty;
            Unauthorized = false;
            Retried = false;
        }
    }

    /// <summary>余额查询结果。</summary>
    public class BalanceResult
    {
        public bool Success;
        /// <summary>已格式化的余额文本，例如 "￥110.00"。</summary>
        public string Text;
        public string Error;
        public bool Unauthorized;

        public BalanceResult()
        {
            Success = false;
            Text = string.Empty;
            Error = string.Empty;
            Unauthorized = false;
        }
    }

    /// <summary>
    /// HTTP 调用层：对话补全（生成）与余额查询，统一错误归一化与有限重试。
    /// </summary>
    public class DeepSeekClient
    {
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _thinking;

        public DeepSeekClient(string apiKey, string model, string thinking)
        {
            _apiKey = apiKey == null ? string.Empty : apiKey.Trim();
            _model = string.IsNullOrEmpty(model) ? Defaults.Model : model.Trim();
            _thinking = Defaults.NormalizeThinking(thinking);
        }

        /// <summary>单次 HTTP 调用的结果。</summary>
        private class HttpOutcome
        {
            public bool Ok;
            public int Status;
            public string Body = string.Empty;
            public string TransportError = string.Empty;
            public bool Retryable;
        }

        /// <summary>
        /// 生成英文提示词。系统提示词作为 messages 的第一条且逐字节不变（缓存友好），
        /// system 与 user 之间不插入任何额外消息。
        /// </summary>
        public GenerateResult Generate(string systemPrompt, string userText)
        {
            GenerateResult result = new GenerateResult();
            if (string.IsNullOrEmpty(_apiKey))
            {
                result.Unauthorized = true;
                result.Error = "尚未配置 API Key，请在「配置」中填写。";
                return result;
            }

            Dictionary<string, object> body = BuildChatBody(systemPrompt, userText);
            string json = JsonUtil.Serialize(body);

            HttpOutcome outcome = ExecuteWithRetry(Defaults.ChatUrl, "POST", json);

            if (!outcome.Ok)
            {
                string message;
                result.Unauthorized = DescribeError(outcome.Status, outcome.Body, outcome.TransportError, out message);
                result.Error = message;
                return result;
            }

            string content;
            string parseError;
            if (!TryExtractContent(outcome.Body, out content, out parseError))
            {
                result.Error = parseError;
                return result;
            }

            result.Success = true;
            result.Content = content;
            return result;
        }

        /// <summary>查询账户余额（不需要模型与思考模式）。</summary>
        public static BalanceResult QueryBalance(string apiKey)
        {
            BalanceResult result = new BalanceResult();
            if (string.IsNullOrEmpty(apiKey) || apiKey.Trim().Length == 0)
            {
                result.Error = "尚未配置 API Key。";
                return result;
            }

            DeepSeekClient client = new DeepSeekClient(apiKey, Defaults.Model, Defaults.ThinkingDefault);
            HttpOutcome outcome = client.ExecuteWithRetry(Defaults.BalanceUrl, "GET", null);

            if (!outcome.Ok)
            {
                string message;
                result.Unauthorized = DescribeError(outcome.Status, outcome.Body, outcome.TransportError, out message);
                result.Error = message;
                return result;
            }

            string text;
            string parseError;
            if (!TryParseBalance(outcome.Body, out text, out parseError))
            {
                result.Error = parseError;
                return result;
            }

            result.Success = true;
            result.Text = text;
            return result;
        }

        /// <summary>组装请求体。thinking 为 DeepSeek 扩展字段：关闭思考后采样参数才生效。</summary>
        private Dictionary<string, object> BuildChatBody(string systemPrompt, string userText)
        {
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["model"] = _model;

            List<object> messages = new List<object>();

            Dictionary<string, object> systemMessage = new Dictionary<string, object>();
            systemMessage["role"] = "system";
            systemMessage["content"] = systemPrompt == null ? string.Empty : systemPrompt;
            messages.Add(systemMessage);

            Dictionary<string, object> userMessage = new Dictionary<string, object>();
            userMessage["role"] = "user";
            userMessage["content"] = userText == null ? string.Empty : userText;
            messages.Add(userMessage);

            body["messages"] = messages;
            body["stream"] = false;

            Dictionary<string, object> thinking = new Dictionary<string, object>();
            if (_thinking == Defaults.ThinkingDisabled)
            {
                thinking["type"] = "disabled";
            }
            else
            {
                thinking["type"] = "enabled";
                body["reasoning_effort"] = _thinking;
            }
            body["thinking"] = thinking;

            return body;
        }

        /// <summary>带有限重试的请求：仅对 429 / 5xx / 网络异常重试。</summary>
        private HttpOutcome ExecuteWithRetry(string url, string method, string jsonBody)
        {
            HttpOutcome outcome = null;
            for (int attempt = 0; attempt <= Defaults.MaxRetries; attempt++)
            {
                outcome = Execute(url, method, jsonBody);
                if (outcome.Ok || !outcome.Retryable)
                {
                    return outcome;
                }
                if (attempt < Defaults.MaxRetries)
                {
                    int delay = 1000 * (int)Math.Pow(2, attempt);
                    Thread.Sleep(delay);
                }
            }
            return outcome;
        }

        /// <summary>执行一次 HTTP 请求，把结果归一化为 HttpOutcome。</summary>
        private HttpOutcome Execute(string url, string method, string jsonBody)
        {
            HttpOutcome outcome = new HttpOutcome();
            HttpWebRequest request = null;
            try
            {
                request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = method;
                request.Accept = "application/json";
                request.Headers["Authorization"] = "Bearer " + _apiKey;
                request.Timeout = Defaults.TimeoutMs;
                request.ReadWriteTimeout = Defaults.TimeoutMs;
                request.KeepAlive = false;
                request.UserAgent = "PromptGenerator/1.0";

                if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    request.ContentType = "application/json; charset=utf-8";
                    byte[] payload = Encoding.UTF8.GetBytes(jsonBody == null ? string.Empty : jsonBody);
                    request.ContentLength = payload.Length;
                    using (Stream stream = request.GetRequestStream())
                    {
                        stream.Write(payload, 0, payload.Length);
                        stream.Flush();
                    }
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    outcome.Status = (int)response.StatusCode;
                    outcome.Body = ReadResponseBody(response);
                    outcome.Ok = outcome.Status >= 200 && outcome.Status < 300;
                    if (!outcome.Ok)
                    {
                        outcome.Retryable = IsRetryableStatus(outcome.Status);
                    }
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse errorResponse = ex.Response as HttpWebResponse;
                if (errorResponse != null)
                {
                    using (errorResponse)
                    {
                        outcome.Status = (int)errorResponse.StatusCode;
                        outcome.Body = ReadResponseBody(errorResponse);
                        outcome.Ok = false;
                        outcome.Retryable = IsRetryableStatus(outcome.Status);
                    }
                }
                else
                {
                    // DNS / 连接 / TLS / 超时等网络层异常
                    outcome.Status = 0;
                    outcome.Ok = false;
                    // 仅“连接失败”类可重试；超时、证书失败等确定性问题不重试，避免长时间无响应
                    outcome.Retryable = IsRetryableTransport(ex.Status);
                    outcome.TransportError = DescribeTransport(ex);
                }
            }
            catch (Exception ex)
            {
                outcome.Status = 0;
                outcome.Ok = false;
                outcome.Retryable = false;
                outcome.TransportError = "请求失败：" + ex.Message;
            }
            return outcome;
        }

        private static bool IsRetryableStatus(int status)
        {
            if (status == 429)
            {
                return true;
            }
            return status >= 500 && status <= 599;
        }

        /// <summary>网络层异常是否可重试：仅连接类故障，超时/证书问题直接反馈。</summary>
        private static bool IsRetryableTransport(WebExceptionStatus status)
        {
            switch (status)
            {
                case WebExceptionStatus.ConnectFailure:
                case WebExceptionStatus.NameResolutionFailure:
                case WebExceptionStatus.ProxyNameResolutionFailure:
                    return true;
                default:
                    return false;
            }
        }

        private static string ReadResponseBody(HttpWebResponse response)
        {
            if (response == null)
            {
                return string.Empty;
            }
            try
            {
                using (Stream stream = response.GetResponseStream())
                {
                    if (stream == null)
                    {
                        return string.Empty;
                    }
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string DescribeTransport(WebException ex)
        {
            string detail = ex.Message;
            switch (ex.Status)
            {
                case WebExceptionStatus.NameResolutionFailure:
                    return "网络错误：无法解析 api.deepseek.com，请检查 DNS 与网络连接。";
                case WebExceptionStatus.ConnectFailure:
                    return "网络错误：无法连接到 api.deepseek.com，请检查网络或代理设置。";
                case WebExceptionStatus.TrustFailure:
                    return "网络错误：TLS 证书校验失败，请检查系统时间或代理。";
                case WebExceptionStatus.Timeout:
                    return "网络错误：请求超时（30 秒），请稍后重试。";
                case WebExceptionStatus.ProxyNameResolutionFailure:
                    return "网络错误：代理服务器无法解析。";
                default:
                    return "网络错误：" + detail;
            }
        }

        /// <summary>
        /// 把 HTTP 状态码与响应体归一化为界面提示。返回是否为鉴权失败（401）。
        /// </summary>
        private static bool DescribeError(int status, string body, string transportError, out string message)
        {
            string detail = ExtractErrorMessage(body);
            string suffix = string.IsNullOrEmpty(detail) ? string.Empty : "：" + detail;

            if (status == 0)
            {
                message = string.IsNullOrEmpty(transportError) ? "网络错误：请求失败。" : transportError;
                return false;
            }

            switch (status)
            {
                case 400:
                    message = "请求参数错误" + suffix;
                    return false;
                case 401:
                    message = "API Key 无效或未配置，请在「配置」中检查 API Key。";
                    if (!string.IsNullOrEmpty(detail))
                    {
                        message = message + "\r\n服务端说明：" + detail;
                    }
                    return true;
                case 402:
                    message = "账户余额不足，请充值后重试。";
                    return false;
                case 422:
                    message = "请求参数不合法（模型名可能已失效，请在配置中更换模型）" + suffix;
                    return false;
                case 429:
                    message = "请求过于频繁，请稍后重试。" + (string.IsNullOrEmpty(detail) ? string.Empty : "\r\n服务端说明：" + detail);
                    return false;
                default:
                    if (status >= 500 && status <= 599)
                    {
                        message = "服务暂时不可用，请稍后重试（HTTP " + status + "）。";
                        return false;
                    }
                    message = "请求失败（HTTP " + status + "）" + suffix;
                    return false;
            }
        }

        /// <summary>从错误响应体中提取 error.message。</summary>
        private static string ExtractErrorMessage(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return string.Empty;
            }
            Dictionary<string, object> root;
            if (!JsonUtil.TryDeserializeObject(body, out root))
            {
                // 非 JSON 响应：截断后原样展示
                string raw = body.Trim();
                if (raw.Length > 200)
                {
                    raw = raw.Substring(0, 200) + "…";
                }
                return raw;
            }
            object errorObj;
            if (root.TryGetValue("error", out errorObj))
            {
                Dictionary<string, object> errorDict = errorObj as Dictionary<string, object>;
                if (errorDict != null)
                {
                    string message = JsonUtil.GetString(errorDict, "message");
                    if (!string.IsNullOrEmpty(message))
                    {
                        return message;
                    }
                }
                else
                {
                    string simple = JsonUtil.AsString(errorObj);
                    if (!string.IsNullOrEmpty(simple))
                    {
                        return simple;
                    }
                }
            }
            string fallback = JsonUtil.GetString(root, "message");
            return fallback;
        }

        /// <summary>提取 choices[0].message.content。</summary>
        private static bool TryExtractContent(string body, out string content, out string error)
        {
            content = string.Empty;
            error = string.Empty;

            Dictionary<string, object> root;
            if (!JsonUtil.TryDeserializeObject(body, out root))
            {
                error = "响应解析失败：返回内容不是合法 JSON。";
                return false;
            }

            object choicesObj;
            if (!root.TryGetValue("choices", out choicesObj))
            {
                error = "响应解析失败：缺少 choices 字段。";
                return false;
            }

            // 注意：JavaScriptSerializer 把嵌套 JSON 数组反序列化为 ArrayList，
            // 只能用 IList 接收（object[] 接收会恒为 null）
            System.Collections.IList choices = choicesObj as System.Collections.IList;
            if (choices == null || choices.Count == 0)
            {
                error = "响应解析失败：choices 为空。";
                return false;
            }

            Dictionary<string, object> first = choices[0] as Dictionary<string, object>;
            if (first == null)
            {
                error = "响应解析失败：choices[0] 结构异常。";
                return false;
            }

            object messageObj;
            if (!first.TryGetValue("message", out messageObj))
            {
                error = "响应解析失败：缺少 message 字段。";
                return false;
            }

            Dictionary<string, object> message = messageObj as Dictionary<string, object>;
            if (message == null)
            {
                error = "响应解析失败：message 结构异常。";
                return false;
            }

            content = JsonUtil.GetString(message, "content");
            if (content == null)
            {
                content = string.Empty;
            }
            content = content.Trim();
            if (content.Length == 0)
            {
                error = "模型返回了空内容，请调整描述或稍后重试。";
                return false;
            }
            return true;
        }

        /// <summary>解析余额响应：遍历 balance_infos，取币种符号 + total_balance（按字符串处理）。</summary>
        private static bool TryParseBalance(string body, out string text, out string error)
        {
            text = string.Empty;
            error = string.Empty;

            Dictionary<string, object> root;
            if (!JsonUtil.TryDeserializeObject(body, out root))
            {
                error = "余额响应解析失败：返回内容不是合法 JSON。";
                return false;
            }

            string available = JsonUtil.GetString(root, "is_available");
            if (string.Equals(available, "false", StringComparison.OrdinalIgnoreCase))
            {
                error = "账户余额不可用（is_available = false）。";
                return false;
            }

            object infosObj;
            if (!root.TryGetValue("balance_infos", out infosObj))
            {
                error = "余额响应解析失败：缺少 balance_infos 字段。";
                return false;
            }

            // 同 TryExtractContent：嵌套数组为 ArrayList，用 IList 接收
            System.Collections.IList infos = infosObj as System.Collections.IList;
            if (infos == null || infos.Count == 0)
            {
                error = "余额响应解析失败：balance_infos 为空。";
                return false;
            }

            List<string> parts = new List<string>();
            for (int i = 0; i < infos.Count; i++)
            {
                Dictionary<string, object> info = infos[i] as Dictionary<string, object>;
                if (info == null)
                {
                    continue;
                }
                string currency = JsonUtil.GetString(info, "currency");
                string total = JsonUtil.GetString(info, "total_balance");
                parts.Add(Defaults.CurrencySymbol(currency) + total);
            }

            if (parts.Count == 0)
            {
                error = "余额响应解析失败：未能读取任何余额信息。";
                return false;
            }

            text = string.Join(" ", parts.ToArray());
            return true;
        }
    }
}
