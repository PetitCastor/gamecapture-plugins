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
    /// <summary>The RS signature number on the floating pin-badge over a scanned target — not
    /// part of the READY TO SCAN MFD panel, which sits separately at screen left. Calibrated
    /// 2026-08-23 against a real 2560x1440 engine capture of a completed mining scan
    /// (`frame_20260823_164053_757.png`, signature 3,400 / Lindinium): verified by direct
    /// OcrPipeline probe, not just visual inspection — a tighter, text-only crop at this same
    /// scale reads "3800" (Windows OCR confuses the HUD font's "4" for "8" without margin around
    /// the glyphs) or returns nothing; this rect's ~20px left/right and ~14px top/bottom margin
    /// around the digits is load-bearing, not slack to trim. The pin icon's densest column sits
    /// at x≈1264, this rect's left edge — a sliver of it is inside the crop, which does not stop
    /// OCR from reading cleanly.
    /// <para>
    /// The badge's measured center (x≈1281.5, ≈50.06% of 2560) matches a screen-centered locked-
    /// target reticle, which is why a fixed rect is plausible here at all — but that is inferred
    /// from this one frame, not confirmed across a scan where the target sits elsewhere on
    /// screen. If a future capture shows the badge off-center, this rect is calibrated to one
    /// camera angle, not a stable HUD element, and needs to be recalibrated. A wider reading
    /// (e.g. a six-digit cluster total) grows left toward the pin — the tighter margin — so watch
    /// that direction first if OCR starts missing again.
    /// </para>
    /// Scale is the OCR upscale factor — small text needs 2-4.</summary>
    public static readonly RoiSubscription Counter =
        new("counter", new RoiRect(1264, 454, 70, 44), 3.0, RoiKind.Text);

    /// <summary>A field, not <c>=> [Counter]</c>: the set never changes, and an
    /// expression-bodied property would build a fresh array on every read.</summary>
    public static readonly IReadOnlyList<RoiSubscription> All = [Counter];
}
