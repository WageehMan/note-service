using NoteService.DAL.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NoteService.DAL
{
    public interface INotesDAL
    {
        /// <summary>
        /// Create or update a note, returns whether content changed
        /// </summary>
        Task<UpsertResult> UpsertNoteAsync(Note note);

        /// <summary>
        /// Update summary only if empty (idempotent)
        /// Used by Summarize Lambda
        /// </summary>
        Task UpdateSummaryIfEmptyAsync(Guid noteId, string summary);

        /// <summary>
        /// Get a single note by ID
        /// </summary>
        Task<Note?> GetNoteByIdAsync(Guid id);

        /// <summary>
        /// Search notes with optional query and sorting
        /// </summary>
        Task<IEnumerable<Note>> SearchNotesAsync(string? query, string? sortBy, string? sortOrder);

        /// <summary>
        /// Delete a note by ID
        /// </summary>
        Task<bool> DeleteNoteAsync(Guid id);

        /// <summary>
        /// Get all notes
        /// </summary>
        Task<IEnumerable<Note>> GetAllNotesAsync();

        /// <summary>
        /// Check if note exists
        /// </summary>
        Task<bool> NoteExistsAsync(Guid id);

        /// <summary>
        /// Get notes that need summarization
        /// </summary>
        Task<IEnumerable<Note>> GetNotesNeedingSummarizationAsync(int limit = 100);

        /// <summary>
        /// Force update summary (even if exists)
        /// </summary>
        Task UpdateSummaryAsync(Guid noteId, string summary);
    }
}