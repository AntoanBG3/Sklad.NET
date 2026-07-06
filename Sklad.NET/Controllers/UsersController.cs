using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Sklad.Models;
using Sklad.Services;
using Sklad.ViewModels;

namespace Sklad.Controllers;

[Authorize(Roles = nameof(UserRole.Admin))]
public class UsersController : Controller
{
    private readonly IUserService _users;
    private readonly IStringLocalizer<SharedResource> _l;

    public UsersController(IUserService users, IStringLocalizer<SharedResource> l)
    {
        _users = users;
        _l = l;
    }

    private int CurrentUserId
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;

    // GET: /Users
    public async Task<IActionResult> Index()
    {
        ViewBag.CurrentUserId = CurrentUserId;
        return View(await _users.GetUsersAsync());
    }

    // GET: /Users/Create
    public IActionResult Create() => View(new CreateUserViewModel());

    // POST: /Users/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        try
        {
            await _users.CreateUserAsync(vm.Username, vm.Password, vm.Role);
        }
        catch (DuplicateUsernameException ex)
        {
            ModelState.AddModelError(nameof(CreateUserViewModel.Username), _l["A user named {0} already exists.", ex.Username]);
            return View(vm);
        }
        TempData["Flash"] = _l["User {0} created.", vm.Username.Trim()].Value;
        return RedirectToAction(nameof(Index));
    }

    // GET: /Users/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var user = await _users.GetUserAsync(id.Value);
        if (user is null) return NotFound();
        return View(new EditUserViewModel { Id = user.Id, Username = user.Username, Role = user.Role });
    }

    // POST: /Users/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EditUserViewModel vm)
    {
        if (id != vm.Id) return NotFound();
        if (!ModelState.IsValid) return View(vm);
        try
        {
            await _users.UpdateUserAsync(id, vm.Role, string.IsNullOrEmpty(vm.NewPassword) ? null : vm.NewPassword);
        }
        catch (UserNotFoundException)
        {
            return NotFound();
        }
        catch (LastAdminException)
        {
            ModelState.AddModelError(string.Empty, _l["The last administrator cannot be removed or demoted."]);
            return View(vm);
        }
        TempData["Flash"] = _l["User {0} saved.", vm.Username].Value;
        return RedirectToAction(nameof(Index));
    }

    // GET: /Users/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var user = await _users.GetUserAsync(id.Value);
        if (user is null) return NotFound();
        ViewBag.IsSelf = user.Id == CurrentUserId;
        return View(user);
    }

    // POST: /Users/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var user = await _users.GetUserAsync(id);
        if (user is null) return RedirectToAction(nameof(Index));
        try
        {
            await _users.DeleteUserAsync(id, CurrentUserId);
        }
        catch (CannotDeleteSelfException)
        {
            ModelState.AddModelError(string.Empty, _l["You cannot delete your own account."]);
            ViewBag.IsSelf = true;
            return View("Delete", user);
        }
        catch (LastAdminException)
        {
            ModelState.AddModelError(string.Empty, _l["The last administrator cannot be removed or demoted."]);
            ViewBag.IsSelf = false;
            return View("Delete", user);
        }
        catch (UserNotFoundException)
        {
            return RedirectToAction(nameof(Index));
        }
        TempData["Flash"] = _l["User {0} deleted.", user.Username].Value;
        return RedirectToAction(nameof(Index));
    }
}
