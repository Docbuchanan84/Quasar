var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.Use(async (context, next) =>
{
    if (context.Request.Path == "/" || context.Request.Path == "/index.html")
    {
        context.Response.Headers.CacheControl = "no-store";
    }

    await next(context);
});
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(new
    {
        application = "Quasar",
        status = "ready",
        stage = "skeleton",
        serverTimeUtc = DateTimeOffset.UtcNow
    });
});

app.Run();
