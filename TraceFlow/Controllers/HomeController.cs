using Microsoft.AspNetCore.Mvc;

namespace TraceFlow.Controllers;

public class HomeController : Controller
{
    // Anonymous visitors see the landing page, signed-in users see the live board
    public IActionResult Index() =>
        User.Identity?.IsAuthenticated == true ? View() : View("Landing");
}
