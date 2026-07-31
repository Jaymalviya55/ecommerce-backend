using ECommerce.Infrastructure.Data;
using ECommerce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Npgsql;
using ECommerce.Api.Hubs;
using ECommerce.Api.Extensions;
using ECommerce.Domain.Entities.UserManagement;
using Casbin;
using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddScoped<ECommerce.Api.Services.ITokenService, ECommerce.Api.Services.TokenService>();
builder.Services.AddHttpClient<ECommerce.Api.Services.IAiSupportService, ECommerce.Api.Services.AiSupportService>()
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))))
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));
builder.Services.AddSingleton<ECommerce.Api.Services.AiTaskQueue>();
builder.Services.AddHostedService<ECommerce.Api.Services.AiBackgroundService>();

builder.Services.Configure<ECommerce.Api.Models.EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddTransient<ECommerce.Api.Services.IEmailService, ECommerce.Api.Services.SmtpEmailService>();
}
else
{
    builder.Services.AddHttpClient<ECommerce.Api.Services.IEmailService, ECommerce.Api.Services.ResendEmailService>();
}

// Setup CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.SetIsOriginAllowed(origin => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Setup DbContext
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Convert Render's postgres:// URL to ADO.NET format if necessary
if (!string.IsNullOrEmpty(connectionString))
{
    if (connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || 
        connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        var databaseUri = new Uri(connectionString);
        var userInfo = databaseUri.UserInfo.Split(':');
        
        var npgsqlBuilder = new NpgsqlConnectionStringBuilder
        {
            Host = databaseUri.Host,
            Port = databaseUri.IsDefaultPort ? 5432 : databaseUri.Port,
            Username = userInfo[0],
            Password = userInfo.Length > 1 ? userInfo[1] : "",
            Database = databaseUri.LocalPath.TrimStart('/'),
            SslMode = SslMode.Require,
            TrustServerCertificate = true
        };
        connectionString = npgsqlBuilder.ToString();
    }
}

builder.Services.AddDbContext<ECommerceDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

// Setup Casbin Authorization
builder.Services.AddCasbinEfCoreAdapter(connectionString!);

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString);

// Setup Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options => 
{            
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = true;
})
.AddEntityFrameworkStores<ECommerceDbContext>()
.AddDefaultTokenProviders();

// Setup JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["Secret"];

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey!)),
        ClockSkew = TimeSpan.Zero
    };
    
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            // Read from secure HttpOnly cookie
            if (context.Request.Cookies.ContainsKey("AccessToken"))
            {
                context.Token = context.Request.Cookies["AccessToken"];
            }

            // Fallback for SignalR which uses query string
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

var app = builder.Build();

app.UseMiddleware<ECommerce.Api.Middleware.GlobalExceptionMiddleware>();

