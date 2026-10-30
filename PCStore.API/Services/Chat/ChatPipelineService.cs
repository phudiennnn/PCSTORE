using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using PCStore.API.Services.Ai;
using PCStore.API.Services.Faq;
using PCStore.Core.Ai;

namespace PCStore.API.Services.Chat;

/// <summary>Xử lý một tin nhắn: FAQ, ý định, dữ liệu cửa hàng, rồi stream lời diễn giải.</summary>
public sealed class ChatPipelineService
{
    private readonly IFaqService _faqService;
    private readonly IAiOrchestrator _orchestrator;
    private readonly IChatCatalog _catalog;
    private readonly IAiRequestLogService _requestLogService;
    private readonly IOptionsMonitor<ChatOptions> _chatOptions;
    private readonly IOptionsMonitor<AiOptions> _aiOptions;

    public ChatPipelineService(
        IFaqService faqService,
        IAiOrchestrator orchestrator,
        IChatCatalog catalog,
        IAiRequestLogService requestLogService,
        IOptionsMonitor<ChatOptions> chatOptions,
        IOptionsMonitor<AiOptions> aiOptions)
    {
        _faqService = faqService;
        _orchestrator = orchestrator;
        _catalog = catalog;
        _requestLogService = requestLogService;
        _chatOptions = chatOptions;
        _aiOptions = aiOptions;
    }

