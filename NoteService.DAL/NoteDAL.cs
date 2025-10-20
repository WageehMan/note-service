using Dapper;
using NoteService.DAL.Models;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NoteService.DAL
{
    public class NotesDAL : INotesDAL
    {
        private readonly string _connectionString;

        public NotesDAL(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            DapperConfig.Initialize();
        }

        /// <summary>
        /// Upsert a note and return whether content changed
        /// </summary>
        public async Task<UpsertResult> UpsertNoteAsync(Note note)
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            // Get existing note if it exists
            var existing = await connection.QuerySingleOrDefaultAsync<Note>(@"
            SELECT id, content, summary, created_at, updated_at 
            FROM notes 
            WHERE id = @Id
        ", new { note.Id });

            // Perform upsert
            var savedNote = await connection.QuerySingleAsync<Note>(@"
            INSERT INTO notes (id, content, created_at, updated_at)
            VALUES (@Id, @Content, NOW(), NOW())
            ON CONFLICT (id) DO UPDATE 
            SET content = EXCLUDED.content,
                updated_at = NOW()
            RETURNING id, content, summary, created_at, updated_at
        ", note);

            // Determine if content changed
            bool contentChanged = existing == null || existing.Content != note.Content;

            return new UpsertResult
            {
                SavedNote = savedNote,
                ContentChanged = contentChanged
            };
        }

        /// <summary>
        /// Update summary only if it's currently empty
        /// This is idempotent - safe to call multiple times
        /// </summary>
        public async Task UpdateSummaryIfEmptyAsync(Guid noteId, string summary)
        {
            using var connection = new NpgsqlConnection(_connectionString);

            var rowsAffected = await connection.ExecuteAsync(@"
            UPDATE notes 
            SET summary = @summary, 
                updated_at = NOW()
            WHERE id = @noteId 
              AND (summary IS NULL OR summary = '')
        ", new { noteId, summary });

            // Log for debugging (optional)
            if (rowsAffected == 0)
            {
                // Summary already exists or note not found
                // This is OK - makes the operation idempotent
            }
        }

        /// <summary>
        /// Get a single note by ID
        /// </summary>
        public async Task<Note?> GetNoteByIdAsync(Guid id)
        {
            using var connection = new NpgsqlConnection(_connectionString);

            return await connection.QuerySingleOrDefaultAsync<Note>(@"
            SELECT id, content, summary, created_at, updated_at
            FROM notes
            WHERE id = @id
        ", new { id });
        }

        /// <summary>
        /// Search notes with optional filtering and sorting
        /// </summary>
        public async Task<IEnumerable<Note>> SearchNotesAsync(
            string? query,
            string? sortBy,
            string? sortOrder)
        {
            using var connection = new NpgsqlConnection(_connectionString);

            // Build dynamic SQL (safe with parameterized queries)
            var sql = "SELECT id, content, summary, created_at, updated_at FROM notes";
            var parameters = new DynamicParameters();

            // Add search filter if provided
            if (!string.IsNullOrWhiteSpace(query))
            {
                sql += " WHERE content ILIKE @query OR summary ILIKE @query";
                parameters.Add("query", $"%{query}%");
            }

            // Add sorting
            var validSortColumns = new[] { "created_at", "updated_at", "content" };
            var sortColumn = !string.IsNullOrWhiteSpace(sortBy) &&
                   validSortColumns.Contains(sortBy.ToLower())
                ? sortBy.ToLower() 
                : "created_at";

            var sortDirection = sortOrder?.ToUpper() == "ASC" ? "ASC" : "DESC";

            sql += $" ORDER BY {sortColumn} {sortDirection}";

            return await connection.QueryAsync<Note>(sql, parameters);
        }

        /// <summary>
        /// Delete a note by ID
        /// </summary>
        public async Task<bool> DeleteNoteAsync(Guid id)
        {
            using var connection = new NpgsqlConnection(_connectionString);

            var rowsAffected = await connection.ExecuteAsync(@"
            DELETE FROM notes 
            WHERE id = @id
        ", new { id });

            return rowsAffected > 0;
        }

        /// <summary>
        /// Get all notes (for admin/debugging)
        /// </summary>
        public async Task<IEnumerable<Note>> GetAllNotesAsync()
        {
            using var connection = new NpgsqlConnection(_connectionString);

            return await connection.QueryAsync<Note>(@"
            SELECT id, content, summary, created_at, updated_at
            FROM notes
            ORDER BY created_at DESC
        ");
        }

        /// <summary>
        /// Check if a note exists
        /// </summary>
        public async Task<bool> NoteExistsAsync(Guid id)
        {
            using var connection = new NpgsqlConnection(_connectionString);

            var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) 
            FROM notes 
            WHERE id = @id
        ", new { id });

            return count > 0;
        }

        /// <summary>
        /// Get notes that need summarization (no summary yet)
        /// Useful for batch processing or retries
        /// </summary>
        public async Task<IEnumerable<Note>> GetNotesNeedingSummarizationAsync(int limit = 100)
        {
            using var connection = new NpgsqlConnection(_connectionString);

            return await connection.QueryAsync<Note>(@"
            SELECT id, content, summary, created_at, updated_at
            FROM notes
            WHERE summary IS NULL OR summary = ''
            ORDER BY created_at DESC
            LIMIT @limit
        ", new { limit });
        }

        /// <summary>
        /// Update summary (force update even if exists)
        /// Use this only when you want to regenerate summaries
        /// </summary>
        public async Task UpdateSummaryAsync(Guid noteId, string summary)
        {
            using var connection = new NpgsqlConnection(_connectionString);

            await connection.ExecuteAsync(@"
            UPDATE notes 
            SET summary = @summary, 
                updated_at = NOW()
            WHERE id = @noteId
        ", new { noteId, summary });
        }
    }

    public static class DapperConfig
    {
        private static bool _initialized = false;

        public static void Initialize()
        {
            if (_initialized) return;

            // Map PascalCase to snake_case
            DefaultTypeMap.MatchNamesWithUnderscores = true;

            _initialized = true;
        }
    }
}
