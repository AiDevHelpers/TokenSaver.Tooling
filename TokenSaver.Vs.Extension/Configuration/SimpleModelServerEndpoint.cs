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
                if (healthUrl is null || !healthUrl.IsAbsoluteUri)
                {
                    return Fallback;
                }

                return new Uri($"{healthUrl.Scheme}://{healthUrl.Authority}");
            }
            catch (UriFormatException)
            {
                return Fallback;
            }
        }
    }
}
