using System;

namespace TokenSaver.Vs.Extension.Configuration;

/// <summary>
/// Resolves the SimpleModelServer base URL from the Settings page's HealthUrl, so the chat and
/// SimpleModelServer commands honour the configured endpoint instead of a hardcoded one.
/// </summary>
internal static class SimpleModelServerEndpoint
{
    private static readonly Uri Fallback = new("http://localhost:11435");

    public static Uri BaseUrl
    {
        get
        {
            try
            {
                var healthUrl = BehaviorConfigManager.GetExtensionSettings().SimpleModelServer?.HealthUrl;
                if (string.IsNullOrWhiteSpace(healthUrl))
                {
                    return Fallback;
                }

                var uri = new Uri(healthUrl, UriKind.Absolute);
                return new Uri($"{uri.Scheme}://{uri.Authority}");
            }
            catch (UriFormatException)
            {
                return Fallback;
            }
        }
    }
}
