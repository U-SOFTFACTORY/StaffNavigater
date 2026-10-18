using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.WebEncoders;
using Microsoft.Extensions.Options;
using StaffNavi.Services;

// CSV（Shift_JIS）の読み書き用
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection("Demo"));
builder.Services.AddSingleton<DataStore>();
builder.Services.AddSingleton<LaborRepository>();
builder.Services.AddSingleton<LaborService>();
builder.Services.AddSingleton<CsvImportService>();
builder.Services.AddSingleton<DemoSeeder>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.Cookie.Name = "StaffNavi.Demo";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
// 日本語をHTML文字参照に変換せずそのまま出力する
builder.Services.Configure<WebEncoderOptions>(options => options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Error");
});

var app = builder.Build();

app.Services.GetRequiredService<DemoSeeder>().EnsureSeeded();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// 公開期限を過ぎたデモ環境は、すべての画面を公開終了表示にする
app.Use(async (context, next) =>
{
    var expiresAt = context.RequestServices.GetRequiredService<IOptionsMonitor<DemoOptions>>().CurrentValue.ExpiresAt;
    var path = context.Request.Path;
    var isAsset = path.StartsWithSegments("/css") || path.StartsWithSegments("/lib") || path.StartsWithSegments("/favicon.ico");
    if (expiresAt != null && Clock.Now > expiresAt && !isAsset)
    {
        context.Response.StatusCode = StatusCodes.Status410Gone;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(
            "<!DOCTYPE html><html lang=\"ja\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
            "<title>公開終了</title><link rel=\"stylesheet\" href=\"/css/site.css\"></head><body class=\"auth-body\">" +
            "<div class=\"auth-card text-center\"><h1 class=\"h5 mb-3\">デモ環境の公開は終了しました</h1>" +
            "<p class=\"text-muted mb-0\">本デモ環境はお打ち合わせでのご確認用として一時的に公開していたものです。<br>ご確認いただき、ありがとうございました。</p>" +
            "</div></body></html>");
        return;
    }
    await next();
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
