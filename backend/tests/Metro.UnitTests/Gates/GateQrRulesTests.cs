using Metro.Application.Abstractions.Gates;
using Metro.Application.Gates;
using Metro.Infrastructure.Gates;
using Microsoft.Extensions.Options;

namespace Metro.UnitTests.Gates;

public sealed class GateQrRulesTests
{
    [Fact]
    public async Task Missing_SMTP_configuration_is_a_delivery_failure_and_never_a_fake_acceptance()
    {
        var sender=new SmtpGateEmailSender(Options.Create(new GateEmailOptions()));
        var error=await Assert.ThrowsAsync<GateException>(()=>sender.SendAsync(new(Guid.NewGuid(),"demo@example.invalid","Test","Test"),CancellationToken.None));
        Assert.Equal("EMAIL_NOT_CONFIGURED",error.Code);
    }
    [Theory]
    [InlineData("  Nguyễn   Văn An ","NGUYỄN VĂN AN")]
    [InlineData("Gate Passenger","GATE PASSENGER")]
    public void Identity_comparison_normalizes_spacing_and_unicode_without_dropping_accents(string name,string expected)
        =>Assert.Equal(expected,GateQrRules.NormalizeName(name));

    [Fact]
    public void Support_requires_a_separate_bound_operation_and_complete_proof()
    {
        var r=new GateQrAccessRequest(Guid.NewGuid(),Guid.NewGuid(),"REPLACE","Gate Passenger",new(1990,1,1),"Lost QR","EMAIL",Guid.NewGuid(),"123456");
        GateQrRules.Validate(r,r.RequestId.ToString());
        Assert.Throws<GateException>(()=>GateQrRules.Validate(r with{OperationId=r.RequestId},r.RequestId.ToString()));
        Assert.Throws<GateException>(()=>GateQrRules.Validate(r with{EmailCode="１２３４５６"},r.RequestId.ToString()));
        Assert.Throws<GateException>(()=>GateQrRules.Validate(r with{VerificationMethod="DOCUMENT"},r.RequestId.ToString()));
        var doc=r with{VerificationMethod="DOCUMENT",ChallengeId=null,EmailCode=null,NoEmailAccess=true,DocumentMatchesProfile=true,EvidenceReference="case-1"};
        GateQrRules.Validate(doc,doc.RequestId.ToString());
        Assert.Throws<GateException>(()=>GateQrRules.Validate(doc with{NoEmailAccess=false},doc.RequestId.ToString()));
        Assert.Throws<GateException>(()=>GateQrRules.Validate(doc with{EmailCode="123456"},doc.RequestId.ToString()));
    }
}
