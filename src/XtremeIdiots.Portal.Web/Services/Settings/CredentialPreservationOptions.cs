namespace XtremeIdiots.Portal.Web.Services.Settings;

public sealed record CredentialPreservationOptions(
    bool FileTransportPassword,
    bool FileTransportPrivateKey,
    bool FileTransportPrivateKeyPassphrase,
    bool FileTransportHostKeyFingerprint,
    bool RconPassword);
