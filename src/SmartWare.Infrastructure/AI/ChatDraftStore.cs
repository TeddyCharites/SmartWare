using Microsoft.Extensions.Caching.Memory;
using SmartWare.Application.AI.Chatbot;

namespace SmartWare.Infrastructure.AI;

/// <summary>
/// In-memory draft store. Drafts expire after 30 minutes and are lost on restart, which is
/// acceptable: nothing is written to the database until the user confirms.
/// </summary>
internal sealed class ChatDraftStore(IMemoryCache cache) : IChatDraftStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private readonly Lock _gate = new();

    public void Save(string userId, ChatReceiptDraft draft) =>
        cache.Set(Key(draft.Id), new Entry(userId, draft), Lifetime);

    public ChatReceiptDraft? Get(string userId, Guid draftId) =>
        cache.TryGetValue(Key(draftId), out Entry? entry) && entry!.UserId == userId
            ? entry.Draft
            : null;

    public ChatReceiptDraft? Take(string userId, Guid draftId)
    {
        lock (_gate)
        {
            var draft = Get(userId, draftId);
            if (draft is not null)
            {
                cache.Remove(Key(draftId));
            }

            return draft;
        }
    }

    private static string Key(Guid draftId) => $"chat-draft:{draftId:N}";

    private sealed record Entry(string UserId, ChatReceiptDraft Draft);
}