// Automatically apply pending database migrations and seed roles on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ECommerceDbContext>();
    dbContext.Database.Migrate();

    var casbinDbContext = scope.ServiceProvider.GetRequiredService<Casbin.Persist.Adapter.EFCore.CasbinDbContext<int>>();
    casbinDbContext.Database.EnsureCreated();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    var roles = new[] { "Admin", "Customer", "SupportAgent", "FulfillmentStaff" };
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    // Seed User Management Domain Tables
    if (!await dbContext.UserTypes.AnyAsync())
    {
        var adminType = new UserType { UserTypeId = 1, Name = "Admin", Code = "ADM" };
        var merchantType = new UserType { UserTypeId = 2, Name = "Merchant", Code = "MER" };
        var supportType = new UserType { UserTypeId = 3, Name = "Support", Code = "SUP" };
        var customerType = new UserType { UserTypeId = 4, Name = "Customer", Code = "CUST" };

        dbContext.UserTypes.AddRange(adminType, merchantType, supportType, customerType);
        await dbContext.SaveChangesAsync();
    }

    if (!await dbContext.UserLevels.AnyAsync())
    {
        var superAdminLevel = new UserLevel { UserLevelId = 1, UserTypeId = 1, Name = "SuperAdmin", Code = "SADM" };
        var storeOwnerLevel = new UserLevel { UserLevelId = 2, UserTypeId = 2, Name = "StoreOwner", Code = "SOWN" };
        var supportAgentLevel = new UserLevel { UserLevelId = 3, UserTypeId = 3, Name = "SupportAgent", Code = "SAGT" };
        var customerLevel = new UserLevel { UserLevelId = 4, UserTypeId = 4, Name = "StandardCustomer", Code = "CUST" };

        dbContext.UserLevels.AddRange(superAdminLevel, storeOwnerLevel, supportAgentLevel, customerLevel);
        await dbContext.SaveChangesAsync();
    }

    if (!await dbContext.AppUserRoles.AnyAsync())
    {
        var adminRole = new UserRole { UserRoleId = 1, UserLevelId = 1, Name = "System Administrator", Sequence = 1 };
        var storeRole = new UserRole { UserRoleId = 2, UserLevelId = 2, Name = "Store Manager", Sequence = 2 };
        var supportRole = new UserRole { UserRoleId = 3, UserLevelId = 3, Name = "Support Representative", Sequence = 3 };
        var customerRole = new UserRole { UserRoleId = 4, UserLevelId = 4, Name = "Customer User", Sequence = 4 };

        dbContext.AppUserRoles.AddRange(adminRole, storeRole, supportRole, customerRole);
        await dbContext.SaveChangesAsync();
    }

    // Seed AppUserLogins for all existing Identity users
    var allUsers = await userManager.Users.ToListAsync();
    foreach (var u in allUsers)
    {
        string userName = u.UserName ?? u.Email ?? "";
        if (string.IsNullOrWhiteSpace(userName)) continue;

        // Skip if UserName already exists in DB or in local tracker
        if (await dbContext.AppUserLogins.AnyAsync(l => l.UserName == userName) ||
            dbContext.AppUserLogins.Local.Any(l => l.UserName == userName))
        {
            continue;
        }

        var userRolesList = await userManager.GetRolesAsync(u);
        int userTypeId = 4; // Default Customer
        int userLevelId = 4;

        if (userRolesList.Contains("Admin"))
        {
            userTypeId = 1;
            userLevelId = 1;
        }
        else if (userRolesList.Contains("SupportAgent"))
        {
            userTypeId = 3;
            userLevelId = 3;
        }
        else if (userRolesList.Contains("FulfillmentStaff"))
        {
            userTypeId = 2;
            userLevelId = 2;
        }

        var userLogin = new UserLogin
        {
            UserName = userName,
            PasswordHash = u.PasswordHash ?? "",
            UserTypeId = userTypeId,
            UserLevelId = userLevelId,
            UserReferenceId = u.Id,
            TenantId = "global",
            IsActive = true,
            IsDefaultPasswordChange = false,
            LoginAttemptsCount = 0,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.AppUserLogins.Add(userLogin);
    }
    if (dbContext.ChangeTracker.HasChanges())
    {
        await dbContext.SaveChangesAsync();
    }

    // Seed Casbin Rules (Multi-Tenant Domain Matching)
    var enforcer = scope.ServiceProvider.GetRequiredService<IEnforcer>();
    string globalTenant = "global";

    // 'g' rules (Level to Role per Tenant)
    if (!enforcer.HasGroupingPolicy("1", "System Administrator", globalTenant))
        await enforcer.AddGroupingPolicyAsync("1", "System Administrator", globalTenant);

    if (!enforcer.HasGroupingPolicy("2", "Store Manager", globalTenant))
        await enforcer.AddGroupingPolicyAsync("2", "Store Manager", globalTenant);

    if (!enforcer.HasGroupingPolicy("3", "Support Representative", globalTenant))
        await enforcer.AddGroupingPolicyAsync("3", "Support Representative", globalTenant);

    // 'p' rules (Role to Feature per Tenant)
    if (!enforcer.HasPolicy("System Administrator", globalTenant, "*", "*"))
    {
        await enforcer.AddPolicyAsync("System Administrator", globalTenant, "*", "*");
        await enforcer.AddPolicyAsync("System Administrator", globalTenant, "@admin/all", "read");
        await enforcer.AddPolicyAsync("System Administrator", globalTenant, "@admin/all", "write");
        await enforcer.AddPolicyAsync("System Administrator", globalTenant, "@admin/coupons", "read");
        await enforcer.AddPolicyAsync("System Administrator", globalTenant, "@admin/coupons", "write");
        await enforcer.AddPolicyAsync("System Administrator", globalTenant, "@admin/orders", "read");
        await enforcer.AddPolicyAsync("System Administrator", globalTenant, "@admin/orders", "write");
        await enforcer.AddPolicyAsync("System Administrator", globalTenant, "@admin/analytics", "read");
        await enforcer.AddPolicyAsync("System Administrator", globalTenant, "@admin/analytics", "write");
    }

    if (!enforcer.HasPolicy("Store Manager", globalTenant, "@admin/products", "read"))
    {
        await enforcer.AddPolicyAsync("Store Manager", globalTenant, "@admin/products", "read");
        await enforcer.AddPolicyAsync("Store Manager", globalTenant, "@admin/products", "write");
        await enforcer.AddPolicyAsync("Store Manager", globalTenant, "@admin/orders", "read");
        await enforcer.AddPolicyAsync("Store Manager", globalTenant, "@admin/orders", "write");
        await enforcer.AddPolicyAsync("Store Manager", globalTenant, "@admin/coupons", "read");
        await enforcer.AddPolicyAsync("Store Manager", globalTenant, "@admin/coupons", "write");
    }

    if (!enforcer.HasPolicy("Support Representative", globalTenant, "@support/tickets", "read"))
    {
        await enforcer.AddPolicyAsync("Support Representative", globalTenant, "@support/tickets", "read");
        await enforcer.AddPolicyAsync("Support Representative", globalTenant, "@support/tickets", "write");
        await enforcer.AddPolicyAsync("Support Representative", globalTenant, "@admin/orders", "read");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowReactApp");
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ECommerce.Api.Middleware.FeatureAuthorizationMiddleware>();
app.MapControllers();
app.MapHub<SupportChatHub>("/hubs/chat");
app.MapHealthChecks("/api/health");

app.Run();
