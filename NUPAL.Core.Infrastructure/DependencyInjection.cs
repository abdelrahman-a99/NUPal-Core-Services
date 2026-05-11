using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using NUPAL.Core.Application.Interfaces;
using Nupal.Core.Infrastructure.Repositories;
using Nupal.Core.Infrastructure.Services;
using NUPAL.Core.Infrastructure.Services;

namespace NUPAL.Core.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
        {
            var mongoUrl = configuration.GetValue<string>("MONGO_URL")
                           ?? Environment.GetEnvironmentVariable("MONGO_URL")
                           ?? throw new InvalidOperationException("MongoDB connection string is not configured. Please provide 'MONGO_URL' in appsettings or environment variables.");

            services.AddSingleton<IMongoClient>(_ =>
            {
                var settings = MongoClientSettings.FromConnectionString(mongoUrl);
                settings.ConnectTimeout = TimeSpan.FromSeconds(10);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);
                return new MongoClient(settings);
            });
            services.AddSingleton<IMongoDatabase>(sp =>
            {
                var client = sp.GetRequiredService<IMongoClient>();
                return client.GetDatabase("nupal");
            });

            services.AddScoped<IStudentRepository, StudentRepository>();
            services.AddScoped<IContactRepository, ContactRepository>();

            services.AddScoped<IChatConversationRepository, ChatConversationRepository>();
            services.AddScoped<IChatMessageRepository, ChatMessageRepository>();
            services.AddHttpClient<IAgentClient, AgentClient>();

            services.AddScoped<IRlJobRepository, RlJobRepository>();
            services.AddScoped<IRlRecommendationRepository, RlRecommendationRepository>();
            services.AddHttpClient<IRlService, RlService>();
            services.AddScoped<IPrecomputeService, PrecomputeService>();

            services.AddHttpClient<IJobService, WuzzufJobService>();
            
            services.AddHttpClient<IAiService, AiService>(client => 
            {
                client.Timeout = TimeSpan.FromMinutes(3);
                client.DefaultRequestHeaders.Add("User-Agent", "NUPAL-Proxy/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            });
            
            services.AddScoped<IDynamicSkillsService, DynamicSkillsService>();
            
            services.AddScoped<IResumeRepository, ResumeRepository>();
            
            services.AddScoped<IJobFitRepository, JobFitRepository>();
            services.AddScoped<ICourseMappingRepository, CourseMappingRepository>();
            services.AddScoped<ICourseNormalizationService, CourseNormalizationService>();
            services.AddScoped<ISystemSettingsRepository, SystemSettingsRepository>();

            services.AddHostedService<PrecomputeBackgroundWorker>();

            services.AddSingleton<IBlockRepository, BlockRepository>();
            services.AddScoped<IRegistrationRepository, RegistrationRepository>();
            services.AddSingleton<ISchedulingService, SchedulingService>();

            services.AddScoped<IAdminService, AdminService>();

            return services;
        }
    }
}
