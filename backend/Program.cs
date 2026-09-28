using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using mood_recommendation.Services;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
var signingKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(signingKey))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Set Jwt__Key to a secret of at least 32 bytes.");
    signingKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
}
if (Encoding.UTF8.GetByteCount(signingKey) < 32)
    throw new InvalidOperationException("Jwt__Key must contain at least 32 bytes.");
builder.Services.AddSingleton(new TokenService(signingKey));
builder.Services.AddSingleton<AdminLoginHandoff>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = TokenService.Issuer,
        ValidateAudience = true, ValidAudience = TokenService.Audience,
        ValidateLifetime = true, ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ClockSkew = TimeSpan.FromSeconds(15)
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            if (!int.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            { context.Fail("Invalid account."); return; }
            var db = context.HttpContext.RequestServices.GetRequiredService<MongoStore>();
            var user = await db.TaiKhoans.FirstOrDefaultAsync(x => x.TaiKhoanID == userId);
            var role = user == null ? "" : AdminRoles.IsAdmin(user.VaiTro) ? AdminRoles.Name : user.VaiTro;
            if (user == null || !user.TrangThai || context.Principal!.FindFirstValue(ClaimTypes.Role) != role)
                context.Fail("Account disabled or permissions changed.");
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("Omdb", client => client.Timeout = TimeSpan.FromSeconds(15));
// OMDb authenticates in the query string: suppress HTTP URL logging to keep the key private.
builder.Logging.AddFilter("System.Net.Http.HttpClient.Omdb", LogLevel.None);
builder.Services.AddHttpClient("Spotify", client => client.Timeout = TimeSpan.FromSeconds(20))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
builder.Logging.AddFilter("System.Net.Http.HttpClient.Spotify", LogLevel.Warning);
builder.Services.AddSingleton<ISpotifyClient>(sp => new SpotifyClient(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("Spotify"), sp.GetRequiredService<IConfiguration>()));
builder.Services.AddScoped<SpotifyImportService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(
            "http://localhost:3000",
            "http://localhost:5173",
            "http://localhost:5174",
            "http://localhost:5500",
            "http://127.0.0.1:5500",
            "http://localhost:8080")
        .AllowAnyHeader()
        .AllowAnyMethod());
});
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(builder.Configuration["Mongo:ConnectionString"] ?? "mongodb://127.0.0.1:27017"));
builder.Services.AddSingleton<MongoStore>();
builder.Services.AddSingleton<ExternalCatalog>();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
await app.Services.GetRequiredService<MongoStore>().Initialize(Path.Combine(app.Environment.ContentRootPath,"Data/mongo-schema.json"));
app.Use(async (context,next) => {
    var store=context.RequestServices.GetRequiredService<MongoStore>();
    var write=context.Request.Path.StartsWithSegments("/api") && context.Request.Method is "POST" or "PUT" or "DELETE" or "PATCH";
    if(write)await store.Gate.WaitAsync(context.RequestAborted);
    try { await next(); }
    catch(MongoWriteException e) {
        context.Response.StatusCode=e.WriteError.Category==ServerErrorCategory.DuplicateKey?409:400;
        await context.Response.WriteAsJsonAsync(new {message=e.WriteError.Category==ServerErrorCategory.DuplicateKey?"Data already exists. Check the name, email or source ID.":"Data does not match the MongoDB schema."});
    }
    catch(System.ComponentModel.DataAnnotations.ValidationException e) {context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new {message=e.Message});}
    catch(MongoException) {context.Response.StatusCode=503;await context.Response.WriteAsJsonAsync(new {message="MongoDB is unavailable. Please try again."});}
    finally {if(write)store.Gate.Release();}
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseHttpsRedirection();

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
