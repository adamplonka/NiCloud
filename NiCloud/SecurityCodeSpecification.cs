namespace NiCloud;

public record SecurityCodeSpecification(
    int Length,
    bool TooManyCodesSent,
    bool TooManyCodesValidated,
    bool SecurityCodeLocked,
    bool SecurityCodeCooldown
);