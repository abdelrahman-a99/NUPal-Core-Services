using Microsoft.AspNetCore.Mvc;
using NUPAL.Core.Application.DTOs;
using NUPAL.Core.Application.Interfaces;
using Nupal.Domain.Entities;

namespace NUPAL.Core.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ContactController : ControllerBase
    {
        private readonly IContactRepository _contactRepository;

        public ContactController(IContactRepository contactRepository)
        {
            _contactRepository = contactRepository;
        }

        [HttpPost]
        public async Task<IActionResult> Submit([FromBody] ContactDto contactDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var message = new ContactMessage
            {
                StudentName = contactDto.StudentName,
                StudentEmail = contactDto.StudentEmail,

                Message = contactDto.Message,
                SubmittedAt = DateTime.UtcNow
            };

            await _contactRepository.AddAsync(message);

            return Ok(new { message = "Message received successfully" });
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var messages = await _contactRepository.GetAllAsync();
            return Ok(messages);
        }
    }
}
