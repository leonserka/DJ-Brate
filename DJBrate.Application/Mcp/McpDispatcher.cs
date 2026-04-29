using System.Diagnostics;
using System.Text.Json;
using DJBrate.Application.Models.Spotify;
using DJBrate.Domain.Entities;
using DJBrate.Domain.Interfaces;

namespace DJBrate.Application.Mcp;

public class McpDispatcher
{
    private readonly IUserTopTrackRepository _topTrackRepo;
    private readonly IUserTopArtistRepository _topArtistRepo;
    private readonly IMcpToolCallRepository _toolCallRepo;

    public McpDispatcher(
        IUserTopTrackRepository topTrackRepo,
        IUserTopArtistRepository topArtistRepo,
        IMcpToolCallRepository toolCallRepo)
    {
        _topTrackRepo  = topTrackRepo;
        _topArtistRepo = topArtistRepo;
        _toolCallRepo  = toolCallRepo;
    }

    public async Task<string> ExecuteToolAsync(Guid sessionId, User user, string toolName, JsonDocument arguments)
    {
        var sw = Stopwatch.StartNew();
        var toolCall = new McpToolCall
        {
            SessionId       = sessionId,
            ToolName        = toolName,
            InputParameters = arguments
        };

        var result = toolName switch
        {
            McpToolDefinitions.ToolNames.GetUserTopTracks  => await HandleGetTopTracks(user, arguments),
            McpToolDefinitions.ToolNames.GetUserTopArtists => await HandleGetTopArtists(user, arguments),
            _ => throw new ArgumentException($"Unknown tool: {toolName}")
        };

        sw.Stop();
        toolCall.Success      = true;
        toolCall.OutputResult = JsonDocument.Parse(result);
        toolCall.DurationMs   = (int)sw.ElapsedMilliseconds;
        await _toolCallRepo.AddAsync(toolCall);

        return result;
    }

    private async Task<string> HandleGetTopTracks(User user, JsonDocument args)
    {
        var timeRange = ParseTimeRange(args);
        var tracks = await _topTrackRepo.GetByUserAndTimeRangeAsync(user.Id, timeRange.ToApiString());

        var result = tracks.Select(t => new
        {
            spotify_id = t.SpotifyTrackId,
            name       = t.TrackName,
            artist     = t.ArtistName,
            rank       = t.RankPosition
        });

        return JsonSerializer.Serialize(result);
    }

    private async Task<string> HandleGetTopArtists(User user, JsonDocument args)
    {
        var timeRange = ParseTimeRange(args);
        var artists = await _topArtistRepo.GetByUserAndTimeRangeAsync(user.Id, timeRange.ToApiString());

        var result = artists.Select(a => new
        {
            spotify_id = a.SpotifyArtistId,
            name       = a.ArtistName,
            genres     = a.Genres ?? [],
            rank       = a.RankPosition
        });

        return JsonSerializer.Serialize(result);
    }

    private static SpotifyTimeRange ParseTimeRange(JsonDocument args)
    {
        if (args.RootElement.TryGetProperty("time_range", out var tr))
        {
            return tr.GetString() switch
            {
                "short_term" => SpotifyTimeRange.ShortTerm,
                "long_term"  => SpotifyTimeRange.LongTerm,
                _            => SpotifyTimeRange.MediumTerm
            };
        }
        return SpotifyTimeRange.MediumTerm;
    }
}
