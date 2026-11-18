// Python: tools.py IMAGE_PX, IMAGES_HELD, LocalTools.__init__ (self.held)
namespace Mini.Core.Tools.Files;

/// <summary>What the transcript holds of images. A transcript can't drop an image once sent, so read stays under limits every later request must meet.</summary>
public sealed class ImageBudget
{
    // Image limits, measured on the Claude API (2026-11-05, Haiku 5.5): one image may be 8000 px a side, but a request
    // holding over 20 images refuses any past 3000 px ("max allowed size for many-image requests: 3000 pixels"); single
    // images of 6.0 and 10.1 MB base64 and a 39.6 MB request went through. A transcript can't drop an image once sent, so
    // read stays under limits every later request must meet: 3000 px, 5 MB a file, 20 MB of images held.
    // Python: tools.py IMAGE_PX
    public const int MaxPixels = 3000;

    // Python: tools.py IMAGES_HELD
    public const long MaxHeld = 20_000_000;

    // Python: tools.py LocalTools.read (the 5_000_000 it checks inline)
    public const long MaxFile = 5_000_000;

    /// <summary>Base64 bytes of images in the transcript; the Agent resets it on resume and compaction.</summary>
    public long Held { get; private set; }

    /// <summary>Would adding an image of this many base64 bytes pass the limit? Every request sends them all again.</summary>
    public bool WouldExceed(long base64Length) => Held + base64Length > MaxHeld;

    public void Add(long base64Length) => Held += base64Length;

    public void Reset(long held) => Held = held;
}
