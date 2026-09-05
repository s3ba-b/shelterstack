using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ShelterStack.Adoptions.Api.Auth;
using ShelterStack.Adoptions.Api.Tenancy;
using Xunit;

namespace ShelterStack.Adoptions.Api.Tests;

public class ClaimsTenantContextTests
{
    private static IHttpContextAccessor AccessorWithClaims(params Claim[] claims)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test")),
        };

        return new HttpContextAccessor { HttpContext = httpContext };
    }

    [Fact]
    public void Resolves_TenantId_FromClaim()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new ClaimsTenantContext(
            AccessorWithClaims(new Claim(TokenAuth.TenantIdClaim, tenantId.ToString()))
        );

        Assert.Equal(tenantId, tenantContext.TenantId);
    }

    [Fact]
    public void Throws_WhenTenantClaimMissing()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ClaimsTenantContext(AccessorWithClaims())
        );
    }

    [Fact]
    public void Throws_WhenTenantClaimIsNotAGuid()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ClaimsTenantContext(
                AccessorWithClaims(new Claim(TokenAuth.TenantIdClaim, "not-a-guid"))
            )
        );
    }

    [Fact]
    public void Throws_WhenNoHttpContext()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };

        Assert.Throws<InvalidOperationException>(() => new ClaimsTenantContext(accessor));
    }
}
