using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Yuqiao
{
    public sealed class TranslationResult
    {
        public string Text;
        public string Provider;
        public long ElapsedMs;
    }

    /// <summary>
    /// Conventional machine translation only. No large-model service or API key.
    /// The Edge endpoint was observed in KISS Translator's microsoft.js adapter;
    /// this is an original implementation and includes none of that project's code.
    /// https://github.com/fishjar/kiss-translator/blob/dev/src/builtin-plugins/microsoft.js
    /// Public client endpoints may change and have no promised free service level.
    /// </summary>
    public sealed class TranslationService : IDisposable
    {
        private const int CacheCapacity = 200;
        private const int RequestTimeoutSeconds = 8;
        private readonly HttpClient client;
        private readonly object cacheLock = new object();
        private readonly Dictionary<string, CacheItem> cache = new Dictionary<string, CacheItem>();
        private readonly Queue<string> cacheOrder = new Queue<string>();
        private bool disposed;

        private sealed class CacheItem
        {
            public string Text;
            public string Provider;
        }

        public TranslationService() : this(new HttpClientHandler())
        {
        }

        // Internal seam for deterministic network-failure checks; normal UI uses the default constructor.
        internal TranslationService(HttpMessageHandler handler)
        {
            // .NET Framework otherwise follows older OS TLS defaults on some PCs.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            client = new HttpClient(handler);
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Yuqiao/1.0");
        }

        public async Task<TranslationResult> TranslateAsync(string text, string source, string target, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (disposed) throw new ObjectDisposedException("TranslationService");
            if (String.IsNullOrWhiteSpace(text)) throw new ArgumentException("请先输入需要翻译的文字。");
            if (text.Length > 5000) throw new ArgumentException("每次最多翻译 5000 个字符，请分段输入。");
            ValidateLanguage(source, true);
            ValidateLanguage(target, false);

            if (source == target)
                return new TranslationResult { Text = text, Provider = "原文", ElapsedMs = 0 };

            // Length prefixes prevent key collisions even when text includes separators.
            string key = source.Length + ":" + source + target.Length + ":" + target + text.Length + ":" + text;
            lock (cacheLock)
            {
                CacheItem hit;
                if (cache.TryGetValue(key, out hit))
                    return new TranslationResult { Text = hit.Text, Provider = hit.Provider + " · 缓存", ElapsedMs = 0 };
            }

            Stopwatch timer = Stopwatch.StartNew();
            string translated = null;
            string provider = "Microsoft";
            bool useFallback = false;
            try
            {
                translated = await TranslateMicrosoftAsync(text, source, target, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                token.ThrowIfCancellationRequested();
                useFallback = true;
            }
            catch (HttpRequestException)
            {
                token.ThrowIfCancellationRequested();
                useFallback = true;
            }
            catch (InvalidOperationException)
            {
                token.ThrowIfCancellationRequested();
                useFallback = true;
            }
            if (useFallback)
            {
                translated = await TranslateFallbackAsync(text, source, target, token).ConfigureAwait(false);
                provider = "Google（备用）";
            }

            token.ThrowIfCancellationRequested();
            timer.Stop();
            translated = TranslationPunctuation.Normalize(translated, target);
            lock (cacheLock)
            {
                if (!cache.ContainsKey(key))
                {
                    while (cache.Count >= CacheCapacity)
                        cache.Remove(cacheOrder.Dequeue());
                    cache.Add(key, new CacheItem { Text = translated, Provider = provider });
                    cacheOrder.Enqueue(key);
                }
            }
            return new TranslationResult { Text = translated, Provider = provider, ElapsedMs = timer.ElapsedMilliseconds };
        }

        /// <summary>
        /// Explicit second Japanese candidate: Google only, independent of the primary chain.
        /// Call only when the user requests an alternative to avoid unnecessary provider traffic.
        /// </summary>
        public async Task<TranslationResult> TranslateAlternativeJapaneseAsync(string text, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (disposed) throw new ObjectDisposedException("TranslationService");
            if (String.IsNullOrWhiteSpace(text)) throw new ArgumentException("请先输入需要翻译的文字。");
            if (text.Length > 5000) throw new ArgumentException("每次最多翻译 5000 个字符，请分段输入。");

            // The nonnumeric prefix is disjoint from all normal source/target cache keys.
            string key = "alternative-japanese:" + text.Length + ":" + text;
            lock (cacheLock)
            {
                CacheItem hit;
                if (cache.TryGetValue(key, out hit))
                    return new TranslationResult { Text = hit.Text, Provider = hit.Provider + " · 缓存", ElapsedMs = 0 };
            }

            Stopwatch timer = Stopwatch.StartNew();
            string translated;
            try
            {
                translated = await TranslateGoogleAsync(text, "zh-Hans", "ja", token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("第二候选翻译连接超时，请稍后重试。");
            }
            catch (HttpRequestException)
            {
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("第二候选翻译暂时无法连接，请检查网络后重试。");
            }
            catch (InvalidOperationException)
            {
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("第二候选翻译暂时不可用，请稍后重试。");
            }

            token.ThrowIfCancellationRequested();
            timer.Stop();
            const string provider = "Google（第二候选）";
            translated = TranslationPunctuation.Normalize(translated, "ja");
            lock (cacheLock)
            {
                if (!cache.ContainsKey(key))
                {
                    while (cache.Count >= CacheCapacity)
                        cache.Remove(cacheOrder.Dequeue());
                    cache.Add(key, new CacheItem { Text = translated, Provider = provider });
                    cacheOrder.Enqueue(key);
                }
            }
            return new TranslationResult { Text = translated, Provider = provider, ElapsedMs = timer.ElapsedMilliseconds };
        }

        private static void ValidateLanguage(string language, bool allowAuto)
        {
            if (language == "zh-Hans" || language == "en" || language == "ja" || (allowAuto && language == "auto")) return;
            throw new ArgumentException("请选择中文、英文或日文。");
        }

        private async Task<string> TranslateMicrosoftAsync(string text, string source, string target, CancellationToken token)
        {
            string from = source == "auto" ? "" : source;
            string url = "https://edge.microsoft.com/translate/translatetext?from=" + Uri.EscapeDataString(from) +
                "&to=" + Uri.EscapeDataString(target) + "&isEnterpriseClient=false";
            JavaScriptSerializer json = CreateSerializer();
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Content = new StringContent(json.Serialize(new string[] { text }), Encoding.UTF8, "application/json");
                string raw = await SendAsync(request, token).ConfigureAwait(false);
                object decoded = Decode(json, raw);
                object first = First(decoded);
                IDictionary<string, object> item = first as IDictionary<string, object>;
                object values;
                if (item == null || !item.TryGetValue("translations", out values))
                    throw new InvalidOperationException("翻译服务返回了无法识别的结果。");
                IDictionary<string, object> translation = First(values) as IDictionary<string, object>;
                object value;
                if (translation == null || !translation.TryGetValue("text", out value) || !(value is string) || String.IsNullOrWhiteSpace((string)value))
                    throw new InvalidOperationException("翻译服务返回了空结果。");
                return (string)value;
            }
        }

        private async Task<string> TranslateFallbackAsync(string text, string source, string target, CancellationToken token)
        {
            try
            {
                return await TranslateGoogleAsync(text, source, target, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("翻译连接超时，请检查网络后重试。");
            }
            catch (HttpRequestException)
            {
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("翻译服务暂时无法连接，请检查网络后重试。");
            }
            catch (InvalidOperationException)
            {
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("翻译服务暂时不可用，请稍后重试。");
            }
        }

        private async Task<string> TranslateGoogleAsync(string text, string source, string target, CancellationToken token)
        {
            string from = source == "zh-Hans" ? "zh-CN" : source;
            string to = target == "zh-Hans" ? "zh-CN" : target;
            string url = "https://translate.googleapis.com/translate_a/single?client=gtx&dt=t&dj=1&ie=UTF-8&sl=" +
                Uri.EscapeDataString(from) + "&tl=" + Uri.EscapeDataString(to) + "&q=" + Uri.EscapeDataString(text);
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                string raw = await SendAsync(request, token).ConfigureAwait(false);
                IDictionary<string, object> root = Decode(CreateSerializer(), raw) as IDictionary<string, object>;
                object sentences;
                if (root == null || !root.TryGetValue("sentences", out sentences))
                    throw new InvalidOperationException("翻译服务返回了无法识别的结果。");
                IEnumerable sequence = sentences as IEnumerable;
                if (sequence == null || sentences is string)
                    throw new InvalidOperationException("翻译服务返回了无法识别的结果。");
                StringBuilder result = new StringBuilder();
                foreach (object segment in sequence)
                {
                    IDictionary<string, object> item = segment as IDictionary<string, object>;
                    object value;
                    if (item != null && item.TryGetValue("trans", out value) && value is string)
                        result.Append((string)value);
                }
                if (String.IsNullOrWhiteSpace(result.ToString()))
                    throw new InvalidOperationException("翻译服务返回了空结果。");
                return result.ToString();
            }
        }

        private async Task<string> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(RequestTimeoutSeconds));
                // ResponseContentRead ensures cancellation also covers a stalled response body.
                using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException("翻译服务暂时无法完成请求。");
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 32 };
        }

        private static object Decode(JavaScriptSerializer json, string raw)
        {
            try { return json.DeserializeObject(raw); }
            catch (ArgumentException) { throw new InvalidOperationException("翻译服务返回了无法识别的结果。"); }
        }

        private static object First(object value)
        {
            IEnumerable items = value as IEnumerable;
            if (items == null || value is string || value is IDictionary) return null;
            foreach (object item in items) return item;
            return null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            client.Dispose();
            lock (cacheLock)
            {
                cache.Clear();
                cacheOrder.Clear();
            }
        }
    }
}
