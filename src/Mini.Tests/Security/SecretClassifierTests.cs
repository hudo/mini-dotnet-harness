namespace Mini.Tests.Security;

/// <summary>SecretClassifier on path text alone: no files are touched.</summary>
// Python test helper: tests/test_mini.py (the secret-file cases of test_secret_files_need_approval, run on the classifier directly)
public class SecretClassifierTests
{
    [Theory]
    [InlineData("/p/.env")]
    [InlineData("/p/.env.local")]
    [InlineData("/p/.env.production")]
    [InlineData("/p/server.pem")]
    [InlineData("/p/tls.KEY")]
    [InlineData("/p/store.p12")]
    [InlineData("/p/store.pfx")]
    [InlineData("/p/release.jks")]
    [InlineData("/p/app.keystore")]
    [InlineData("/p/id_rsa")]
    [InlineData("/p/id_ed25519.pub")]
    [InlineData("/p/.netrc")]
    [InlineData("/p/.npmrc")]
    [InlineData("/p/.pypirc")]
    [InlineData("/p/.git-credentials")]
    [InlineData("/p/credentials")]
    [InlineData("/p/Credentials.json")]
    [InlineData("/p/secret.yaml")]
    [InlineData("/p/secrets.toml")]
    public void NamesThatHoldKeysAreSecret(string path) => Assert.True(SecretClassifier.IsSecret(path));

    [Theory]
    [InlineData("/home/u/.ssh/config")]
    [InlineData("/home/u/.aws/credentials-backup/x.txt")]
    [InlineData("/home/u/.gnupg/pubring.kbx")]
    [InlineData("/home/u/.kube/config")]
    [InlineData("/home/u/.docker/config.json")]
    [InlineData("/home/u/.azure/x")]
    [InlineData("/home/u/.config/gcloud/properties")]
    public void AnythingInASecretFolderIsSecret(string path) => Assert.True(SecretClassifier.IsSecret(path));

    [Theory]
    [InlineData("/p/.env.example")]
    [InlineData("/p/.env.sample")]
    [InlineData("/p/.env.template")]
    [InlineData("/p/notes.txt")]
    [InlineData("/p/environment.md")]
    [InlineData("/p/keyboard.txt")]
    [InlineData("/p/credentials.txt")]
    [InlineData("/p/secretary.yaml")]
    [InlineData("/p/src/ssh/readme.md")]
    public void TemplatesAndLookalikesAreNot(string path) => Assert.False(SecretClassifier.IsSecret(path));

    [Fact]
    public void TheMatchIgnoresCase()
    {
        Assert.True(SecretClassifier.IsSecret("/p/.ENV"));
        Assert.True(SecretClassifier.IsSecret("/home/u/.SSH/known_hosts"));
        Assert.True(SecretClassifier.IsSecret("/p/ID_RSA"));
    }
}
