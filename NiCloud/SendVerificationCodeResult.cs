namespace NiCloud;

public record SendVerificationCodeResult(
    PhoneNumber[] TrustedPhoneNumbers,
    PhoneNumber PhoneNumber,
    SecurityCodeSpecification SecurityCode,
    VerificationMode Mode,
    AuthenticationType? AuthenticationType,
    PhoneNumber TrustedPhoneNumber
);
