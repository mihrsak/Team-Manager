using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
using TeamManager.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables();
builder.Services.AddRazorPages();
builder.Services.AddScoped<TeamStore>();

// Database deployment is an explicit command, never part of normal application startup.
if (args.Contains("--apply-schema") || args.Contains("--seed-demo"))
{
    var connectionString = builder.Configuration.GetConnectionString("TeamManager");
    if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("Postavite ConnectionStrings__TeamManager.");
    var file = args.Contains("--seed-demo") ? "002-demo.sql" : "001-schema.sql";
    var path = Path.Combine(builder.Environment.ContentRootPath, "..", "..", "database", file);
    var sql = await File.ReadAllTextAsync(Path.GetFullPath(path));
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(batch)) continue;
        await using var command = new SqlCommand(batch, connection);
        await command.ExecuteNonQueryAsync();
    }
    Console.WriteLine($"Primijenjena skripta: {file}");
    return;
}

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.MapRazorPages();
app.Run();
