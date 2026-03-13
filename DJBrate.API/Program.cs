using DJBrate.API;
using Microsoft.EntityFrameworkCore;
 
var builder = WebApplication.CreateBuilder(args);
 
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
 
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
 
var app = builder.Build();
 
// Auto-migrate on startup (EF Core zamjena za Flyway)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}
 
app.MapOpenApi();
 
app.MapGet("/", () => "DJ Brate API is running!");
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();