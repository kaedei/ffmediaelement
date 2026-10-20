namespace Unosquare.FFME.Common
{
    using System;
    using System.Windows.Media;

    /// <summary>
    /// Processes decoded BGRA video frames on the GPU and presents them through a D3D-backed image source.
    /// Implemented by the host application (e.g. an Anime4K shader chain), and used when
    /// <see cref="RendererOptions.VideoImageType"/> is set to <see cref="VideoRendererImageType.Direct3D"/>.
    /// </summary>
    /// <remarks>
    /// All calls to this interface happen on the video view's dispatcher thread, except <see cref="IDisposable.Dispose"/>
    /// which may be called from any thread. The implementation owns any D3D device resources it creates.
    /// </remarks>
    public interface IDirect3DVideoProcessor : IDisposable
    {
        /// <summary>
        /// Processes one decoded frame and returns the image source to display (typically a D3DImage).
        /// The same instance is returned on every call; its contents are updated in place.
        /// </summary>
        /// <param name="bgraFrameData">Pointer to the decoded frame in BGRA/BGR0 byte order.</param>
        /// <param name="frameWidth">Frame width in pixels.</param>
        /// <param name="frameHeight">Frame height in pixels.</param>
        /// <param name="frameStride">Frame stride in bytes.</param>
        /// <param name="outputWidth">Desired output width in physical pixels.</param>
        /// <param name="outputHeight">Desired output height in physical pixels.</param>
        /// <returns>The image source to display, or null when the processor has permanently failed.</returns>
        ImageSource Render(IntPtr bgraFrameData, int frameWidth, int frameHeight, int frameStride, int outputWidth, int outputHeight);
    }
}
