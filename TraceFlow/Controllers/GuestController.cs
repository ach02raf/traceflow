using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TraceFlow.Services;

namespace TraceFlow.Controllers;

public class GuestController : Controller
{
    private readonly GuestService _guests;
    private readonly SignInManager<IdentityUser> _signIn;
    public GuestController(GuestService g, SignInManager<IdentityUser> s) { _guests = g; _signIn = s; }

    [HttpPost("/guest"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Start()
    {
        if (User.Identity?.IsAuthenticated == true) return Redirect("/");

        var user = await _guests.CreateAsync();
        if (user is null)
        {
            TempData["Error"] = "Too many guests right now, please try again in a minute.";
            return Redirect("/");
        }
        // isPersistent:false => session cookie, gone when the browser is closed
        await _signIn.SignInAsync(user, isPersistent: false);
        return Redirect("/");
    }

    [HttpPost("/guest/leave"), Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Leave()
    {
        var name = User.Identity!.Name!;
        await _signIn.SignOutAsync();
        await _guests.DeleteAsync(name);   // no-op for non-guests
        return Redirect("/");
    }
}
