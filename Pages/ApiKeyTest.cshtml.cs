using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using TestAPIKeyCalling.Services;

namespace TestAPIKeyCalling.Pages;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ApiKeyTestModel(IGeminiService gemini) : PageModel
{
    [BindProperty]
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Input { get; set; } = "";

    public string Output { get; private set; } = "";
    public string StatusMessage { get; private set; } = "";
    public string StatusStyle { get; private set; } = "";

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Output = "";
        if (InputValidation.GetError(Input) is { } error)
        {
            StatusMessage = error;
            StatusStyle = "danger";
            return Page();
        }

        if (!ModelState.IsValid)
        {
            StatusMessage = "輸入格式不正確，請重新輸入。";
            StatusStyle = "danger";
            return Page();
        }

        try
        {
            var result = await gemini.GenerateAsync(Input, HttpContext.RequestAborted);
            Output = result.Text;
            StatusMessage = result.Message;
            StatusStyle = result.Outcome switch
            {
                GeminiOutcome.Success => "success",
                GeminiOutcome.Truncated => "warning",
                _ => "danger"
            };
            return Page();
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            return new EmptyResult();
        }
    }
}