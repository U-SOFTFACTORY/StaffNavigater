using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StaffNavi.Pages.App
{
    /// <summary>スタッフ用アプリの画面の基底クラス（スタッフのログインを確認する）</summary>
    public abstract class AppPageModel : PageModel
    {
        public const string Scheme = "StaffApp";

        public string EmployeeNo { get; private set; } = "";

        public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            var result = await HttpContext.AuthenticateAsync(Scheme);
            var employeeNo = result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (employeeNo == null)
            {
                context.Result = RedirectToPage("/App/Login");
                return;
            }
            EmployeeNo = employeeNo;
            await next();
        }
    }
}
