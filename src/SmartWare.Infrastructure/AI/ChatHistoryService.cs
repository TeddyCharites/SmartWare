using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartWare.Application.AI.Chatbot;
using SmartWare.Domain.Entities;
using SmartWare.Infrastructure.Data;

namespace SmartWare.Infrastructure.AI;

internal sealed class ChatHistoryService(ApplicationDbContext dbContext) : IChatHistoryService
{
    public async Task<IReadOnlyList<ChatSessionSummary>> GetSessionsAsync(
        string userId,
        string accessRole,
        CancellationToken cancellationToken = default) =>
        await dbContext.ChatSessions
            .AsNoTracking()
            .Where(session => session.UserId == userId && session.AccessRole == accessRole)
            .OrderByDescending(session => session.UpdatedAt)
            .Take(50)
            .Select(session => new ChatSessionSummary(
                session.ChatSessionId,
                session.Title,
                session.UpdatedAt,
                session.Messages.Count))
            .ToListAsync(cancellationToken);

    public async Task<ChatSessionDetails?> GetSessionAsync(
        string userId,
        string accessRole,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await dbContext.ChatSessions
            .AsNoTracking()
            .Where(item =>
                item.ChatSessionId == sessionId &&
                item.UserId == userId &&
                item.AccessRole == accessRole)
            .Select(item => new
            {
                item.ChatSessionId,
                item.Title,
                Messages = item.Messages
                    .OrderBy(message => message.CreatedAt)
                    .ThenBy(message => message.ChatMessageId)
                    .Select(message => new
                    {
                        message.ChatMessageId,
                        message.Role,
                        message.Content,
                        message.SourcesJson,
                        message.IsAiGenerated,
                        message.CreatedAt
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (session is null)
        {
            return null;
        }

        return new ChatSessionDetails(
            session.ChatSessionId,
            session.Title,
            session.Messages.Select(message => new ChatHistoryMessage(
                message.ChatMessageId,
                message.Role,
                message.Content,
                DeserializeSources(message.SourcesJson),
                message.IsAiGenerated,
                message.CreatedAt)).ToArray());
    }

    public async Task<IReadOnlyList<ChatHistoryMessage>?> GetRecentMessagesAsync(
        string userId,
        string accessRole,
        Guid sessionId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var ownsSession = await dbContext.ChatSessions
            .AsNoTracking()
            .AnyAsync(
                session =>
                    session.ChatSessionId == sessionId &&
                    session.UserId == userId &&
                    session.AccessRole == accessRole,
                cancellationToken);
        if (!ownsSession)
        {
            return null;
        }

        var messages = await dbContext.ChatMessages
            .AsNoTracking()
            .Where(message => message.ChatSessionId == sessionId)
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.ChatMessageId)
            .Take(Math.Clamp(take, 1, 20))
            .Select(message => new
            {
                message.ChatMessageId,
                message.Role,
                message.Content,
                message.SourcesJson,
                message.IsAiGenerated,
                message.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return messages
            .AsEnumerable()
            .Reverse()
            .Select(message => new ChatHistoryMessage(
                message.ChatMessageId,
                message.Role,
                message.Content,
                DeserializeSources(message.SourcesJson),
                message.IsAiGenerated,
                message.CreatedAt))
            .ToArray();
    }

    public async Task<Guid> AppendExchangeAsync(
        string userId,
        string accessRole,
        Guid? sessionId,
        string question,
        ChatResponse response,
        CancellationToken cancellationToken = default)
    {
        ChatSession session;
        if (sessionId.HasValue)
        {
            session = await dbContext.ChatSessions.SingleAsync(
                item =>
                    item.ChatSessionId == sessionId.Value &&
                    item.UserId == userId &&
                    item.AccessRole == accessRole,
                cancellationToken);
        }
        else
        {
            var now = DateTimeOffset.UtcNow;
            session = new ChatSession
            {
                ChatSessionId = Guid.NewGuid(),
                UserId = userId,
                AccessRole = accessRole,
                Title = BuildTitle(question),
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.ChatSessions.Add(session);
        }

        var timestamp = DateTimeOffset.UtcNow;
        session.UpdatedAt = timestamp;
        session.Messages.Add(new ChatMessage
        {
            Role = "user",
            Content = question.Trim(),
            IsAiGenerated = false,
            CreatedAt = timestamp
        });
        session.Messages.Add(new ChatMessage
        {
            Role = "assistant",
            Content = response.Answer,
            SourcesJson = response.Sources.Count == 0
                ? null
                : JsonSerializer.Serialize(response.Sources),
            IsAiGenerated = response.IsAiGenerated,
            CreatedAt = timestamp.AddTicks(1)
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return session.ChatSessionId;
    }

    public async Task<bool> DeleteSessionAsync(
        string userId,
        string accessRole,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await dbContext.ChatSessions.SingleOrDefaultAsync(
            item =>
                item.ChatSessionId == sessionId &&
                item.UserId == userId &&
                item.AccessRole == accessRole,
            cancellationToken);
        if (session is null)
        {
            return false;
        }

        dbContext.ChatSessions.Remove(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string BuildTitle(string question)
    {
        var title = string.Join(' ', question.Split(
            ['\r', '\n', '\t', ' '],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return title.Length <= 80 ? title : title[..77] + "...";
    }

    private static IReadOnlyList<string> DeserializeSources(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
