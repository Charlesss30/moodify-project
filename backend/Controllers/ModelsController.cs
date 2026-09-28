using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace mood_recommendation.Controllers;
[ApiController, Route("api/[controller]"), Authorize(Roles = "Admin,AIEngineer")]
public class ModelsController : ControllerBase
{
    [HttpGet]
    public IActionResult Status() => Ok(new { items = new[] {
        new { name = "Mood matching", version = "mood-euclidean-v1", status = "Active" },
        new { name = "NLP / DistilBERT", version = "—", status = "No trained model available" },
        new { name = "Hybrid / Collaborative filtering", version = "—", status = "Training data not connected" }
    } });
}
