using CareerConnect.Api.Contracts;
using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Services;

public interface IApplicationService
{
    Task<List<ApplicationResponse>> ListAsync(Guid userId);
    Task<ApplicationResponse?> GetAsync(Guid userId, Guid id);
    Task<ApplicationResponse> CreateAsync(Guid userId, CreateApplicationRequest request);
    Task<ApplicationResponse?> UpdateAsync(Guid userId, Guid id, UpdateApplicationRequest request);
    Task<ApplicationResponse?> UpdateStatusAsync(
        Guid userId, Guid id, ApplicationStatus newStatus, ChangeSource source = ChangeSource.Manual);
    Task<ApplicationResponse?> UpdateDocumentsAsync(Guid userId, Guid id, UpdateApplicationDocumentsRequest request);
    /// <summary>The tailored resume's layout, for the PDF download. Null until a prep run has produced one.</summary>
    Task<(ResumeLayout Layout, string CompanyName)?> GetTailoredResumeAsync(Guid userId, Guid id);
    Task<bool> DeleteAsync(Guid userId, Guid id);
    Task<SummaryResponse> GetSummaryAsync(Guid userId);
}
