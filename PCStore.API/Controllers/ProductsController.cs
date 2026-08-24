using Microsoft.AspNetCore.Mvc;

namespace PCStore.API.Controllers
{
    public class ProductsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
