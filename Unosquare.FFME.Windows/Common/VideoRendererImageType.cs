namespace Unosquare.FFME.Common
{
    /// <summary>
    /// Enumerates the different Video renderer image types.
    /// </summary>
    public enum VideoRendererImageType
    {
        /// <summary>
        /// Uses a tear-free WriteableBitmap.
        /// </summary>
        WriteableBitmap,

        /// <summary>
        /// Uses the faster, non tear-free InteropBitmap.
        /// </summary>
        InteropBitmap,

        /// <summary>
        /// Uses an <see cref="IDirect3DVideoProcessor"/> supplied by the host application to process
        /// frames on the GPU and present them through a D3DImage. Requires
        /// <see cref="RendererOptions.Direct3DProcessor"/> to be set before opening media.
        /// </summary>
        Direct3D
    }
}
