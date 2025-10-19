using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NoteService.DAL.Models
{
    public class Note
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Result of upsert operation
    /// Includes the saved note and whether content changed
    /// </summary>
    public class UpsertResult
    {
        public Note SavedNote { get; set; } = new();
        public bool ContentChanged { get; set; }
    }

    /// <summary>
    /// Request model for creating/updating notes
    /// </summary>
    public class NoteRequest
    {
        [JsonPropertyName("id")]
        public Guid? Id { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>
    /// Event published to SNS after note creation/update
    /// </summary>
    public class NoteEvent
    {
        [JsonPropertyName("noteId")]
        public Guid NoteId { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("operation")]
        public string Operation { get; set; } = string.Empty; // CREATE, UPDATE

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
