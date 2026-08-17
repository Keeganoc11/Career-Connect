using CareerConnect.Api.Services;

namespace CareerConnect.Api.Tests;

public sealed class FakeEmailStatusClassifier : IEmailStatusClassifier
{
    public bool IsConfigured { get; set; } = true;
    public List<EmailClassificationMatch> Result { get; set; } = [];
    public List<EmailNewApplicationMatch> NewApplicationResult { get; set; } = [];
    public Exception? ThrowOnClassify { get; set; }

    public List<CandidateEmail>? LastEmails { get; private set; }
    public List<OpenApplicationContext>? LastApplications { get; private set; }

    public Task<EmailClassificationResult> ClassifyAsync(
        List<CandidateEmail> emails,
        List<OpenApplicationContext> openApplications,
        CancellationToken cancellationToken = default)
    {
        LastEmails = emails;
        LastApplications = openApplications;
        if (ThrowOnClassify is not null)
        {
            throw ThrowOnClassify;
        }
        return Task.FromResult(new EmailClassificationResult(Result, NewApplicationResult));
    }
}
