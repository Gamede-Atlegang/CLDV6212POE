using ABCRetailers.Models;
using ABCRetailers.Services;
using Microsoft.AspNetCore.Mvc;

namespace ABCRetailers.Controllers
{
    public class OrderController : Controller
    {
        private readonly IAzureStorageService _storage;

        public OrderController(IAzureStorageService storage)
        {
            _storage = storage;
        }

        public async Task<IActionResult> Index()
        {
            var orders = await _storage.GetAllEntitiesAsync<Order>();
            return View(orders.OrderByDescending(o => o.OrderDate));
        }

        public async Task<IActionResult> Create()
        {
            var customers = await _storage.GetAllEntitiesAsync<Customer>();
            var products = await _storage.GetAllEntitiesAsync<Product>();

            ViewBag.Customers = customers.OrderBy(c => c.Surname).ThenBy(c => c.Name);
            ViewBag.Products = products.OrderBy(p => p.ProductName);

            return View(new Order());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Order model)
        {

            // These are computed server-side; remove from model validation
            ModelState.Remove(nameof(Order.Username));
            ModelState.Remove(nameof(Order.ProductName));

            if (!ModelState.IsValid)
            {
                var customers = await _storage.GetAllEntitiesAsync<Customer>();
                var products = await _storage.GetAllEntitiesAsync<Product>();
                ViewBag.Customers = customers.OrderBy(c => c.Surname).ThenBy(c => c.Name);
                ViewBag.Products = products.OrderBy(p => p.ProductName);
                return View(model);
            }

            // Fetch referenced entities
            var customer = await _storage.GetEntityAsync<Customer>("Customer", model.CustomerId);
            var product = await _storage.GetEntityAsync<Product>("Product", model.ProductId);

            if (customer is null || product is null)
            {
                ModelState.AddModelError("", "Invalid customer or product selected.");
                var customers = await _storage.GetAllEntitiesAsync<Customer>();
                var products = await _storage.GetAllEntitiesAsync<Product>();
                ViewBag.Customers = customers.OrderBy(c => c.Surname).ThenBy(c => c.Name);
                ViewBag.Products = products.OrderBy(p => p.ProductName);
                return View(model);
            }

            // Stock check
            if (product.StockAvailable < model.Quantity)
            {
                ModelState.AddModelError("Quantity", $"Only {product.StockAvailable} item(s) available.");
                var customers = await _storage.GetAllEntitiesAsync<Customer>();
                var products = await _storage.GetAllEntitiesAsync<Product>();
                ViewBag.Customers = customers.OrderBy(c => c.Surname).ThenBy(c => c.Name);
                ViewBag.Products = products.OrderBy(p => p.ProductName);
                return View(model);
            }

            // Compose order
            model.PartitionKey = "Order";
            model.OrderDate = DateTime.UtcNow;
            model.Username = customer.Username;
            model.ProductName = product.ProductName;
            model.UnitPrice = product.Price;             
            model.TotalPrice = product.Price * model.Quantity;
            model.Status = "Pending";

            await _storage.AddEntityAsync(model);

            // Decrease stock and save product
            product.StockAvailable -= model.Quantity;
            await _storage.UpdateEntityAsync(product);

            await _storage.SendMessageAsync("order-notifications", $"New order {model.OrderId} for {customer.Username}");
            await _storage.SendMessageAsync("stock-updates", $"Stock {product.ProductName}: {product.StockAvailable}");

            TempData["Message"] = "Order created successfully.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();
            var entity = await _storage.GetEntityAsync<Order>("Order", id);
            if (entity is null) return NotFound();
            return View(entity);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Order model)
        {
            // Remove validation for computed fields
            ModelState.Remove(nameof(Order.Username));
            ModelState.Remove(nameof(Order.ProductName));
            ModelState.Remove(nameof(Order.CustomerId));
            ModelState.Remove(nameof(Order.ProductId));
            ModelState.Remove(nameof(Order.Quantity));
            ModelState.Remove(nameof(Order.UnitPrice));
            ModelState.Remove(nameof(Order.TotalPrice));
            ModelState.Remove(nameof(Order.OrderDate));

            if (!ModelState.IsValid) return View(model);

            model.PartitionKey = "Order";

            // Preserve the original values that shouldn't change
            var existingOrder = await _storage.GetEntityAsync<Order>("Order", model.RowKey);
            if (existingOrder != null)
            {
                model.Username = existingOrder.Username;
                model.ProductName = existingOrder.ProductName;
                model.CustomerId = existingOrder.CustomerId;
                model.ProductId = existingOrder.ProductId;
                model.Quantity = existingOrder.Quantity;
                model.UnitPrice = existingOrder.UnitPrice;
                model.TotalPrice = existingOrder.TotalPrice;
                model.OrderDate = existingOrder.OrderDate;
            }

            await _storage.UpdateEntityAsync(model);
            TempData["Message"] = "Order updated.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();
            var entity = await _storage.GetEntityAsync<Order>("Order", id);
            if (entity is null) return NotFound();
            return View(entity);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return RedirectToAction(nameof(Index));
            await _storage.DeleteEntityAsync<Order>("Order", id);
            TempData["Message"] = "Order deleted.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> GetProductPrice(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId))
                return Json(new { price = 0.0, stock = 0 });

            var product = await _storage.GetEntityAsync<Product>("Product", productId);
            if (product is null)
                return Json(new { price = 0.0, stock = 0 });

            return Json(new { price = product.Price, stock = product.StockAvailable });
        }
    }
}