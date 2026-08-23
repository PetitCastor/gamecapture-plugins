using GameCapture.Contracts;
using GameCapture.Sdk;

namespace SignaturePlugin;

/// <summary>
/// The regions this plugin subscribes, in reference space (2560x1440). Static for the life of the
/// process because the host reads it once per connect and sends it as the initial subscription:
/// per-tick atomicity means there is no mid-tick round-trip that could add a region later.
/// </summary>
public static class Rois
{
    /// <summary>The RS signature number near top-center of the scan MFD (the map-pin icon sits
    /// just left of this rect and is deliberately excluded — including it makes Windows OCR
    /// return empty text). Calibrated 2026-08-23 against a real 2560x1440 engine capture of a
    /// completed mining scan (`frame_20260823_164053_757.png`, signature 3,400 / Lindinium):
    /// verified by direct OcrPipeline probe, not just visual inspection — a tighter, text-only
    /// crop at this same scale reads "3800" (Windows OCR confuses the HUD font's "4" for "8"
    /// without margin around the glyphs) or returns nothing; this rect's extra padding is load-
    /// bearing, not slack to trim. Scale is the OCR upscale factor — small text needs 2-4.</summary>
    public static readonly RoiSubscription Counter =
        new("counter", new RoiRect(1264, 454, 70, 44), 3.0, RoiKind.Text);

    /// <summary>A field, not <c>=> [Counter]</c>: the set never changes, and an
    /// expression-bodied property would build a fresh array on every read.</summary>
    public static readonly IReadOnlyList<RoiSubscription> All = [Counter];
}
