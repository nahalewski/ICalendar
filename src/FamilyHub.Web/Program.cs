using FamilyHub.Web;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["Web:Urls"] ?? "http://0.0.0.0:5643");
builder.Services.AddFamilyHubWeb();

var app = builder.Build();
app.MapFamilyHub();
app.Run();
