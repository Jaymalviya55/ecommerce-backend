using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using ECommerce.Api.Models;

namespace ECommerce.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FeaturesController : ControllerBase
{
    [HttpGet]
    public IActionResult GetAll()
    {
        IEnumerable<FieldInfo> featureFields = typeof(Features)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(Features) && f.IsInitOnly);

        var featureDtos = featureFields
            .Select(field =>
            {
                Features featureInstance = (Features)field.GetValue(null)!;
                return new 
                { 
                    Name = field.Name, 
                    Key = featureInstance.Value 
                };
            })
            .ToList();

        return Ok(featureDtos);
    }
}
