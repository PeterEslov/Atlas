namespace Atlas.Application.Common.Interfaces;

/// <summary>
/// Hashes and verifies passwords. Implemented in Atlas.Infrastructure using
/// PBKDF2 (System.Security.Cryptography, part of the BCL — no extra package).
/// Atlas.Domain never sees a plaintext password or the hashing algorithm; it
/// only stores whatever opaque string this produces (see User.SetPassword).
/// </summary>
public interface IPasswordHasher
{
    string Hash(string plaintextPassword);

    bool Verify(string plaintextPassword, string passwordHash);
}
