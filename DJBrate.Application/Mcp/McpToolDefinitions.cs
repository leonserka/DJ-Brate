using System.Text.Json;
using DJBrate.Application.Models.Ai;

namespace DJBrate.Application.Mcp;

public static class McpToolDefinitions
{
    public static List<AiToolDefinition> GetAllTools() =>
    [
        new AiToolDefinition
        {
            Name = ToolNames.GetUserTopTracks,
            Description = "OPTIONAL context tool. Returns the user's top tracks from Spotify. Only call this if the user explicitly asks the playlist to reference, extend, or feel similar to what they already listen to. If the user is asking for discovery, new music, or a specific vibe unrelated to their history, DO NOT call this.",
            Parameters = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "time_range": {
                        "type": "string",
                        "enum": ["short_term", "medium_term", "long_term"],
                        "description": "short_term = last 4 weeks, medium_term = last 6 months, long_term = all time"
                    }
                },
                "required": ["time_range"]
            }
            """)
        },
        new AiToolDefinition
        {
            Name = ToolNames.GetUserTopArtists,
            Description = "OPTIONAL context tool. Returns the user's top artists and their genres. Only call this if the user explicitly asks the playlist to be based on, similar to, or inspired by their own listening history. For pure discovery or prompt-driven playlists, DO NOT call this.",
            Parameters = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "time_range": {
                        "type": "string",
                        "enum": ["short_term", "medium_term", "long_term"],
                        "description": "short_term = last 4 weeks, medium_term = last 6 months, long_term = all time"
                    }
                },
                "required": ["time_range"]
            }
            """)
        }
    ];

    public static class ToolNames
    {
        public const string GetUserTopTracks  = "get_user_top_tracks";
        public const string GetUserTopArtists = "get_user_top_artists";
    }
}
