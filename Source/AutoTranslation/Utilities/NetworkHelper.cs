using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using AutoTranslation.Services;
using Verse;

namespace AutoTranslation.Utilities
{
    public static class NetworkHelper
    {
        private const int DEFAULT_RETRIES = 5;
        private const int BASE_DELAY_MS = 5000;
        private const int RATE_LIMIT_DELAY_MS = 30000; // 30 seconds for 429 errors
        public const int DEFAULT_TIMEOUT_MS = 30000;

        static NetworkHelper()
        {
            try
            {
                // Unity Mono may default to TLS 1.0/1.1 only; OR-in TLS 1.2 (do not overwrite -
                // overwriting could break other mods' requests)
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                // Some non-standard servers (Papago/Yandex) mishandle Expect: 100-continue on POST
                ServicePointManager.Expect100Continue = false;
            }
            catch (Exception e)
            {
                Log.Warning($"{AutoTranslation.LogPrefix} Failed to configure ServicePointManager: {e.Message}");
            }
        }

        public static string Post(string url, string body, Dictionary<string, string> headers = null, string contentType = "application/json", int maxRetries = DEFAULT_RETRIES, int timeoutMs = DEFAULT_TIMEOUT_MS)
        {
            return ExecuteWithRetry(() =>
            {
                var request = CreateRequest(url, "POST", headers, timeoutMs);
                
                // Only set ContentType if not already set by headers and contentType is not null
                if (string.IsNullOrEmpty(request.ContentType) && !string.IsNullOrEmpty(contentType))
                {
                    request.ContentType = contentType;
                }

                // Only write body if it's not empty
                if (!string.IsNullOrEmpty(body))
                {
                    byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                    request.ContentLength = bodyBytes.Length;
                    
                    using (var stream = request.GetRequestStream())
                    {
                        stream.Write(bodyBytes, 0, bodyBytes.Length);
                    }
                }
                // For empty body, don't call GetRequestStream() at all - let the framework handle it

                return GetResponseText(request);
            }, maxRetries, url);
        }

        public static string Get(string url, Dictionary<string, string> headers = null, int maxRetries = DEFAULT_RETRIES, int timeoutMs = DEFAULT_TIMEOUT_MS)
        {
            return ExecuteWithRetry(() =>
            {
                var request = CreateRequest(url, "GET", headers, timeoutMs);
                return GetResponseText(request);
            }, maxRetries, url);
        }

        private static string ExecuteWithRetry(Func<string> action, int maxRetries, string context)
        {
            int attempts = 0;
            while (true)
            {
                try
                {
                    attempts++;
                    var result = action();
                    NetworkStateMonitor.ReportSuccess();
                    return result;
                }
                catch (WebException ex)
                {
                    if (IsConnectionLevelFailure(ex))
                    {
                        NetworkStateMonitor.ReportFailure();
                    }
                    else
                    {
                        // The server responded (401/404/429/...) - the network itself is up
                        NetworkStateMonitor.ReportSuccess();
                    }

                    // While the circuit is open, fail fast: no inner retries, the queue is paused anyway
                    if (NetworkStateMonitor.IsOpen) throw;

                    if (attempts > maxRetries)
                    {
                        Log.Warning($"{AutoTranslation.LogPrefix} Network request failed after {maxRetries} attempts. URL: {context}. Error: {ex.Message}");
                        throw;
                    }

                    var response = ex.Response as HttpWebResponse;
                    if (response != null && (int)response.StatusCode == 429) // Too Many Requests
                    {
                        // For rate limits, use minimum 30 seconds with exponential backoff
                        int exponentialDelay = BASE_DELAY_MS * (int)Math.Pow(2, attempts - 1);
                        int delay = Math.Max(RATE_LIMIT_DELAY_MS, exponentialDelay);
                        Log.Warning($"{AutoTranslation.LogPrefix} Rate limit (429) hit. Retrying in {delay}ms ({delay/1000}s)... ({attempts}/{maxRetries})");
                        Thread.Sleep(delay);
                    }
                    else if (ex.Status == WebExceptionStatus.Timeout || ex.Status == WebExceptionStatus.ConnectionClosed)
                    {
                        int delay = BASE_DELAY_MS * (int)Math.Pow(2, attempts - 1); // Exponential backoff
                        Log.Warning($"{AutoTranslation.LogPrefix} Network error ({ex.Status}). Retrying in {delay}ms ({delay/1000}s)... ({attempts}/{maxRetries})");
                        Thread.Sleep(delay);
                    }
                    else
                    {
                        // Fatal error (e.g. 401 Unauthorized, 404 Not Found) - do not retry
                        throw; 
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"{AutoTranslation.LogPrefix} Unexpected error during request to {context}: {ex}");
                    throw;
                }
            }
        }

