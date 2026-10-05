using System.Net.Http.Headers;
using System.Text.Json.Serialization;

namespace ExternalHttp.Web;

// The app neither knows nor cares whether "unsplash" is the real API or the
// mock: Aspire service discovery resolves the name, and the key is just config.
public class UnsplashClient(HttpClient httpClient)
{
    public static void Configure(HttpClient client, string accessKey)
    {
        // This URL uses "https+http://" to indicate HTTPS is preferred over HTTP.
        // Learn more about service discovery scheme resolution at https://aka.ms/dotnet/sdschemes.
        client.BaseAddress = new("https+http://unsplash");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Client-ID", accessKey);
        client.DefaultRequestHeaders.Add("Accept-Version", "v1");
    }

    public Task<UnsplashPhoto?> GetRandomPhotoAsync(CancellationToken cancellationToken = default) =>
        httpClient.GetFromJsonAsync<UnsplashPhoto>("photos/random?orientation=landscape", cancellationToken);
}

public record UnsplashPhoto(
    string Id,
    [property: JsonPropertyName("alt_description")] string? AltDescription,
    UnsplashUrls Urls,
    UnsplashUser User,
    UnsplashLinks Links);

public record UnsplashUrls(string Regular, string Small);

public record UnsplashUser(string Name, UnsplashLinks Links);

public record UnsplashLinks(string Html);
