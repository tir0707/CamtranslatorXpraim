namespace CamtranslatorXpraim.Video;

// WebRTC owns the jitter buffer, congestion control and GPU decode selection.
// End-to-end camera latency is deliberately not inferred from network RTT.
public sealed record VideoStats(int Width, int Height, double Fps, double Bitrate, double? Rtt, double? JitterMs, double? DecodeMs, double Loss)
{
    public string Display => $"{Width}×{Height}  •  {Fps:F1} FPS  •  {Bitrate / 1_000_000:F2} Мбит/с\nRTT: {Format(Rtt)}  •  Буфер: {Format(JitterMs)}  •  Декодирование: {Format(DecodeMs)}  •  Потери: {Loss:P1}\nЗадержка камера → экран: не измерена";
    private static string Format(double? value) => value.HasValue && double.IsFinite(value.Value) ? $"{value:F1} мс" : "—";
}
