using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

namespace MemoryGame.Controllers
{
    public class MemoryController : Controller
    {
        private readonly IWebHostEnvironment _environment;

        public MemoryController(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> UploadImages(List<IFormFile> images)
        {
            if (images == null || images.Count < 2)
            {
                return Json(new { success = false, message = "Dodaj min. 2 obrazki" });
            }

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var imagePaths = new List<string>();
            foreach (var image in images)
            {
                if (image.Length > 0)
                {
                    var fileName = Path.GetRandomFileName() + Path.GetExtension(image.FileName);
                    var filePath = Path.Combine(uploadsFolder, fileName);
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await image.CopyToAsync(stream);
                    }
                    imagePaths.Add($"/uploads/{fileName}");
                }
            }

            return Json(new { success = true, imagePaths });
        }
    }
}