        private static WebRequest CreateRequest(string url, string method, Dictionary<string, string> headers, int timeoutMs)
        {
            var request = WebRequest.Create(url);
            request.Method = method;
            request.Timeout = timeoutMs;
            
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    // Handle restricted headers with their specific properties
                    if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        request.ContentType = header.Value;
                    }
                    else if (header.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase) && request is HttpWebRequest httpReq)
                    {
                        httpReq.UserAgent = header.Value;
                    }
                    else if (header.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase) && request is HttpWebRequest httpReq2)
                    {
                        httpReq2.Accept = header.Value;
                    }
                    else if (header.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase) && request is HttpWebRequest httpReq3)
                    {
                        httpReq3.Referer = header.Value;
                    }
                    else if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                    {
                        request.Headers.Add(HttpRequestHeader.Authorization, header.Value);
                    }
                    else
                    {
                        try
                        {
                            request.Headers.Add(header.Key, header.Value);
                        }
                        catch (ArgumentException)
                        {
                            // Skip restricted headers that we haven't handled explicitly
                            Log.Warning($"{AutoTranslation.LogPrefix} Skipping restricted header: {header.Key}");
                        }
                    }
                }
            }

            return request;
        }

        private static string GetResponseText(WebRequest request)
        {
            using (var response = request.GetResponse())
            using (var stream = response.GetResponseStream())
            {
                if (stream == null) return string.Empty;
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        /// <summary>
        /// True when the failure indicates the endpoint could not be reached at all
        /// (offline, DNS failure, refused connection) as opposed to a server-side error response.
        /// </summary>
        public static bool IsConnectionLevelFailure(WebException ex)
        {
            return ex.Status == WebExceptionStatus.NameResolutionFailure ||
                   ex.Status == WebExceptionStatus.ConnectFailure ||
                   ex.Status == WebExceptionStatus.Timeout ||
                   ex.Status == WebExceptionStatus.ConnectionClosed ||
                   ex.Status == WebExceptionStatus.ReceiveFailure ||
                   ex.Status == WebExceptionStatus.SendFailure;
        }

        /// <summary>
        /// Extracts a human-readable error from an exception, including the API error body
        /// (e.g. Gemini's "API key not valid") when the server returned one.
        /// </summary>
        public static string ExtractErrorMessage(Exception e)
        {
            if (e is WebException webEx)
            {
                try
                {
                    if (webEx.Response is HttpWebResponse resp)
                    {
                        string body;
                        using (var stream = resp.GetResponseStream())
                        {
                            if (stream == null) return $"HTTP {(int)resp.StatusCode} {resp.StatusCode}";
                            using (var reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                body = reader.ReadToEnd();
                            }
                        }

                        var message = body.GetStringValueFromJson("message") ?? body.GetStringValueFromJson("error");
                        var prefix = $"HTTP {(int)resp.StatusCode}";
                        if (!string.IsNullOrEmpty(message)) return $"{prefix}: {message}";
                        if (!string.IsNullOrEmpty(body)) return $"{prefix}: {body.Substring(0, Math.Min(200, body.Length))}";
                        return prefix;
                    }
                }
                catch
                {
                    // fall through to status-based message
                }
                return $"{webEx.Status}: {webEx.Message}";
            }
            return e.Message;
        }
    }
}

