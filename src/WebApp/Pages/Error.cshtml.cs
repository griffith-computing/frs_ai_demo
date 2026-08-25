using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FrsAiDemo.WebApp.Pages;

[AllowAnonymous]
public sealed class ErrorModel : PageModel;