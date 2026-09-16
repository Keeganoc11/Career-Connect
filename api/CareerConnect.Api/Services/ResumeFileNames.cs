using System.Text;
using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Services;

public static class ResumeFileNames
{
    /// <summary>
    /// "Resume_KeeganOConnell_Stripe.pdf" — the name from the resume's first
    /// line, and the company, with anything a file system or an upload form
    /// might choke on removed.
    /// </summary>
    public static string ForApplication(ResumeLayout layout, string companyName)
    {
        var name = layout.Lines.FirstOrDefault(l => l.Kind != ResumeLineKind.Blank)?.Text ?? "Resume";
        return $"Resume_{Safe(name, keepSpaces: false)}_{Safe(companyName, keepSpaces: false)}.pdf";
    }

    public static string Safe(string text, bool keepSpaces = true)
    {
        var result = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            {
                result.Append(c);
            }
            else if (c == ' ' && keepSpaces)
            {
                result.Append(' ');
            }
        }

        var safe = result.ToString().Trim();
        return safe.Length == 0 ? "Resume" : safe[..Math.Min(safe.Length, 80)];
    }
}
