﻿namespace Unosquare.FFME.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Reflection;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Contains all the status properties of the stream being handled by the media engine.
    /// </summary>
    public sealed class MediaEngineState : IMediaEngineState
    {
        #region Property Backing and Private State

        private const ulong NetworkStreamCacheFactor = 30;
        private const ulong StandardStreamCacheFactor = 4;

        private static readonly TimeSpan GenericFrameStepDuration = TimeSpan.FromSeconds(0.01d);
        private static readonly PropertyInfo[] Properties = null;

        private readonly object SyncLock = new object();
        private readonly MediaEngine Parent = null;
        private readonly ReadOnlyDictionary<string, string> EmptyDictionary
            = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

        /// <summary>
        /// Gets the guessed buffered bytes in the packet queue per second.
        /// If bitrate information is available, then it returns the bitrate converted to byte rate.
        /// Returns null if it has not been guessed.
        /// </summary>
        private ulong? GuessedByteRate;

        private PlaybackStatus m_MediaState = PlaybackStatus.Close;
        private TimeSpan m_Position = default;
        private TimeSpan m_PositionNext = default;
        private TimeSpan m_PositionCurrent = default;
        private TimeSpan m_PositionPrevious = default;
        private bool m_HasMediaEnded = default;
        private bool m_IsBuffering = default;
        private double m_BufferingProgress = default;
        private ulong m_BufferCacheLength = default;
        private double m_DownloadProgress = default;
        private ulong m_DownloadCacheLength = default;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes static members of the <see cref="MediaEngineState" /> class.
        /// </summary>
        static MediaEngineState()
        {
            Properties = typeof(MediaEngineState).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaEngineState" /> class.
        /// </summary>
        /// <param name="parent">The parent.</param>
        internal MediaEngineState(MediaEngine parent)
        {
            Parent = parent;
            ResetMediaProperties();
            UpdateFixedContainerProperties();
            InitializeBufferingProperties();
        }

        #endregion

        #region Volatile Controller Properties

        /// <summary>
        /// Gets or Sets the SpeedRatio property of the media.
        /// </summary>
        public double SpeedRatio { get; set; }

        /// <summary>
        /// Returns the current video SMTPE timecode if available.
        /// If not available, this property returns an empty string.
        /// </summary>
        public string VideoSmtpeTimecode { get; internal set; } = string.Empty;

        /// <summary>
        /// Gets the name of the video hardware decoder in use.
        /// Enabling hardware acceleration does not guarantee decoding will be performed in hardware.
        /// When hardware decoding of frames is in use this will return the name of the HW accelerator.
        /// Otherwise it will return an empty string.
        /// </summary>
        public string VideoHardwareDecoder { get; internal set; } = string.Empty;

        #endregion

        #region Non-Volatile Contoller Properties

        /// <summary>
        /// Gets or Sets the Source on this MediaElement.
        /// The Source property is the Uri of the media to be played.
        /// </summary>
        public Uri Source { get; internal set; }

        /// <summary>
        /// Gets/Sets the Volume property on the MediaElement.
        /// Note: Valid values are from 0 to 1
        /// </summary>
        public double Volume { get; set; } = Constants.Controller.DefaultVolume;

        /// <summary>
        /// Gets/Sets the Balance property on the MediaElement.
        /// </summary>
        public double Balance { get; set; } = Constants.Controller.DefaultBalance;

        /// <summary>
        /// Gets/Sets the IsMuted property on the MediaElement.
        /// </summary>
        public bool IsMuted { get; set; } = false;

        #endregion

        #region Renderer Update Properties

        /// <summary>
        /// Gets the duration of a single frame step.
        /// If there is a video component with a framerate, this propery returns the length of a frame.
        /// If there is no video component it simply returns 10 milliseconds.
        /// </summary>
        public TimeSpan FrameStepDuration
        {
            get
            {
                var frameLengthMillis = 1000d * VideoFrameLength;

                if (frameLengthMillis <= 0)
                    return IsOpen ? GenericFrameStepDuration : TimeSpan.Zero;

                return TimeSpan.FromTicks(Convert.ToInt64(TimeSpan.TicksPerMillisecond * frameLengthMillis));
            }
        }

        #endregion

        #region Container Fixed, One-Time Properties

        /// <summary>
        /// Provides key-value pairs of the metadata contained in the media.
        /// Returns null when media has not been loaded.
        /// </summary>
        public ReadOnlyDictionary<string, string> Metadata { get; private set; }

        /// <summary>
        /// Gets the media format. Returns null when media has not been loaded.
        /// </summary>
        public string MediaFormat { get; private set; }

        /// <summary>
        /// Gets the index of the video stream.
        /// </summary>
        public int VideoStreamIndex { get; private set; }

        /// <summary>
        /// Gets the index of the audio stream.
        /// </summary>
        public int AudioStreamIndex { get; private set; }

        /// <summary>
        /// Gets the index of the subtitle stream.
        /// </summary>
        public int SubtitleStreamIndex { get; private set; }

        /// <summary>
        /// Returns whether the given media has audio.
        /// Only valid after the MediaOpened event has fired.
        /// </summary>
        public bool HasAudio { get; private set; }

        /// <summary>
        /// Returns whether the given media has video. Only valid after the
        /// MediaOpened event has fired.
        /// </summary>
        public bool HasVideo { get; private set; }

        /// <summary>
        /// Returns whether the given media has subtitles (in stream or preloaded). Only valid after the
        /// MediaOpened event has fired.
        /// </summary>
        public bool HasSubtitles { get; private set; }

        /// <summary>
        /// Gets the video codec.
        /// Only valid after the MediaOpened event has fired.
        /// </summary>
        public string VideoCodec { get; private set; }

        /// <summary>
        /// Gets the video bitrate.
        /// Only valid after the MediaOpened event has fired.
        /// </summary>
        public ulong VideoBitrate { get; private set; }

        /// <summary>
        /// Gets the video display rotation.
        /// </summary>
        public double VideoRotation { get; private set; }

        /// <summary>
        /// Returns the natural width of the media in the video.
        /// Only valid after the MediaOpened event has fired.
        /// </summary>
        public int NaturalVideoWidth { get; private set; }

        /// <summary>
        /// Returns the natural height of the media in the video.
        /// Only valid after the MediaOpened event has fired.
        /// </summary>
        public int NaturalVideoHeight { get; private set; }

        /// <summary>
        /// Gets the video frame rate.
        /// Only valid after the MediaOpened event has fired.
        /// </summary>
        public double VideoFrameRate { get; private set; }

        /// <summary>
        /// Gets the duration in seconds of the video frame.
        /// Only valid after the MediaOpened event has fired.
        /// </summary>
        public double VideoFrameLength { get; private set; }

        /// <summary>
        /// Gets the audio codec.
        /// Only valid after the MediaOpened event has fired.
        /// </summary>
        public string AudioCodec { get; private set; }

        /// <summary>
        /// Gets the audio bitrate.
        /// Only valid after the MediaOpened event has fired.
        /// </summary>
        public ulong AudioBitrate { get; private set; }

        /// <summary>
        /// Gets the audio channels count.
        /// Only valid after the MediaOpened event has fired.
        /// </summary>
        public int AudioChannels { get; private set; }

        /// <summary>
        /// Gets the audio sample rate.
        /// Only valid after the MediaOpened event has fired.
        /// </summary>
        public int AudioSampleRate { get; private set; }

        /// <summary>
        /// Gets the audio bits per sample.
        /// Only valid after the MediaOpened event has fired.
        /// </summary>
        public int AudioBitsPerSample { get; private set; }

        /// <summary>
        /// Gets the Media's natural duration
        /// Only valid after the MediaOpened event has fired.
        /// </summary>
        public TimeSpan? NaturalDuration { get; private set; }

        /// <summary>
        /// Returns whether the currently loaded media is live or real-time and does not have a set duration
        /// This is only valid after the MediaOpened event has fired.
        /// </summary>
        public bool IsLiveStream { get; private set; }

        /// <summary>
        /// Returns whether the currently loaded media is a network stream.
        /// This is only valid after the MediaOpened event has fired.
        /// </summary>
        public bool IsNetowrkStream { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the currently loaded media can be seeked.
        /// </summary>
        public bool IsSeekable { get; private set; }

        #endregion

        #region Self-Updating Properties

        /// <summary>
        /// Returns whether the currently loaded media can be paused.
        /// This is only valid after the MediaOpened event has fired.
        /// Note that this property is computed based on wether the stream is detected to be a live stream.
        /// </summary>
        public bool CanPause { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this media element
        /// currently has an open media url.
        /// </summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the media clock is playing.
        /// </summary>
        public bool IsPlaying => IsOpen && (Parent?.Clock?.IsRunning ?? default);

        /// <summary>
        /// Gets a value indicating whether the media clock is paused.
        /// </summary>
        public bool IsPaused => IsOpen && (Parent?.Clock?.IsRunning ?? true) == false;

        /// <summary>
        /// Gets a value indicating whether the current video stream has closed captions
        /// </summary>
        public bool HasClosedCaptions =>
            Parent.Container?.Components[MediaType.Video]?.StreamInfo?.HasClosedCaptions ?? default;

        /// <summary>
        /// Gets a value indicating whether the media seeking is in progress.
        /// </summary>
        public bool IsSeeking => Parent?.Commands?.IsSeeking ?? false;

        /// <summary>
        /// Gets a value indicating whether the media is in the process of closing media.
        /// </summary>
        public bool IsClosing => Parent?.Commands?.IsClosing ?? false;

        /// <summary>
        /// Gets a value indicating whether the media is in the process of opening.
        /// </summary>
        public bool IsOpening => Parent?.Commands?.IsOpening ?? false;

        /// <summary>
        /// Gets a value indicating whether the media is currently changing its components.
        /// </summary>
        public bool IsChanging => Parent?.Commands?.IsChanging ?? false;

        #endregion

        #region State Method Managed Media Properties

        /// <summary>
        /// Gets the current playback state.
        /// </summary>
        public PlaybackStatus MediaState
        {
            get { lock (SyncLock) return m_MediaState; }
            private set { lock (SyncLock) m_MediaState = value; }
        }

        /// <summary>
        /// Gets or Sets the Position property on the MediaElement.
        /// </summary>
        public TimeSpan Position
        {
            get { lock (SyncLock) return m_Position; }
            private set { lock (SyncLock) m_Position = value; }
        }

        /// <summary>
        /// Gets the discrete timestamp of the next frame.
        /// </summary>
        public TimeSpan PositionNext
        {
            get { lock (SyncLock) return m_PositionNext; }
            private set { lock (SyncLock) m_PositionNext = value; }
        }

        /// <summary>
        /// Gets the discrete timestamp of the current frame.
        /// </summary>
        public TimeSpan PositionCurrent
        {
            get { lock (SyncLock) return m_PositionCurrent; }
            private set { lock (SyncLock) m_PositionCurrent = value; }
        }

        /// <summary>
        /// Gets the discrete timestamp of the previous frame.
        /// </summary>
        public TimeSpan PositionPrevious
        {
            get { lock (SyncLock) return m_PositionPrevious; }
            private set { lock (SyncLock) m_PositionPrevious = value; }
        }

        /// <summary>
        /// Gets a value indicating whether the media has reached its end.
        /// </summary>
        public bool HasMediaEnded
        {
            get { lock (SyncLock) return m_HasMediaEnded; }
            private set { lock (SyncLock) m_HasMediaEnded = value; }
        }

        /// <summary>
        /// Get a value indicating whether the media is buffering.
        /// </summary>
        public bool IsBuffering
        {
            get { lock (SyncLock) return m_IsBuffering; }
            private set { lock (SyncLock) m_IsBuffering = value; }
        }

        /// <summary>
        /// Gets a value that indicates the percentage of buffering progress made.
        /// Range is from 0 to 1
        /// </summary>
        public double BufferingProgress
        {
            get { lock (SyncLock) return m_BufferingProgress; }
            private set { lock (SyncLock) m_BufferingProgress = value; }
        }

        /// <summary>
        /// The packet buffer length.
        /// It is adjusted to 1 second if bitrate information is available.
        /// Otherwise, it's simply 512KB and it is guessed later on.
        /// </summary>
        public ulong BufferCacheLength
        {
            get { lock (SyncLock) return m_BufferCacheLength; }
            private set { lock (SyncLock) m_BufferCacheLength = value; }
        }

        /// <summary>
        /// Gets a value that indicates the percentage of download progress made.
        /// Range is from 0 to 1
        /// </summary>
        public double DownloadProgress
        {
            get { lock (SyncLock) return m_DownloadProgress; }
            private set { lock (SyncLock) m_DownloadProgress = value; }
        }

        /// <summary>
        /// Gets the maximum packet buffer length, according to the bitrate (if available).
        /// If it's a realtime stream it will return 30 times the buffer cache length.
        /// Otherwise, it will return  4 times of the buffer cache length.
        /// </summary>
        public ulong DownloadCacheLength
        {
            get { lock (SyncLock) return m_DownloadCacheLength; }
            private set { lock (SyncLock) m_DownloadCacheLength = value; }
        }

        #endregion

        #region Container Property Management Methods

        /// <summary>
        /// Updates the fixed container properties.
        /// </summary>
        internal void UpdateFixedContainerProperties()
        {
            lock (SyncLock)
            {
                IsOpen = (IsOpening == false) && (Parent.Container?.IsOpen ?? default);
                Metadata = Parent.Container?.Metadata ?? EmptyDictionary;
                MediaFormat = Parent.Container?.MediaFormatName;
                VideoStreamIndex = Parent.Container?.Components[MediaType.Video]?.StreamIndex ?? -1;
                AudioStreamIndex = Parent.Container?.Components[MediaType.Audio]?.StreamIndex ?? -1;
                SubtitleStreamIndex = Parent.Container?.Components[MediaType.Subtitle]?.StreamIndex ?? -1;
                HasAudio = Parent.Container?.Components.HasAudio ?? default;
                HasVideo = Parent.Container?.Components.HasVideo ?? default;
                HasSubtitles =
                    (Parent.PreloadedSubtitles != null && Parent.PreloadedSubtitles.Count > 0) ||
                    (Parent.Container?.Components.HasSubtitles ?? false);
                VideoCodec = Parent.Container?.Components.Video?.CodecName;
                VideoBitrate = Parent.Container?.Components.Video?.Bitrate ?? default;
                VideoRotation = Parent.Container?.Components.Video?.DisplayRotation ?? default;
                NaturalVideoWidth = Parent.Container?.Components.Video?.FrameWidth ?? default;
                NaturalVideoHeight = Parent.Container?.Components.Video?.FrameHeight ?? default;
                VideoFrameRate = Parent.Container?.Components.Video?.BaseFrameRate ?? default;
                VideoFrameLength = VideoFrameRate <= 0 ? default : 1d / VideoFrameRate;
                AudioCodec = Parent.Container?.Components.Audio?.CodecName;
                AudioBitrate = Parent.Container?.Components.Audio?.Bitrate ?? default;
                AudioChannels = Parent.Container?.Components.Audio?.Channels ?? default;
                AudioSampleRate = Parent.Container?.Components.Audio?.SampleRate ?? default;
                AudioBitsPerSample = Parent.Container?.Components.Audio?.BitsPerSample ?? default;
                NaturalDuration = Parent.Container?.MediaDuration;
                IsLiveStream = Parent.Container?.IsLiveStream ?? default;
                IsNetowrkStream = Parent.Container?.IsNetworkStream ?? default;
                IsSeekable = Parent.Container?.IsStreamSeekable ?? default;
                CanPause = IsOpen ? (IsLiveStream == false) : default;
            }
        }

        #endregion

        #region State Management Methods

        /// <summary>
        /// Updates the media ended state and notifies the parent if there is a change from false to true.
        /// </summary>
        /// <param name="hasEnded">if set to <c>true</c> [has ended].</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void UpdateMediaEnded(bool hasEnded)
        {
            if (HasMediaEnded == false && hasEnded == true)
            {
                SignalBufferingEnded();
                HasMediaEnded = true;
                Parent?.SendOnMediaEnded();
                return;
            }

            HasMediaEnded = hasEnded;
        }

        /// <summary>
        /// Updates the position.
        /// </summary>
        /// <param name="newPosition">The position.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void UpdatePosition(TimeSpan newPosition)
        {
            const long Millisecond = TimeSpan.TicksPerMillisecond;

            var oldPosition = Position;
            if (oldPosition.Ticks == newPosition.Ticks)
                return;

            Position = newPosition;

            // TODO: Improve this code - it's not efficient.
            // Update discrete positions
            var t = Parent?.Container?.Components?.Main?.MediaType;
            if (t.HasValue && Parent?.Blocks[t.Value] != null)
            {
                Parent.Blocks[t.Value].GetNeighboringBlocks(newPosition, out var current, out var previous, out var next);
                lock (SyncLock)
                {
                    m_PositionCurrent = current?.StartTime ?? newPosition;
                    if (current == null)
                    {
                        m_PositionNext = next?.StartTime ?? TimeSpan.FromTicks(
                            PositionCurrent.Ticks + FrameStepDuration.Ticks + Millisecond);
                        m_PositionPrevious = previous?.StartTime ?? TimeSpan.FromTicks(
                            PositionCurrent.Ticks - FrameStepDuration.Ticks + Millisecond);
                    }
                    else
                    {
                        m_PositionNext = next?.StartTime ?? TimeSpan.FromTicks(
                            current.EndTime.Ticks + (current.Duration.Ticks / 2));
                        m_PositionPrevious = previous?.StartTime ?? TimeSpan.FromTicks(
                            current.StartTime.Ticks - (current.Duration.Ticks / 2));
                    }
                }
            }

            Parent.SendOnPositionChanged(oldPosition, newPosition);
        }

        /// <summary>
        /// Updates the MediaState property.
        /// </summary>
        /// <param name="mediaState">State of the media.</param>
        /// <param name="position">The new position value for this state.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void UpdateMediaState(PlaybackStatus mediaState, TimeSpan? position = null)
        {
            if (position != null)
                UpdatePosition(position.Value);

            var oldValue = MediaState;
            if (oldValue == mediaState)
                return;

            MediaState = mediaState;
            Parent.SendOnMediaStateChanged(oldValue, mediaState);
        }

        /// <summary>
        /// Resets the controller properies.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ResetMediaProperties()
        {
            var oldMediaState = default(PlaybackStatus);
            var newMediaState = PlaybackStatus.Close;

            // Reset Method-controlled properties
            lock (SyncLock)
            {
                oldMediaState = m_MediaState;

                m_MediaState = newMediaState;
                m_Position = default;
                m_PositionCurrent = default;
                m_PositionNext = default;
                m_PositionPrevious = default;
                m_HasMediaEnded = default;
                m_IsBuffering = default;
                m_BufferingProgress = default;
                m_BufferCacheLength = default;
                m_DownloadProgress = default;
                m_DownloadCacheLength = default;
            }

            VideoSmtpeTimecode = string.Empty;
            VideoHardwareDecoder = string.Empty;

            // Reset volatile controller poperties
            SpeedRatio = Constants.Controller.DefaultSpeedRatio;

            if (oldMediaState != newMediaState)
                Parent.SendOnMediaStateChanged(oldMediaState, newMediaState);
        }

        /// <summary>
        /// Resets all the buffering properties to their defaults.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void InitializeBufferingProperties()
        {
            const int MinimumValidBitrate = 96 * 1000; // 96kbps
            const int StartingCacheLength = 512 * 1024; // Half a megabyte

            lock (SyncLock)
            {
                GuessedByteRate = default;

                if (Parent.Container == null)
                {
                    m_IsBuffering = default;
                    m_BufferCacheLength = default;
                    m_DownloadCacheLength = default;
                    m_BufferingProgress = default;
                    m_DownloadProgress = default;
                    return;
                }

                var allComponentsHaveBitrate = true;

                if (HasAudio && AudioBitrate <= 0)
                    allComponentsHaveBitrate = false;

                if (HasVideo && VideoBitrate <= 0)
                    allComponentsHaveBitrate = false;

                if (HasAudio == false && HasVideo == false)
                    allComponentsHaveBitrate = false;

                // The metadata states that we have bitrates for the components
                // but sometimes (like in certain WMV files) we have slightly incorrect information
                // and therefore, we multiply times 2 just to be safe
                var mediaBitrate = 2d * Math.Max(Parent.Container.MediaBitrate,
                    allComponentsHaveBitrate ? AudioBitrate + VideoBitrate : 0);

                if (mediaBitrate > MinimumValidBitrate)
                {
                    m_BufferCacheLength = Convert.ToUInt64(mediaBitrate / 8d);
                    GuessedByteRate = Convert.ToUInt64(BufferCacheLength);
                }
                else
                {
                    m_BufferCacheLength = StartingCacheLength;
                }

                m_DownloadCacheLength = m_BufferCacheLength * (IsNetowrkStream ? NetworkStreamCacheFactor : StandardStreamCacheFactor);
                m_IsBuffering = false;
                m_BufferingProgress = 0;
                m_DownloadProgress = 0;
            }
        }

        /// <summary>
        /// Signals the buffering started.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SignalBufferingStarted()
        {
            lock (SyncLock)
            {
                if (m_IsBuffering) return;
                else m_IsBuffering = true;
            }

            Parent?.SendOnBufferingStarted();
        }

        /// <summary>
        /// Signals the buffering ended.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SignalBufferingEnded()
        {
            lock (SyncLock)
            {
                if (m_IsBuffering == false) return;
                else m_IsBuffering = false;
            }

            Parent?.SendOnBufferingEnded();
        }

        /// <summary>
        /// Updates the buffering properties: IsBuffering, BufferingProgress, DownloadProgress.
        /// </summary>
        /// <param name="packetBufferLength">Length of the packet buffer.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void UpdateBufferingProgress(double packetBufferLength)
        {
            bool wasBuffering = default;

            lock (SyncLock)
            {
                // Capture the current state
                wasBuffering = m_IsBuffering;

                // Update the buffering progress
                m_BufferingProgress = m_BufferCacheLength != 0 ? Math.Min(
                    1d, Math.Round(packetBufferLength / m_BufferCacheLength, 3)) : 0;

                // Update the download progress
                m_DownloadProgress = m_DownloadCacheLength != 0 ? Math.Min(
                    1d, Math.Round(packetBufferLength / m_DownloadCacheLength, 3)) : 0;
            }

            // Compute the new state
            IsBuffering = packetBufferLength < BufferCacheLength
                && (Parent?.CanReadMorePackets ?? false);

            // Notify the change
            if (wasBuffering && IsBuffering == false)
                Parent?.SendOnBufferingEnded();
        }

        /// <summary>
        /// Guesses the bitrate of the input stream.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void GuessBufferingProperties()
        {
            if (GuessedByteRate != null || Parent.Container == null || Parent.Container.Components == null)
                return;

            // Capture the read bytes of a 1-second buffer
            var bytesReadSoFar = Parent.Container.Components.LifetimeBytesRead;
            var shortestDuration = TimeSpan.MaxValue;
            var currentDuration = TimeSpan.Zero;

            foreach (var t in Parent.Container.Components.MediaTypes)
            {
                if (t != MediaType.Audio && t != MediaType.Video)
                    continue;

                currentDuration = Parent.Blocks[t].LifetimeBlockDuration;

                if (currentDuration.TotalSeconds < 1)
                {
                    shortestDuration = TimeSpan.Zero;
                    break;
                }

                if (currentDuration < shortestDuration)
                    shortestDuration = currentDuration;
            }

            if (shortestDuration.TotalSeconds >= 1 && shortestDuration != TimeSpan.MaxValue)
            {
                // We make the byterate 20% larget than what we have received, just to be safe.
                GuessedByteRate = (ulong)(1.2 * bytesReadSoFar / shortestDuration.TotalSeconds);
                BufferCacheLength = Convert.ToUInt64(GuessedByteRate);
                DownloadCacheLength = BufferCacheLength * (IsNetowrkStream ? NetworkStreamCacheFactor : StandardStreamCacheFactor);
            }
        }

        #endregion
    }
}
