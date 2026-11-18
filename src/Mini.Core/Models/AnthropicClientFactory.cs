using System.Text.Json;
using Anthropic;
using BetaCreate = Anthropic.Models.Beta.Messages.MessageCreateParams;

// Python: loop.py need_key, Agent.__init__ (client construction)
// Python: cli.py main (--models)
namespace Mini.Core.Models;

/// <summary>
/// Builds the Anthropic SDK clients, and owns the guard that keeps a provider's request from carrying Anthropic credentials.
/// With <see cref="AnthropicModelClient"/>, the only code that touches the SDK (Anthropic 13.4.0).
/// </summary>
public sealed class AnthropicClientFactory
{
    static readonly Dictionary<string, JsonElement> Empty = new();

    // Static on purpose, and not a field of the instance: the SDK's header table (ParamsBase) is process-wide, so "it was
    // frozen, and it captured the variable" is a fact about the process. A second factory in the same process (a test's, a
    // second composition root) must find the guard as the first one left it, not reset it and let a captured table pass.
    static readonly object Gate = new();
    static bool _frozen, _captured;

    readonly Func<string, string?> _readEnvironment;

    /// <param name="readEnvironment">The value of a variable of the live process environment, or null when it is not set.
    /// It must read the live environment, not a copy taken at startup: the SDK reads ANTHROPIC_CUSTOM_HEADERS from the real
    /// environment, so a startup snapshot would check a stale copy; and the provider keys are named by models.json, so no
    /// options record can list them ahead of time.</param>
    public AnthropicClientFactory(Func<string, string?> readEnvironment) => _readEnvironment = readEnvironment;

    /// <summary>An Anthropic client, or one pointed at another provider's Anthropic-format endpoint.</summary>
    // Checked before anything is written. Without its own key the SDK would send the Anthropic key elsewhere.
    // Python: loop.py need_key, Agent.__init__ (client construction)
    public AnthropicModelClient Create(string model, ModelConfig? other)
    {
        var dirty = FreezeHeaders(); // must run before the first request: see FreezeHeaders
        if (other is null)
        {
            if (string.IsNullOrEmpty(_readEnvironment("ANTHROPIC_API_KEY"))
                && string.IsNullOrEmpty(_readEnvironment("ANTHROPIC_AUTH_TOKEN")))
            {
                throw new MissingCredentialsException($"{model} needs ANTHROPIC_API_KEY set (README: Keys).");
            }

            return new AnthropicModelClient(new AnthropicClient());
        }
        var key = other.Key ?? throw new ModelConfigurationException($"{model} has no \"key\" in models.json");
        var baseUrl = other.BaseUrl ?? throw new ModelConfigurationException($"{model} has no \"base_url\" in models.json");
        var secret = _readEnvironment(key);
        // The SDK reads ANTHROPIC_CUSTOM_HEADERS once into a process-wide table (ParamsBase) and adds it to every request
        // of every client; no ClientOptions field can turn that off. Refuse when it is set now, and also when the table
        // captured it before: by then the variable may be unset, and the table is exactly what would leak.
        if (string.IsNullOrEmpty(secret) || dirty || _readEnvironment("ANTHROPIC_CUSTOM_HEADERS") is not null)
        {
            throw new MissingCredentialsException($"{model} needs {key} set and ANTHROPIC_CUSTOM_HEADERS unset (the SDK sends those anywhere"
                + (dirty ? "; the SDK captured them earlier in this process, so restart mini without it" : "") + ").");
        }
        // An explicit ApiKey makes the credential "passed": ANTHROPIC_API_KEY, ANTHROPIC_AUTH_TOKEN, ANTHROPIC_PROFILE,
        // the config-dir profiles and workload-identity variables are then never read, and an explicit BaseUrl
        // wins over ANTHROPIC_BASE_URL.
        // shortcut: the SDK sends this key as Authorization: Bearer, Python sends x-api-key; DeepSeek accepts both,
        // upgrade if a models.json provider accepts only x-api-key.
        return new AnthropicModelClient(new AnthropicClient(new Anthropic.Core.ClientOptions { BaseUrl = baseUrl, ApiKey = secret }));
    }

    /// <summary>The ids of the models the Anthropic account can use (<c>--models</c>).</summary>
    // Python: cli.py main (--models)
    public async Task<List<string>> ListModelsAsync()
    {
        FreezeHeaders(); // same first-touch rule as Create, in case this runs before any Create() in the process
        using var client = new AnthropicClient();
        var ids = new List<string>();
        var page = await client.Models.List();
        while (true)
        {
            ids.AddRange(page.Items.Select(m => m.ID));
            if (!page.HasNext()) return ids;
            page = await page.Next();
        }
    }

    /// <summary>Read ANTHROPIC_CUSTOM_HEADERS into the SDK's process-wide header table now, and report whether it was
    /// set. ParamsBase reads that variable once, when the first request type is made, and nothing public can clear it
    /// afterwards. This factory is the only code that touches the SDK, so the first Create() is that moment: the table is frozen
    /// before any request exists, and a variable that appears later can never reach it.</summary>
    bool FreezeHeaders()
    {
        lock (Gate)
        {
            if (_frozen) return _captured;
            _frozen = true;
            _captured = _readEnvironment("ANTHROPIC_CUSTOM_HEADERS") is not null;
            _ = BetaCreate.FromRawUnchecked(Empty, Empty, Empty); // constructing a params object runs ParamsBase's static constructor
            return _captured;
        }
    }
}
