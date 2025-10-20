using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using System.Text.Json;
using NoteService.DAL.Models;
using NoteService.DAL;
using Npgsql;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace NoteCrud;

public class Function
{
    private readonly INotesDAL _dal;
    private readonly IAmazonSimpleNotificationService _snsClient;
    private readonly string _topicArn;
    private readonly string _connectionString;
    private static bool _databaseInitialized = false;
    private static readonly SemaphoreSlim _initSemaphore = new SemaphoreSlim(1, 1);

    /// <summary>
    /// Default constructor - used by Lambda runtime
    /// </summary>
    public Function()
    {
        _connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")
            ?? throw new InvalidOperationException("CONNECTION_STRING environment variable is required");

        _dal = new NotesDAL(_connectionString);
        _snsClient = new AmazonSimpleNotificationServiceClient();
        _topicArn = Environment.GetEnvironmentVariable("SNS_TOPIC_ARN") ?? "";

        // Initialize database schema on first cold start
        EnsureDatabaseAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Constructor for testing with dependency injection
    /// </summary>
    public Function(INotesDAL dal, IAmazonSimpleNotificationService snsClient, string topicArn)
    {
        _dal = dal ?? throw new ArgumentNullException(nameof(dal));
        _snsClient = snsClient ?? throw new ArgumentNullException(nameof(snsClient));
        _topicArn = topicArn ?? "";
        _connectionString = "";
    }

    /// <summary>
    /// Ensure database schema exists - runs once per Lambda instance
    /// </summary>
    private async Task EnsureDatabaseAsync()
    {
        // Only initialize once per Lambda instance (cold start)
        if (_databaseInitialized)
            return;

        await _initSemaphore.WaitAsync();
        try
        {
            // Double-check after acquiring semaphore
            if (_databaseInitialized)
                return;

            Console.WriteLine("Initializing database schema...");

            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            var createSchemaSql = @"
                -- Create notes table if not exists
                CREATE TABLE IF NOT EXISTS notes (
                    id UUID PRIMARY KEY,
                    content TEXT NOT NULL,
                    summary TEXT,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

                -- Create indexes
                CREATE INDEX IF NOT EXISTS idx_notes_created_at ON notes(created_at DESC);
                CREATE INDEX IF NOT EXISTS idx_notes_updated_at ON notes(updated_at DESC);
                CREATE INDEX IF NOT EXISTS idx_notes_content ON notes USING gin(to_tsvector('english', content));

                -- Create trigger function for updated_at
                CREATE OR REPLACE FUNCTION update_updated_at_column()
                RETURNS TRIGGER AS $$
                BEGIN
                    NEW.updated_at = CURRENT_TIMESTAMP;
                    RETURN NEW;
                END;
                $$ language 'plpgsql';

                -- Create trigger
                DROP TRIGGER IF EXISTS update_notes_updated_at ON notes;
                CREATE TRIGGER update_notes_updated_at
                    BEFORE UPDATE ON notes
                    FOR EACH ROW
                    EXECUTE FUNCTION update_updated_at_column();
            ";

            using var cmd = new NpgsqlCommand(createSchemaSql, conn);
            await cmd.ExecuteNonQueryAsync();

            _databaseInitialized = true;
            Console.WriteLine("✅ Database schema initialized successfully");
        }
        catch (Exception ex)
        {
            // Log error but don't crash - database might already exist
            Console.WriteLine($"⚠️ Database initialization warning: {ex.Message}");
            // Mark as initialized anyway to avoid retrying on every request
            _databaseInitialized = true;
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    /// <summary>
    /// Main Lambda handler - routes to appropriate CRUD method
    /// </summary>
    public async Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context)
    {
        context.Logger.LogLine($"Request: {request.HttpMethod} {request.Path}");
        context.Logger.LogLine($"Query: {JsonSerializer.Serialize(request.QueryStringParameters)}");

        try
        {
            return request.HttpMethod switch
            {
                "GET" => await HandleGetAsync(request, context),
                "POST" => await HandlePostAsync(request, context),
                "PUT" => await HandlePutAsync(request, context),
                "DELETE" => await HandleDeleteAsync(request, context),
                _ => CreateResponse(405, new { error = "Method not allowed" })
            };
        }
        catch (JsonException ex)
        {
            context.Logger.LogLine($"JSON error: {ex.Message}");
            return CreateResponse(400, new { error = "Invalid JSON in request body" });
        }
        catch (Exception ex)
        {
            context.Logger.LogLine($"Error: {ex.Message}");
            context.Logger.LogLine($"Stack: {ex.StackTrace}");
            return CreateResponse(500, new { error = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Handle GET requests - retrieve note(s)
    /// GET /notes - List all notes with optional search and sort
    /// GET /notes/{id} - Get single note
    /// </summary>
    private async Task<APIGatewayProxyResponse> HandleGetAsync(
        APIGatewayProxyRequest request,
        ILambdaContext context)
    {
        // Check if requesting single note by ID
        if (request.PathParameters != null &&
            request.PathParameters.TryGetValue("id", out var idString))
        {
            return await GetSingleNoteAsync(idString, context);
        }

        // List all notes with optional filtering and sorting
        return await ListNotesAsync(request, context);
    }

    /// <summary>
    /// Get a single note by ID
    /// </summary>
    private async Task<APIGatewayProxyResponse> GetSingleNoteAsync(
        string idString,
        ILambdaContext context)
    {
        if (!Guid.TryParse(idString, out var noteId))
        {
            context.Logger.LogLine($"Invalid GUID: {idString}");
            return CreateResponse(400, new { error = "Invalid note ID format" });
        }

        context.Logger.LogLine($"Getting note: {noteId}");

        var note = await _dal.GetNoteByIdAsync(noteId);

        if (note == null)
        {
            context.Logger.LogLine($"Note not found: {noteId}");
            return CreateResponse(404, new { error = "Note not found" });
        }

        context.Logger.LogLine($"Found note: {noteId}");
        return CreateResponse(200, note);
    }

    /// <summary>
    /// List all notes with optional search and sorting
    /// Query params: ?search=keyword&sortBy=created_at&sortOrder=desc
    /// </summary>
    private async Task<APIGatewayProxyResponse> ListNotesAsync(
        APIGatewayProxyRequest request,
        ILambdaContext context)
    {
        // Extract query parameters
        var searchQuery = GetQueryParameter(request, "search");
        var sortBy = GetQueryParameter(request, "sortBy");
        var sortOrder = GetQueryParameter(request, "sortOrder");

        context.Logger.LogLine($"Search: {searchQuery ?? "none"}");
        context.Logger.LogLine($"Sort: {sortBy ?? "created_at"} {sortOrder ?? "desc"}");

        var notes = await _dal.SearchNotesAsync(searchQuery, sortBy, sortOrder);
        var notesList = notes.ToList();

        context.Logger.LogLine($"Found {notesList.Count} notes");

        return CreateResponse(200, new
        {
            count = notesList.Count,
            notes = notesList
        });
    }

    /// <summary>
    /// Handle POST requests - create new note
    /// POST /notes
    /// Body: { "content": "Note content" }
    /// </summary>
    private async Task<APIGatewayProxyResponse> HandlePostAsync(
        APIGatewayProxyRequest request,
        ILambdaContext context)
    {
        context.Logger.LogLine("Creating new note");

        // Parse request body
        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return CreateResponse(400, new { error = "Request body is required" });
        }

        var noteRequest = JsonSerializer.Deserialize<NoteRequest>(request.Body);

        if (noteRequest == null || string.IsNullOrWhiteSpace(noteRequest.Content))
        {
            return CreateResponse(400, new { error = "Content is required" });
        }

        // Create new note with generated ID
        var note = new Note
        {
            Id = Guid.NewGuid(),
            Content = noteRequest.Content.Trim()
        };

        context.Logger.LogLine($"Creating note: {note.Id}");

        // Save to database
        var result = await _dal.UpsertNoteAsync(note);

        context.Logger.LogLine($"Note created: {result.SavedNote.Id}");

        // Publish to SNS for summarization (always for new notes)
        if (!string.IsNullOrWhiteSpace(_topicArn))
        {
            await PublishNoteEventAsync(result.SavedNote, "CREATE", context);
        }

        return CreateResponse(201, result.SavedNote);
    }

    /// <summary>
    /// Handle PUT requests - update existing note
    /// PUT /notes/{id}
    /// Body: { "content": "Updated content" }
    /// </summary>
    private async Task<APIGatewayProxyResponse> HandlePutAsync(
        APIGatewayProxyRequest request,
        ILambdaContext context)
    {
        // Get note ID from path
        if (request.PathParameters == null ||
            !request.PathParameters.TryGetValue("id", out var idString))
        {
            return CreateResponse(400, new { error = "Note ID is required in path" });
        }

        if (!Guid.TryParse(idString, out var noteId))
        {
            return CreateResponse(400, new { error = "Invalid note ID format" });
        }

        context.Logger.LogLine($"Updating note: {noteId}");

        // Parse request body
        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return CreateResponse(400, new { error = "Request body is required" });
        }

        var noteRequest = JsonSerializer.Deserialize<NoteRequest>(request.Body);

        if (noteRequest == null || string.IsNullOrWhiteSpace(noteRequest.Content))
        {
            return CreateResponse(400, new { error = "Content is required" });
        }

        // Check if note exists
        var existingNote = await _dal.GetNoteByIdAsync(noteId);
        if (existingNote == null)
        {
            context.Logger.LogLine($"Note not found: {noteId}");
            return CreateResponse(404, new { error = "Note not found" });
        }

        // Update note
        var note = new Note
        {
            Id = noteId,
            Content = noteRequest.Content.Trim()
        };

        var result = await _dal.UpsertNoteAsync(note);

        context.Logger.LogLine($"Note updated: {noteId}, content changed: {result.ContentChanged}");

        // Publish to SNS only if content actually changed
        if (result.ContentChanged && !string.IsNullOrWhiteSpace(_topicArn))
        {
            await PublishNoteEventAsync(result.SavedNote, "UPDATE", context);
        }

        return CreateResponse(200, result.SavedNote);
    }

    /// <summary>
    /// Handle DELETE requests - delete note
    /// DELETE /notes/{id}
    /// </summary>
    private async Task<APIGatewayProxyResponse> HandleDeleteAsync(
        APIGatewayProxyRequest request,
        ILambdaContext context)
    {
        // Get note ID from path
        if (request.PathParameters == null ||
            !request.PathParameters.TryGetValue("id", out var idString))
        {
            return CreateResponse(400, new { error = "Note ID is required in path" });
        }

        if (!Guid.TryParse(idString, out var noteId))
        {
            return CreateResponse(400, new { error = "Invalid note ID format" });
        }

        context.Logger.LogLine($"Deleting note: {noteId}");

        var deleted = await _dal.DeleteNoteAsync(noteId);

        if (!deleted)
        {
            context.Logger.LogLine($"Note not found: {noteId}");
            return CreateResponse(404, new { error = "Note not found" });
        }

        context.Logger.LogLine($"Note deleted: {noteId}");

        // Return 204 No Content for successful deletion
        return new APIGatewayProxyResponse
        {
            StatusCode = 204,
            Headers = CreateCorsHeaders()
        };
    }

    /// <summary>
    /// Publish note event to SNS for summarization
    /// </summary>
    private async Task PublishNoteEventAsync(
        Note note,
        string operation,
        ILambdaContext context)
    {
        try
        {
            var noteEvent = new NoteEvent
            {
                NoteId = note.Id,
                Content = note.Content,
                Operation = operation,
                Timestamp = DateTime.UtcNow
            };

            var message = JsonSerializer.Serialize(noteEvent);

            context.Logger.LogLine($"Publishing to SNS: {_topicArn}");

            await _snsClient.PublishAsync(new PublishRequest
            {
                TopicArn = _topicArn,
                Message = message,
                Subject = $"Note {operation}",
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    {
                        "operation",
                        new MessageAttributeValue
                        {
                            DataType = "String",
                            StringValue = operation
                        }
                    },
                    {
                        "noteId",
                        new MessageAttributeValue
                        {
                            DataType = "String",
                            StringValue = note.Id.ToString()
                        }
                    }
                }
            });

            context.Logger.LogLine($"Published event for note: {note.Id}");
        }
        catch (Exception ex)
        {
            // Log error but don't fail the request
            // Summarization is async, so if it fails, the note is still created/updated
            context.Logger.LogLine($"Failed to publish to SNS: {ex.Message}");
        }
    }

    /// <summary>
    /// Create API Gateway response with CORS headers
    /// </summary>
    private APIGatewayProxyResponse CreateResponse(int statusCode, object body)
    {
        return new APIGatewayProxyResponse
        {
            StatusCode = statusCode,
            Body = JsonSerializer.Serialize(body, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            }),
            Headers = CreateCorsHeaders()
        };
    }

    /// <summary>
    /// Create CORS headers for API responses
    /// </summary>
    private Dictionary<string, string> CreateCorsHeaders()
    {
        return new Dictionary<string, string>
        {
            { "Content-Type", "application/json" },
            { "Access-Control-Allow-Origin", "*" },
            { "Access-Control-Allow-Headers", "Content-Type,X-Amz-Date,Authorization,X-Api-Key,X-Amz-Security-Token" },
            { "Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS" }
        };
    }

    /// <summary>
    /// Get query parameter value safely
    /// </summary>
    private string? GetQueryParameter(APIGatewayProxyRequest request, string key)
    {
        if (request.QueryStringParameters == null)
            return null;

        return request.QueryStringParameters.TryGetValue(key, out var value)
            ? value
            : null;
    }
}