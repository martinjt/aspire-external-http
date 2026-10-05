using System.Globalization;
using System.Text;

// A stand-in for https://api.unsplash.com. It implements just enough of the
// real API for the web app: GET /photos/random, guarded by the same
// "Authorization: Client-ID <access key>" scheme Unsplash uses.

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var accessKey = builder.Configuration["Unsplash:ApiKey"]
    ?? throw new InvalidOperationException("Unsplash:ApiKey must be configured for the mock.");

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "Unsplash mock is running. Try GET /photos/random with 'Authorization: Client-ID <key>'.");

app.MapGet("/photos/random", (HttpRequest request) =>
{
    if (!IsAuthorized(request, accessKey))
    {
        // Same status and body shape as the real API.
        return Results.Json(new { errors = new[] { "OAuth error: The access token is invalid" } },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    var photo = MockPhotos.All[Random.Shared.Next(MockPhotos.All.Length)];
    var baseUrl = $"{request.Scheme}://{request.Host}";
    var image = $"{baseUrl}/images/{photo.Id}.svg";

    return Results.Json(new
    {
        id = photo.Id,
        width = 1080,
        height = 720,
        color = photo.From,
        description = photo.Description,
        alt_description = photo.Description,
        urls = new
        {
            raw = image,
            full = image,
            regular = $"{image}?w=1080",
            small = $"{image}?w=400",
            thumb = $"{image}?w=200",
        },
        links = new { html = $"{baseUrl}/photos/{photo.Id}", download = image },
        user = new
        {
            username = "mock-photographer",
            name = "Mock Photographer",
            links = new { html = $"{baseUrl}/@mock-photographer" },
        },
    });
})
.WithName("GetRandomPhoto");

// Image URLs are public on the real Unsplash CDN too, so no key is required here.
app.MapGet("/images/{id}.svg", (string id, int? w) =>
{
    var photo = MockPhotos.All.FirstOrDefault(p => p.Id == id);
    return photo is null
        ? Results.NotFound()
        : Results.Content(MockPhotos.RenderSvg(photo, w ?? 1080), "image/svg+xml");
});

app.MapDefaultEndpoints();

app.Run();

static bool IsAuthorized(HttpRequest request, string accessKey)
{
    // Unsplash accepts the key either as a header or as ?client_id=.
    var header = request.Headers.Authorization.ToString();
    if (header.StartsWith("Client-ID ", StringComparison.Ordinal) && header["Client-ID ".Length..] == accessKey)
    {
        return true;
    }

    return request.Query["client_id"] == accessKey;
}

record MockPhoto(string Id, string Description, string From, string To);

static class MockPhotos
{
    public static readonly MockPhoto[] All =
    [
        new("mock-sunset", "a mock sunset over a mock sea", "#ff7e5f", "#feb47b"),
        new("mock-forest", "a mock forest in the mist", "#134e5e", "#71b280"),
        new("mock-glacier", "a mock glacier at dawn", "#83a4d4", "#b6fbff"),
        new("mock-desert", "a mock desert at dusk", "#c06c84", "#f8b195"),
    ];

    public static string RenderSvg(MockPhoto photo, int width)
    {
        var height = width * 2 / 3;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 1080 720">""");
        sb.Append($"""<defs><linearGradient id="g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{photo.From}"/><stop offset="1" stop-color="{photo.To}"/></linearGradient></defs>""");
        sb.Append("""<rect width="1080" height="720" fill="url(#g)"/>""");
        sb.Append("""<circle cx="760" cy="260" r="110" fill="#fff" fill-opacity="0.55"/>""");
        sb.Append("""<path d="M0 560 L240 380 L420 520 L640 330 L1080 600 L1080 720 L0 720 Z" fill="#000" fill-opacity="0.25"/>""");
        sb.Append($"""<text x="40" y="680" font-family="sans-serif" font-size="48" fill="#fff">MOCK · {photo.Id}</text>""");
        sb.Append("</svg>");
        return sb.ToString();
    }
}
