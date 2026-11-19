namespace SupernovaBench.Core.Extras;

/// <summary>Faixa do FPS para a cor do visor.</summary>
public enum FpsBand { Good, Ok, Bad }

/// <summary>Retângulo em pixels físicos da tela (como o Windows informa, sem escala de DPI).</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>Regras de exibição do visor de FPS (sem WPF, para poder testar).</summary>
public static class OverlayLayout
{
    public const string DefaultCorner = "topright";
    public static readonly string[] Corners = { "topleft", "topright", "bottomleft", "bottomright" };

    /// <summary>Verde a partir de 60 FPS, amarelo de 30 a 59, vermelho abaixo de 30.</summary>
    public static FpsBand Band(double fps) => fps >= 60 ? FpsBand.Good : fps >= 30 ? FpsBand.Ok : FpsBand.Bad;

    /// <summary>Posição (canto superior esquerdo da janela) no canto pedido do monitor, com margem. Canto desconhecido = superior direito.</summary>
    public static (int X, int Y) Place(string? corner, PixelRect monitor, int windowWidth, int windowHeight, int margin)
    {
        corner = Corners.Contains(corner) ? corner : DefaultCorner;
        int x = corner!.EndsWith("right", StringComparison.Ordinal) ? monitor.Right - windowWidth - margin : monitor.Left + margin;
        int y = corner.StartsWith("bottom", StringComparison.Ordinal) ? monitor.Bottom - windowHeight - margin : monitor.Top + margin;
        // Nunca sai da tela, mesmo se a janela for maior que o espaço.
        x = Math.Max(monitor.Left, Math.Min(x, monitor.Right - windowWidth));
        y = Math.Max(monitor.Top, Math.Min(y, monitor.Bottom - windowHeight));
        return (x, y);
    }
}
