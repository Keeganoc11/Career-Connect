using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CareerConnect.Api.Tests;

public sealed class PlanServiceTests : IDisposable
{
    private readonly TestDatabase _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private PlanService Service(params (string Key, string Value)[] settings) => new(
        _fixture.Db,
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build());

    [Fact]
    public async Task GetPlanAsync_ReadsThePlanOnTheRow()
    {
        var free = _fixture.SeedUser("free@example.com", PlanTier.Free);
        var pro = _fixture.SeedUser("pro@example.com", PlanTier.Pro);

        Assert.Equal(PlanTier.Free, await Service().GetPlanAsync(free));
        Assert.Equal(PlanTier.Pro, await Service().GetPlanAsync(pro));
    }

    [Fact]
    public async Task GetPlanAsync_TreatsAComplimentaryEmailAsPro_WhateverTheRowSays()
    {
        var userId = _fixture.SeedUser("owner@example.com", PlanTier.Free);

        // Case and spacing come from a config value typed by hand.
        var service = Service(("Billing:ProEmails", " Owner@Example.com , someone@else.com "));

        Assert.Equal(PlanTier.Pro, await service.GetPlanAsync(userId));
        Assert.True(await service.IsProAsync(userId));
    }

    [Fact]
    public async Task GetPlanAsync_SaysFree_ForAUserThatNoLongerExists() =>
        Assert.Equal(PlanTier.Free, await Service().GetPlanAsync(Guid.NewGuid()));

    [Fact]
    public async Task FilterProAsync_KeepsOnlyProUsers()
    {
        var free = _fixture.SeedUser("free@example.com", PlanTier.Free);
        var paid = _fixture.SeedUser("paid@example.com", PlanTier.Pro);
        var comped = _fixture.SeedUser("comped@example.com", PlanTier.Free);
        var deleted = Guid.NewGuid();

        var pro = await Service(("Billing:ProEmails", "comped@example.com"))
            .FilterProAsync([free, paid, comped, deleted]);

        Assert.Equal([paid, comped], pro.Order().ToHashSet().Order());
        Assert.DoesNotContain(free, pro);
        Assert.DoesNotContain(deleted, pro);
    }

    [Fact]
    public async Task FilterProAsync_HandlesAnEmptySet() =>
        Assert.Empty(await Service().FilterProAsync([]));

    [Fact]
    public async Task ExistingUsersDefaultToFree()
    {
        // The column was added to a table that already had rows; the migration
        // defaults them to Free, and so does the model for a user created
        // without a plan being mentioned at all.
        _fixture.Db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = "new@example.com",
            PasswordHash = "not-a-real-hash",
            CreatedAtUtc = DateTime.UtcNow,
        });
        await _fixture.Db.SaveChangesAsync();

        var stored = await _fixture.Db.Users.AsNoTracking().FirstAsync(u => u.Email == "new@example.com");
        Assert.Equal(PlanTier.Free, stored.Plan);
    }
}
