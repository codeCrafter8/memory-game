/* 
 
nie rób mergów na maina

input -> button

graj jako gospodarz lub jako (widok)

przycks - wybraæ ka¿dego

trojkat?? przy obecnym ruchu, tlo na aktywnego gracza


lobby - poczekajlnia
 
 */

using MemoryGame.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Memory}/{action=Index}/{id?}");
app.MapHub<MemoryHub>("/memoryHub");

app.Run();