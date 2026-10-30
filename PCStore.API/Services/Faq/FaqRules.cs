namespace PCStore.API.Services.Faq;

/// <summary>Kiểm tra dữ liệu FAQ trước khi ghi.</summary>
public static class FaqRules
{
    public static void Validate(FaqWriteModel model, FaqOptions options)
    {
        if (string.IsNullOrWhiteSpace(model.Category) || model.Category.Trim().Length > options.MaxCategoryLength)
            throw new ArgumentException($"Nhóm FAQ phải có từ 1 đến {options.MaxCategoryLength} ký tự.");
        if (string.IsNullOrWhiteSpace(model.Question) || model.Question.Trim().Length > options.MaxQuestionLength)
            throw new ArgumentException($"Câu hỏi phải có từ 1 đến {options.MaxQuestionLength} ký tự.");
        if (string.IsNullOrWhiteSpace(model.Answer) || model.Answer.Trim().Length > options.MaxAnswerLength)
            throw new ArgumentException($"Câu trả lời phải có từ 1 đến {options.MaxAnswerLength} ký tự.");
        var words = model.Answer.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > Math.Max(1, options.MaxAnswerWords))
            throw new ArgumentException($"Câu trả lời tối đa {options.MaxAnswerWords} từ.");
        if (string.IsNullOrWhiteSpace(model.Keywords) || model.Keywords.Trim().Length > options.MaxKeywordsLength)
            throw new ArgumentException($"Từ khóa phải có từ 1 đến {options.MaxKeywordsLength} ký tự.");
        if (model.Priority < 0 || model.Priority > options.MaxPriority)
            throw new ArgumentException($"Độ ưu tiên phải từ 0 đến {options.MaxPriority}.");
        if (model.IsActive && FaqMatcher.ContainsPlaceholder(model.Answer, options.PlaceholderMarker))
            throw new ArgumentException("Câu còn nội dung cần điền thì không được bật.");
    }
}
