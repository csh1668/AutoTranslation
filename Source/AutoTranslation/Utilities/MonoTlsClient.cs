using System;
using System.IO;
using System.Net;
using System.Text;
#if !RW17
using Mono.Security.Interface;
#endif

namespace AutoTranslation.Utilities
{
#if RW17
    // 1.7에서는 MonoTLS Client가 필요하지 않음 - 아마 네이티브 TLS를 쓸 것이고 그것은 문제가 안될 것임
    public static class MonoTlsClient
    {
        public static bool IsSupported => false;

        public static void Disable() { }

        public static string Get(string url, string userAgent, int timeoutMs, out int statusCode)
        {
            throw new NotSupportedException("Mono TLS is not available on RimWorld 1.7");
        }
    }
#else

    public static class MonoTlsClient
    {
        private static readonly CipherSuiteCode[] BrowserLikeCiphers =
        {
            CipherSuiteCode.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
            CipherSuiteCode.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
            CipherSuiteCode.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
            CipherSuiteCode.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
            CipherSuiteCode.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
            CipherSuiteCode.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,
        };

        private const string ConnectionGroup = "AutoTranslation.BrowserLikeTls";

        private static volatile bool _disabled;
        private static MonoTlsSettings _settings;

        public static bool IsSupported => !_disabled;

        public static void Disable() => _disabled = true;

        public static string Get(string url, string userAgent, int timeoutMs, out int statusCode)
        {
            if (_settings == null)
            {
                var settings = MonoTlsSettings.CopyDefaultSettings();
                settings.EnabledCiphers = BrowserLikeCiphers;
                _settings = settings;
            }

            var provider = MonoTlsProviderFactory.GetProvider();
            var request = MonoTlsProviderFactory.CreateHttpsRequest(new Uri(url), provider, _settings);
            request.Method = "GET";
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.UserAgent = userAgent;
            request.ConnectionGroupName = ConnectionGroup;

            HttpWebResponse response;
            try
            {
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (WebException e) when (e.Response is HttpWebResponse errorResponse)
            {
                response = errorResponse;
            }

            using (response)
            using (var stream = response.GetResponseStream())
            {
                statusCode = (int)response.StatusCode;
                if (stream == null) return string.Empty;
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
#endif
}
