using System.Text.RegularExpressions;

// Python: tools.py LocalTools._secret, LocalTools.SECRET, LocalTools.SECRET_DIRS
namespace Mini.Core.Security;

/// <summary>Does a path name a file that usually holds keys or passwords, or live in a folder of them? Pure: only the path's text counts.</summary>
public static partial class SecretClassifier
{
    // Files that usually hold keys or passwords, and folders of them. Templates (.env.example) are fine.
    // Python: tools.py LocalTools.SECRET
    [GeneratedRegex(@"^\.env(?!\.(example|sample|template)$)|\.(pem|key|p12|pfx|jks|keystore)$|^id_(rsa|dsa|ecdsa|ed25519)"
        + @"|^(\.netrc|\.npmrc|\.pypirc|\.git-credentials|credentials(\.json)?|secrets?\.(json|ya?ml|toml))$", RegexOptions.CultureInvariant)]
    private static partial Regex SecretFileName();

    // Python: tools.py LocalTools.SECRET_DIRS
    private static readonly HashSet<string> SecretDirs = [".ssh", ".aws", ".gnupg", ".kube", ".docker", ".azure", "gcloud"];

    // Python: tools.py LocalTools._secret
    public static bool IsSecret(string path)
    {
        var parts = PathParts.Of(path).Select(x => x.ToLowerInvariant()).ToArray();
        return SecretDirs.Overlaps(parts) || SecretFileName().IsMatch(parts[^1]);
    }
}
