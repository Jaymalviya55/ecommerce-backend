using Casbin;
using Casbin.Model;
using Casbin.Persist;
using Casbin.Persist.Adapter.EFCore;
using Casbin.Persist.Adapter.EFCore.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Api.Extensions;

public static class CasbinExtensions
{
    public static IServiceCollection AddCasbinEfCoreAdapter(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDbContext<CasbinDbContext<int>>(options =>
        {
            options.UseNpgsql(connectionString);
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        services.AddEFCoreAdapter<int>();

        services.AddScoped<IEnforcer>(sp =>
        {
            var casbinDbContext = sp.GetRequiredService<CasbinDbContext<int>>();
            
            string createTableSql = @"
                CREATE TABLE IF NOT EXISTS casbin_rule (
                    id SERIAL PRIMARY KEY,
                    ptype TEXT NULL,
                    v0 TEXT NULL,
                    v1 TEXT NULL,
                    v2 TEXT NULL,
                    v3 TEXT NULL,
                    v4 TEXT NULL,
                    v5 TEXT NULL,
                    v6 TEXT NULL,
                    v7 TEXT NULL,
                    v8 TEXT NULL,
                    v9 TEXT NULL,
                    v10 TEXT NULL,
                    v11 TEXT NULL,
                    v12 TEXT NULL,
                    v13 TEXT NULL,
                    v14 TEXT NULL,
                    v15 TEXT NULL
                );
            ";
            casbinDbContext.Database.ExecuteSqlRaw(createTableSql);

            IHostEnvironment environment = sp.GetRequiredService<IHostEnvironment>();
            IAdapter adapter = sp.GetRequiredService<IAdapter>();
            string modelPath = Path.Combine(environment.ContentRootPath, "model.conf");
            IModel? model = DefaultModel.CreateFromFile(modelPath);
            Enforcer enforcer = new(model, adapter);
            enforcer.LoadPolicy();
            return enforcer;
        });

        return services;
    }
}
