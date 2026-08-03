using CareerConnect.Api.Contracts;
using CareerConnect.Api.Domain;

namespace CareerConnect.Api.Services;

public interface IApplicationService
{
    Task<List<ApplicationResponse>> ListAsync(Guid userId);
    Task<ApplicationResponse?> GetAsync(Guid userId, Guid id);
    Task<ApplicationResponse> CreateAsync(Guid userId, CreateApplicationRequest request);
    Task<ApplicationResponse?> UpdateAsync(Guid userId, Guid id, UpdateApplicationRequest request);
    Task<ApplicationResponse?> UpdateStatusAsync(Guid userId, Guid id, ApplicationStatus newStatus);
    Task<bool> DeleteAsync(Guid userId, Guid id);
    Task<SummaryResponse> GetSummaryAsync(Guid userId);
}
