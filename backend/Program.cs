using Microsoft.EntityFrameworkCore;
using mood_recommendation.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(
            "http://localhost:3000",
            "http://localhost:5173",
            "http://localhost:5500",
            "http://127.0.0.1:5500",
            "http://localhost:8080")
        .AllowAnyHeader()
        .AllowAnyMethod());
});
// PostgreSQL
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseHttpsRedirection();

app.UseCors("Frontend");
app.UseAuthorization();

app.MapControllers();

app.Run();
