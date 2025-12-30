using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Verse;

namespace AutoTranslation
{
    public static class NetworkHelper
    {
        private const int DEFAULT_RETRIES = 3;
        private const int BASE_DELAY_MS = 1000;

        public static string Post(string url, string body, Dictionary<string, string> headers = null, string contentType = "application/json", int maxRetries = DEFAULT_RETRIES)
        {
            return ExecuteWithRetry(() =>
            {
                var request = CreateRequest(url, "POST", headers);
                
                // Only set ContentType if not already set by headers and contentType is not null
                if (string.IsNullOrEmpty(request.ContentType) && !string.IsNullOrEmpty(contentType))
                {
                    request.ContentType = contentType;
                }

                // Only write body if it's not empty
                if (!string.IsNullOrEmpty(body))
                {
                    using (var stream = request.GetRequestStream())
                    using (var writer = new StreamWriter(stream, Encoding.UTF8))
                    {
                        writer.Write(body);
                    }
                }
                // For empty body, don't call GetRequestStream() at all - let the framework handle it

                return GetResponseText(request);
            }, maxRetries, url);
        }

        public static string Get(string url, Dictionary<string, string> headers = null, int maxRetries = DEFAULT_RETRIES)
        {
            return ExecuteWithRetry(() =>
            {
                var request = CreateRequest(url, "GET", headers);
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
                    return action();
                }
                catch (WebException ex)
                {
                    if (attempts > maxRetries)
                    {
                        Log.Error($"{AutoTranslation.LogPrefix} Network request failed after {maxRetries} attempts. URL: {context}. Error: {ex.Message}");
                        throw;
                    }

                    var response = ex.Response as HttpWebResponse;
                    if (response != null && (int)response.StatusCode == 429) // Too Many Requests
                    {
                        int delay = BASE_DELAY_MS * (int)Math.Pow(2, attempts - 1); // Exponential backoff
                        Log.Warning($"{AutoTranslation.LogPrefix} Rate limit (429) hit. Retrying in {delay}ms... ({attempts}/{maxRetries})");
                        Thread.Sleep(delay);
                    }
                    else if (ex.Status == WebExceptionStatus.Timeout || ex.Status == WebExceptionStatus.ConnectionClosed)
                    {
                        int delay = BASE_DELAY_MS * attempts;
                        Log.Warning($"{AutoTranslation.LogPrefix} Network error ({ex.Status}). Retrying in {delay}ms... ({attempts}/{maxRetries})");
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
                    Log.Error($"{AutoTranslation.LogPrefix} Unexpected error during request to {context}: {ex}");
                    throw;
                }
            }
        }

        private static WebRequest CreateRequest(string url, string method, Dictionary<string, string> headers)
        {
            var request = WebRequest.Create(url);
            request.Method = method;
            request.Timeout = 30000; // 30s timeout
            
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
    }
}
