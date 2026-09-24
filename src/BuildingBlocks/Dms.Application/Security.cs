namespace Dms.Application;

/// <summary>Slow, salted password hashing: user passwords and share-link passwords.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);
}

/// <param name="Value">Handed to the client once and never stored.</param>
/// <param name="Hash">What is stored and looked up.</param>
public sealed record GeneratedToken(string Value, byte[] Hash);

/// <summary>High-entropy bearer tokens (refresh tokens, share links) stored only as a digest.</summary>
public interface ISecureTokenGenerator
{
    GeneratedToken Create();

    byte[] ComputeHash(string token);
}
