using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using MemoryGame.Models;
using System.Text.Json;

namespace MemoryGame.Controllers
{
    public class MemoryController : Controller
    {
        private readonly IWebHostEnvironment _environment;
        private readonly string _cardSetsFilePath;

        public MemoryController(IWebHostEnvironment environment)
        {
            _environment = environment;
            _cardSetsFilePath = Path.Combine(_environment.WebRootPath, "cardsets.json");
            if (!System.IO.File.Exists(_cardSetsFilePath))
            {
                System.IO.File.WriteAllText(_cardSetsFilePath, "[]");
            }
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> UploadImages(List<IFormFile> images, string setName)
        {
            if (string.IsNullOrEmpty(setName))
            {
                return Json(new { success = false, message = "Nazwa zestawu nie została podana." });
            }

            if (images == null || images.Count < 2)
            {
                return Json(new { success = false, message = "Dodaj przynajmniej 2 obrazki." });
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

            var cardSets = LoadCardSets();
            var newSet = new CardSet
            {
                Id = Guid.NewGuid().ToString(),
                Name = setName,
                ImagePaths = imagePaths
            };
            cardSets.Add(newSet);
            SaveCardSets(cardSets);

            return Json(new { success = true, imagePaths, setId = newSet.Id });
        }

        [HttpGet]
        public IActionResult GetCardSets()
        {
            var cardSets = LoadCardSets();
            return Json(cardSets);
        }

        private List<CardSet> LoadCardSets()
        {
            var json = System.IO.File.ReadAllText(_cardSetsFilePath);
            return JsonSerializer.Deserialize<List<CardSet>>(json) ?? new List<CardSet>();
        }

        private void SaveCardSets(List<CardSet> cardSets)
        {
            var json = JsonSerializer.Serialize(cardSets, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(_cardSetsFilePath, json);
        }
    }
}