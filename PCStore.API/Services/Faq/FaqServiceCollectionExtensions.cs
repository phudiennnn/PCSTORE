namespace PCStore.API.Services.Faq;

/// <summary>Đăng ký FAQ và kiểm tra quyền Admin.</summary>
public static class FaqServiceCollectionExtensions
{
    public static IServiceCollection AddPcStoreFaq(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FaqOptions>(configuration.GetSection(FaqOptions.SectionName));
        services.AddSingleton<IFaqService, FaqService>();
        services.AddSingleton<AdminRequestGuard>();
        return services;
    }
}
