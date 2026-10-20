namespace Unosquare.FFME.Rendering
{
    using System;
    using System.Windows.Threading;
    using Common;
    using Container;
    using Diagnostics;
    using Engine;

    /// <summary>
    /// Presents video frames through an <see cref="IDirect3DVideoProcessor"/> provided by the host
    /// application. The processor owns the image source (a D3DImage) which gets assigned to the video view.
    /// All processor calls happen on the video view's dispatcher thread.
    /// </summary>
    internal sealed class Direct3DVideoRenderer : VideoRendererBase
    {
        private bool m_LoggedMissingProcessor;

        public Direct3DVideoRenderer(MediaEngine mediaCore)
            : base(mediaCore)
        {
        }

        public override void Render(MediaBlock mediaBlock, TimeSpan clockPosition)
        {
            var block = BeginRenderingCycle(mediaBlock);
            if (block == null) return;

            IDisposable blockLock = null;

            try
            {
                if (!block.TryAcquireReaderLock(out blockLock))
                    return;

                var processor = MediaElement?.RendererOptions?.Direct3DProcessor;
                if (processor == null)
                {
                    if (!m_LoggedMissingProcessor)
                    {
                        m_LoggedMissingProcessor = true;
                        this.LogWarning(Aspects.VideoRenderer,
                            $"{nameof(Direct3DVideoRenderer)}: RendererOptions.Direct3DProcessor is not set; video will not be displayed.");
                    }

                    return;
                }

                var task = VideoDispatcher?.InvokeAsync(
                    () =>
                    {
                        var view = MediaElement?.VideoView;
                        var element = view?.Element;
                        if (view == null || element == null) return;

                        // 输出尺寸 = 视频视图的实际渲染尺寸 × DPI 倍率，传给着色器链作为 OUTPUT
                        var renderSize = element.RenderSize;
                        var outputWidth = Math.Max(1, (int)Math.Round(renderSize.Width * DpiX / DefaultDpi));
                        var outputHeight = Math.Max(1, (int)Math.Round(renderSize.Height * DpiY / DefaultDpi));

                        var source = processor.Render(
                            block.Buffer,
                            block.PixelWidth,
                            block.PixelHeight,
                            block.PictureBufferStride,
                            outputWidth,
                            outputHeight);
                        if (source != null && !ReferenceEquals(view.Source, source))
                        {
                            view.Source = source;
                        }
                    },
                    DispatcherPriority.Loaded);
                task?.Wait();
            }
            catch (Exception ex)
            {
                this.LogError(Aspects.VideoRenderer, $"{nameof(Direct3DVideoRenderer)}.{nameof(Render)} frame processing failed.", ex);
            }
            finally
            {
                blockLock?.Dispose();
                FinishRenderingCycle(block, clockPosition);
            }
        }

        public override void OnClose()
        {
            base.OnClose();

            // 处理器由内核侧持有；这里仅在其所属的呈现线程上触发释放（实现内部保证幂等）
            var processor = MediaElement?.RendererOptions?.Direct3DProcessor;
            if (processor == null) return;

            try
            {
                VideoDispatcher?.InvokeAsync(() => processor.Dispose(), DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                this.LogWarning(Aspects.VideoRenderer, $"{nameof(Direct3DVideoRenderer)}.{nameof(OnClose)} dispose dispatch failed: {ex.Message}");
            }
        }
    }
}
