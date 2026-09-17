using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

public class CompanyNamesTests
{
    [Theory]
    [InlineData("Delta Dental of Missouri", "Delta Dental MO", true)]
    [InlineData("Delta Dental", "Delta Dental of Missouri", true)]
    [InlineData("Walmart", "Walmart Inc.", true)]
    [InlineData("MiTek Inc.", "mitek", true)]
    [InlineData("The Home Depot", "Home Depot", true)]
    [InlineData("Capital One", "Capital Group", false)]
    [InlineData("Stripe", "Square", false)]
    [InlineData("", "Stripe", false)]
    public void Same_TreatsDifferentSpellingsOfOneEmployerAsOne(string a, string b, bool expected)
    {
        Assert.Equal(expected, CompanyNames.Same(a, b));
    }

    [Theory]
    [InlineData("Full Stack Engineer Associate", "full-stack engineer associate", true)]
    [InlineData("", "Software Engineer", true)]
    [InlineData("Software Engineer I", "Software Engineer II", false)]
    public void SameRole_IgnoresCaseAndPunctuation_AndAnUnstatedRoleMatchesAnything(string a, string b, bool expected)
    {
        Assert.Equal(expected, CompanyNames.SameRole(a, b));
    }
}
