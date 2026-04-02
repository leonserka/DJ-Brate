using System.Security.Claims;
using DJBrate.Application.Interfaces;
using DJBrate.Application.Services;
using DJBrate.Domain.Entities;
using DJBrate.Domain.Interfaces;
using DJBrate.Infrastructure.Data;
using DJBrate.Infrastructure.Repositories;
using DJBrate.Infrastructure.Spotify;
using DJBrate.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using DJBrate.Application.Models.Spotify;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath        = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan   = TimeSpan.FromDays(SpotifyConstants.CookieExpiryDays);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient();

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IMoodSessionRepository, MoodSessionRepository>();
builder.Services.AddScoped<IPlaylistRepository, PlaylistRepository>();
builder.Services.AddScoped<IUserTopTrackRepository, UserTopTrackRepository>();
builder.Services.AddScoped<IUserTopArtistRepository, UserTopArtistRepository>();

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IMoodSessionService, MoodSessionService>();
builder.Services.AddScoped<IPlaylistService, PlaylistService>();
builder.Services.AddScoped<ISpotifyTokenService, SpotifyTokenService>();
builder.Services.AddScoped<ISpotifyDataSyncService, SpotifyDataSyncService>();
builder.Services.AddScoped<ISpotifyApiClient, SpotifyApiClient>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/auth/spotify/login", (HttpContext ctx, IConfiguration config) =>
{
    var state = Guid.NewGuid().ToString("N");
    ctx.Response.Cookies.Append(SpotifyConstants.OAuthStateCookie, state, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        MaxAge   = TimeSpan.FromMinutes(SpotifyConstants.OAuthStateCookieMaxAgeMinutes)
    });

    var clientId    = config["Spotify:ClientId"];
    var redirectUri = Uri.EscapeDataString(config["Spotify:RedirectUri"]!);
    var scopes      = Uri.EscapeDataString(SpotifyConstants.Scopes);

    var url = $"{SpotifyConstants.AuthorizeUrl}" +
              $"?client_id={clientId}" +
              $"&response_type=code" +
              $"&redirect_uri={redirectUri}" +
              $"&scope={scopes}" +
              $"&state={state}";

    return Results.Redirect(url);
});

app.MapGet("/auth/spotify/callback", async (
    HttpContext ctx,
    IConfiguration config,
    IUserService userService,
    ISpotifyTokenService tokenService,
    ISpotifyApiClient spotifyClient,
    ISpotifyDataSyncService syncService) =>
{
    var code  = ctx.Request.Query["code"].ToString();
    var state = ctx.Request.Query["state"].ToString();
    var error = ctx.Request.Query["error"].ToString();

    if (!string.IsNullOrEmpty(error))
        return Results.Redirect("/login?spotifyError=access_denied");

    var savedState = ctx.Request.Cookies[SpotifyConstants.OAuthStateCookie];
    if (string.IsNullOrEmpty(savedState) || savedState != state)
        return Results.Redirect("/login?spotifyError=invalid_state");

    ctx.Response.Cookies.Delete(SpotifyConstants.OAuthStateCookie);

    var tokens  = await tokenService.ExchangeCodeForTokensAsync(code, config["Spotify:RedirectUri"]!);
    var profile = await spotifyClient.GetProfileAsync(tokens.AccessToken);

    var spotifyId   = profile.Id;
    var displayName = profile.DisplayName ?? spotifyId;
    var email       = profile.Email ?? $"{spotifyId}{SpotifyConstants.PlaceholderEmailSuffix}";
    var avatarUrl   = profile.Images.FirstOrDefault()?.Url;

    var existingUser = await userService.GetUserBySpotifyIdAsync(spotifyId);
    var needsSync = existingUser is null
        || !existingUser.LastLoginAt.HasValue
        || existingUser.LastLoginAt < DateTime.UtcNow.AddHours(-SpotifyConstants.SyncIntervalHours);

    var user = await userService.CreateOrUpdateUserAsync(new User
    {
        SpotifyId           = spotifyId,
        DisplayName         = displayName,
        Email               = email,
        AvatarUrl           = avatarUrl,
        SpotifyAccessToken  = tokens.AccessToken,
        SpotifyRefreshToken = tokens.RefreshToken,
        TokenExpiresAt      = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn),
        Role                = SpotifyConstants.DefaultUserRole
    });

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Name,           user.DisplayName),
        new(ClaimTypes.Email,          user.Email),
        new(ClaimTypes.Role,           user.Role)
    };
    var principal = new ClaimsPrincipal(
        new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

    if (needsSync)
        await syncService.SyncUserTopDataAsync(user.Id);

    return Results.Redirect("/");
});

app.MapGet("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
