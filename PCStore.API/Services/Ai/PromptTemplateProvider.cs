using Microsoft.Extensions.Options;
using PCStore.Core.Ai;

namespace PCStore.API.Services.Ai;

/// <summary>Cung cấp prompt và lấy giới hạn số từ từ cấu hình.</summary>
public sealed class PromptTemplateProvider(IOptionsMonitor<AiOptions> options) : IPromptTemplateProvider
{
    public string PromptVersion => PromptTemplates.PromptVersion;

    public string BuildIntentPrompt(string customerMessage) =>
        PromptTemplates.BuildIntentPrompt(customerMessage);

    public string BuildIntentRepairPrompt(string invalidOutput) =>
        PromptTemplates.BuildIntentRepairPrompt(invalidOutput);

    public string BuildExplanationPrompt(string selectedPartsJson) =>
        PromptTemplates.BuildExplanationPrompt(selectedPartsJson, options.CurrentValue.Tasks.ExplainSelection.MaxWords);

    public string BuildCustomerReplyPrompt(string customerMessage) =>
        PromptTemplates.BuildCustomerReplyPrompt(customerMessage, options.CurrentValue.Tasks.ExplainSelection.MaxWords);

    public string BuildIntentJsonSchema() => PromptTemplates.BuildIntentJsonSchema();
}