    public async IAsyncEnumerable<ChatStreamEvent> HandleAsync(
        string? rawMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var options = _chatOptions.CurrentValue;
        var started = Stopwatch.StartNew();
        int? firstTokenMilliseconds = null;
        var provider = "template";
        var intent = CustomerIntent.Other;
        var faqHit = false;
        string? failure = null;

        var message = Normalize(rawMessage, options.MaxMessageCharacters);
        if (message.Length == 0)
        {
            yield return ChatStreamEvent.Create("error", new { message = "Bạn chưa nhập nội dung." });
            yield break;
        }

        var faq = await _faqService.FindMatchingFaqAsync(message, cancellationToken);
        if (faq is not null)
        {
            faqHit = true;
            provider = "FAQ";
            intent = CustomerIntent.Faq;
            yield return ChatStreamEvent.Create("meta", new { provider, intent });
            foreach (var piece in Split(faq.Answer, options.FaqChunkCharacters))
            {
                firstTokenMilliseconds ??= (int)started.ElapsedMilliseconds;
                yield return ChatStreamEvent.Create("token", new { text = piece });
            }
            yield return ChatStreamEvent.Create("done", new { });
            await WriteLogAsync(provider, intent, started, firstTokenMilliseconds, true, faqHit, null);
            yield break;
        }

        var unfilled = await _faqService.FindUnfilledNoticeAsync(message, cancellationToken);
        if (!string.IsNullOrWhiteSpace(unfilled))
        {
            faqHit = true;
            provider = "FAQ";
            intent = CustomerIntent.Faq;
            yield return ChatStreamEvent.Create("meta", new { provider, intent });
            foreach (var piece in Split(unfilled, options.FaqChunkCharacters))
            {
                firstTokenMilliseconds ??= (int)started.ElapsedMilliseconds;
                yield return ChatStreamEvent.Create("token", new { text = piece });
            }
            yield return ChatStreamEvent.Create("done", new { });
            await WriteLogAsync(provider, intent, started, firstTokenMilliseconds, true, faqHit, null);
            yield break;
        }

        IntentExtractionResult? extracted = null;
        string? extractError = null;
        try
        {
            var masked = PersonalDataMasker.MaskPersonalData(message, _aiOptions.CurrentValue.PrivacyReplacement);
            extracted = await _orchestrator.ExtractIntentAsync(masked, cancellationToken);
        }
        catch (AllProvidersFailedException)
        {
            extractError = options.ProviderFailedMessage;
        }

        if (extracted is null)
        {
            var nearest = await _faqService.FindNearestFaqAsync(message, cancellationToken);
            failure = extractError ?? options.ProviderFailedMessage;
            yield return ChatStreamEvent.Create("error", new
            {
                message = failure,
                faq = nearest is null ? null : new { nearest.Question, nearest.Answer },
            });
            await WriteLogAsync(provider, intent, started, null, false, false, failure);
            yield break;
        }

        intent = extracted.Intent;
        provider = string.IsNullOrWhiteSpace(extracted.ProviderName) ? provider : extracted.ProviderName;
        if (intent is CustomerIntent.OrderSupport or CustomerIntent.Other or CustomerIntent.Faq)
        {
            var text = intent == CustomerIntent.OrderSupport ? options.OrderSupportMessage : options.OtherMessage;
            provider = "template";
            yield return ChatStreamEvent.Create("meta", new { provider, intent });
            foreach (var piece in Split(text, options.FaqChunkCharacters))
            {
                firstTokenMilliseconds ??= (int)started.ElapsedMilliseconds;
                yield return ChatStreamEvent.Create("token", new { text = piece });
            }
            yield return ChatStreamEvent.Create("done", new { });
            await WriteLogAsync(provider, intent, started, firstTokenMilliseconds, true, false, null);
            yield break;
        }

        var grounding = await _catalog.GroundAsync(extracted, message, cancellationToken);
        if (!grounding.CallModel)
        {
            provider = "template";
            yield return ChatStreamEvent.Create("meta", new { provider, intent });
            foreach (var piece in Split(grounding.FixedMessage ?? options.MissingPartsMessage, options.FaqChunkCharacters))
            {
                firstTokenMilliseconds ??= (int)started.ElapsedMilliseconds;
                yield return ChatStreamEvent.Create("token", new { text = piece });
            }
            yield return ChatStreamEvent.Create("done", new { });
            await WriteLogAsync(provider, intent, started, firstTokenMilliseconds, true, false, null);
            yield break;
        }

        yield return ChatStreamEvent.Create("meta", new { provider, intent });
        if (grounding.Products.Count > 0)
            yield return ChatStreamEvent.Create("products", new { items = grounding.Products });

        var announcedProvider = false;
        var interrupted = false;
        string? streamFailure = null;
        await using (var enumerator = _orchestrator.StreamExplanationAsync(grounding.PartsJson, cancellationToken).GetAsyncEnumerator(cancellationToken))
        {
            while (true)
            {
                var moved = false;
                var token = default(AiStreamToken);
                try
                {
                    moved = await enumerator.MoveNextAsync();
                    if (moved)
                        token = enumerator.Current;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    interrupted = true;
                    break;
                }
                catch (AllProvidersFailedException)
                {
                    streamFailure = options.ProviderFailedMessage;
                    break;
                }
                catch (AiStreamInterruptedException interruptedException)
                {
                    streamFailure = interruptedException.Message;
                    break;
                }

                if (!moved)
                    break;
                if (!announcedProvider && !string.IsNullOrWhiteSpace(token.ProviderName) && token.ProviderName != provider)
                {
                    provider = token.ProviderName;
                    yield return ChatStreamEvent.Create("meta", new { provider, intent });
                }
                announcedProvider = true;
                firstTokenMilliseconds ??= (int)started.ElapsedMilliseconds;
                yield return ChatStreamEvent.Create("token", new { text = token.Text });
            }
        }

        if (interrupted || cancellationToken.IsCancellationRequested)
        {
            await WriteLogAsync(provider, intent, started, firstTokenMilliseconds, false, false, "Khách ngắt kết nối.");
            yield break;
        }

        if (streamFailure is not null || !firstTokenMilliseconds.HasValue)
        {
            var nearest = await _faqService.FindNearestFaqAsync(message, cancellationToken);
            failure = streamFailure ?? options.ProviderFailedMessage;
            yield return ChatStreamEvent.Create("error", new
            {
                message = failure,
                faq = nearest is null ? null : new { nearest.Question, nearest.Answer },
            });
            await WriteLogAsync(provider, intent, started, firstTokenMilliseconds, false, false, failure);
            yield break;
        }
        yield return ChatStreamEvent.Create("done", new { });
        await WriteLogAsync(provider, intent, started, firstTokenMilliseconds, true, false, null);
    }

    private async Task WriteLogAsync(
        string provider,
        string intent,
        Stopwatch started,
        int? firstTokenMilliseconds,
        bool success,
        bool faqHit,
        string? failure)
    {
        await _requestLogService.WriteAsync(new AiRequestLogEntry
        {
            Provider = provider,
            Intent = intent,
            LatencyMilliseconds = (int)Math.Min(int.MaxValue, started.ElapsedMilliseconds),
            IsSuccess = success,
            FailoverReason = failure,
            TimeToFirstTokenMilliseconds = firstTokenMilliseconds,
            FaqHit = faqHit,
        }, CancellationToken.None);
    }

    public static string Normalize(string? rawMessage, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
            return string.Empty;
        var collapsed = string.Join(' ', rawMessage.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var limit = Math.Max(1, maxCharacters);
        return collapsed.Length <= limit ? collapsed : collapsed[..limit];
    }

    public static IEnumerable<string> Split(string? text, int chunkCharacters)
    {
        var value = text ?? string.Empty;
        if (value.Length == 0)
            yield break;
        var size = Math.Max(1, chunkCharacters);
        for (var index = 0; index < value.Length; index += size)
            yield return value.Substring(index, Math.Min(size, value.Length - index));
    }
}
