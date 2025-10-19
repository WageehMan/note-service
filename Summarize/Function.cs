using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using NoteService.DAL;
using NoteService.DAL.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Summarize;

public class Function
{
    private readonly INotesDAL _dal;
    private readonly IAmazonBedrockRuntime _bedrockClient;
    private readonly string _modelId;

    /// <summary>
    /// Constructor - initializes dependencies
    /// </summary>
    public Function()
    {
        var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")
            ?? throw new InvalidOperationException("CONNECTION_STRING environment variable is required");

        _dal = new NotesDAL(connectionString);
        _bedrockClient = new AmazonBedrockRuntimeClient();
        _modelId = Environment.GetEnvironmentVariable("BEDROCK_MODEL_ID")
            ?? "anthropic.claude-3-haiku-20240307-v1:0";
    }

    /// <summary>
    /// Lambda handler - processes SQS messages
    /// </summary>
    public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        context.Logger.LogLine($"Processing {sqsEvent.Records.Count} message(s)");

        foreach (var record in sqsEvent.Records)
        {
            await ProcessMessageAsync(record, context);
        }
    }

    /// <summary>
    /// Process a single SQS message
    /// </summary>
    private async Task ProcessMessageAsync(SQSEvent.SQSMessage message, ILambdaContext context)
    {
        context.Logger.LogLine($"Processing message: {message.MessageId}");

        try
        {
            // Deserialize the note event from SNS message
            var noteEvent = JsonSerializer.Deserialize<NoteEvent>(message.Body);

            if (noteEvent == null)
            {
                context.Logger.LogLine("Failed to deserialize message body");
                throw new InvalidOperationException("Invalid message format");
            }

            context.Logger.LogLine($"Note ID: {noteEvent.NoteId}");
            context.Logger.LogLine($"Content length: {noteEvent.Content?.Length ?? 0} characters");

            // Validate content
            if (string.IsNullOrWhiteSpace(noteEvent.Content))
            {
                context.Logger.LogLine("Note content is empty, skipping summarization");
                return;
            }

            // Generate summary using AWS Bedrock
            context.Logger.LogLine("Generating summary with Bedrock...");
            var summary = await GenerateSummaryAsync(noteEvent.Content, context);

            if (string.IsNullOrWhiteSpace(summary))
            {
                context.Logger.LogLine("Failed to generate summary");
                throw new InvalidOperationException("Summary generation failed");
            }

            context.Logger.LogLine($"Generated summary: {summary}");

            // Update note with summary using DAL
            // This is idempotent - only updates if summary is empty
            await _dal.UpdateSummaryIfEmptyAsync(noteEvent.NoteId, summary);

            context.Logger.LogLine($"Successfully updated note {noteEvent.NoteId} with summary");
        }
        catch (JsonException ex)
        {
            context.Logger.LogLine($"JSON deserialization error: {ex.Message}");
            throw; // Re-throw to send to DLQ
        }
        catch (Exception ex)
        {
            context.Logger.LogLine($"Error processing message: {ex.Message}");
            context.Logger.LogLine($"Stack trace: {ex.StackTrace}");
            throw; // Re-throw to send to DLQ
        }
    }

    /// <summary>
    /// Generate summary using AWS Bedrock Claude model
    /// </summary>
    private async Task<string> GenerateSummaryAsync(string content, ILambdaContext context)
    {
        try
        {
            // Truncate content if too long (Claude has token limits)
            var truncatedContent = content.Length > 2000
                ? content.Substring(0, 2000) + "..."
                : content;

            // Build Bedrock request
            var requestBody = new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens = 150,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = $"Summarize this note in 1-2 sentences:\n\n{truncatedContent}"
                    }
                }
            };

            var requestJson = JsonSerializer.Serialize(requestBody);
            context.Logger.LogLine($"Bedrock request: {requestJson}");

            // Invoke Bedrock
            var request = new InvokeModelRequest
            {
                ModelId = _modelId,
                Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(requestJson)),
                ContentType = "application/json"
            };

            var response = await _bedrockClient.InvokeModelAsync(request);

            // Parse response
            using var reader = new StreamReader(response.Body);
            var responseBody = await reader.ReadToEndAsync();
            context.Logger.LogLine($"Bedrock response: {responseBody}");

            var result = JsonSerializer.Deserialize<BedrockResponse>(responseBody);

            var summaryText = result?.Content?.FirstOrDefault()?.Text?.Trim();

            return summaryText ?? "Unable to generate summary";
        }
        catch (AmazonBedrockRuntimeException ex)
        {
            context.Logger.LogLine($"Bedrock API error: {ex.Message}");
            context.Logger.LogLine($"Error code: {ex.ErrorCode}");
            return $"Error: {ex.Message}";
        }
        catch (Exception ex)
        {
            context.Logger.LogLine($"Unexpected error generating summary: {ex.Message}");
            return "Error generating summary";
        }
    }
}

/// <summary>
/// Response from Bedrock API
/// </summary>
public class BedrockResponse
{
    [JsonPropertyName("content")]
    public List<ContentBlock>? Content { get; set; }

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }
}

/// <summary>
/// Content block in Bedrock response
/// </summary>
public class ContentBlock
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
