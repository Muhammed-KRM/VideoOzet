using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Services;

namespace VideoOzet.Business;

public static class DependencyInjection
{
    public static IServiceCollection AddBusinessServices(this IServiceCollection services)
    {
        // Manager'lar (Servisler)
        services.AddScoped<IAuthService, AuthManager>();
        services.AddScoped<IListingService, ListingManager>();
        services.AddScoped<ITokenService, TokenManager>();
        services.AddScoped<IMessageService, MessageManager>();
        services.AddScoped<IReviewService, ReviewManager>();
        services.AddScoped<IVitrinService, VitrinManager>();
        services.AddScoped<IUserService, UserManager>();
        services.AddScoped<IAdminService, AdminManager>();
        services.AddScoped<ISettingService, SettingManager>();
        services.AddScoped<ILogService, LogManager>();
        services.AddMemoryCache();
        services.AddScoped<IEmailService, VideoOzet.Business.Infrastructure.Email.SmtpEmailService>();
        services.AddScoped<IModerationService, ModerationManager>();

        // Bildirim Sistemi
        services.AddScoped<INotificationService, NotificationManager>();
        services.AddScoped<ISmsService, VideoOzet.Business.Infrastructure.Sms.NetgsmSmsService>();
        services.AddScoped<VideoOzet.Business.Infrastructure.Messaging.IFcmService, VideoOzet.Business.Infrastructure.Messaging.FcmService>();
        services.AddHttpClient("Netgsm");
        services.AddHttpClient<VideoOzet.Business.Infrastructure.Messaging.FcmService>();

        // FluentValidation — Bu assembly'deki tüm Validator'ları otomatik tarayıp kaydet
        services.AddValidatorsFromAssemblyContaining<AuthManager>();

        // Adım 3.2: Elasticsearch
        VideoOzet.Business.Infrastructure.Search.ElasticsearchExtensions.AddElasticsearch(services);
        services.AddScoped<ISearchService, VideoOzet.Business.Infrastructure.Search.ElasticsearchService>();

        // Adım 3.3: Redis
        services.AddSingleton<ICacheService, VideoOzet.Business.Infrastructure.Cache.RedisCacheService>();

        // Ödeme Sistemi — Strategy + Factory Pattern (PayTR yurt içi, Stripe yurt dışı)
        services.AddScoped<IPaymentService, VideoOzet.Business.Infrastructure.Payment.PayTRPaymentService>();
        services.AddScoped<IPaymentService, VideoOzet.Business.Infrastructure.Payment.StripePaymentService>();
        services.AddScoped<IPaymentServiceFactory, VideoOzet.Business.Infrastructure.Payment.PaymentServiceFactory>();

        // Dosya Yükleme (Local → ileride Azure Blob'a geçilebilir)
        services.AddScoped<IFileStorageService, VideoOzet.Business.Infrastructure.Storage.LocalFileStorageService>();

        // RabbitMQ/MassTransit — RabbitMQ disabled olduğunda DummyPublishEndpoint kullanılır
        // RabbitMQ enabled olduğunda Program.cs'deki AddMassTransit bu kaydın üzerine yazar
        services.AddScoped<MassTransit.IPublishEndpoint, VideoOzet.Business.Infrastructure.Messaging.DummyPublishEndpoint>();
        
        return services;
    }
}
