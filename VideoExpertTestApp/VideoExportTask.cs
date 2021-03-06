﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.UI;
using Microsoft.Graphics.Canvas;

namespace VideoExpertTestApp
{
    public class ExportProgressEventArgs : EventArgs
    {
        public int Progress { get; set; }

        public ExportProgressEventArgs(int progress)
        {
            Progress = progress;
        }
    }

    public class VideoExportTask
    {
        public event EventHandler<ExportProgressEventArgs> ExportProgressChanged;
        public event EventHandler<EventArgs> ExportComplated;
        public event EventHandler<EventArgs> ExportFailed;
        private readonly SynchronizationContext _context;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Task _exportTask;
        public StorageFolder DestinationFolder { get; }
        public string FileName { get; }
        public int Progress { get; private set; }
        public Size ExportSize { get; private set; }
        public IList<BackgroundAudioTrack> BackgroundAudioTracks { get; } = new List<BackgroundAudioTrack>();
        public bool Canceled => _cancellationTokenSource.IsCancellationRequested;


        public VideoExportTask(StorageFolder destinationFolder, string fileName, Size exportSize)
        {
            DestinationFolder = destinationFolder;
            FileName = fileName;
            ExportSize = exportSize;
            _context = SynchronizationContext.Current;
            _cancellationTokenSource = new CancellationTokenSource();
            var obj = new Tuple<StorageFolder, object, string, Size>(DestinationFolder, null, fileName,
                ExportSize);
            _exportTask = CreateExportTask(obj, _cancellationTokenSource.Token);
            _cancellationTokenSource.Token.Register(ClearCacheAsync);
        }

        private async Task<StorageFolder> GetCacheFolder()
        {
            return await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("ExportVideoCache",
                CreationCollisionOption.OpenIfExists);
        }

        private async void ClearCacheAsync()
        {
            (await GetCacheFolder())?.DeleteAsync();
        }

        private void UpdateProgress(int progress)
        {
            _context.Post(_ =>
            {
                Progress = progress;
                ExportProgressChanged?.Invoke(this, new ExportProgressEventArgs(progress));
            }, null);
        }

        private void UpdateExportStatus(bool succeed)
        {
            _context.Post(_ =>
            {
                if (succeed)
                {
                    ExportComplated?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    ExportFailed?.Invoke(this, EventArgs.Empty);
                }
            }, null);
        }

        private Task CreateExportTask(object obj, CancellationToken cancellationToken)
        {
            return new Task(() =>
            {
                var para = obj as Tuple<StorageFolder, object, string, Size>;
                var folder = para?.Item1;
                var player = para?.Item2;
                var fileName = para?.Item3;
                var mediaEncodingProfile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
                if (para == null ||
                    folder == null ||
                    //player == null ||
                    string.IsNullOrEmpty(fileName) ||
                    Math.Abs(para.Item4.Width) < 1 ||
                    Math.Abs(para.Item4.Height) < 1 ||
                    mediaEncodingProfile?.Video == null)
                {
                    UpdateExportStatus(false);
                    return;
                }
                var exportSize = para.Item4;
                mediaEncodingProfile.Video.Width = (uint) exportSize.Width;
                mediaEncodingProfile.Video.Height = (uint) exportSize.Height;
                var cacheFolder = GetCacheFolder().GetAwaiter().GetResult();
                var device = CanvasDevice.GetSharedDevice();
                long total = 10000;
                int fps = 25;
                var mediafileList = new List<StorageFile>();
                var tmpFrameImgs = new List<CanvasBitmap>();
                var layerTmp = new MediaOverlayLayer();
                int start = 0;
                int loopGap = 1000 / fps;
                var span = TimeSpan.FromMilliseconds(loopGap);
                var backgroud =
                    new CanvasRenderTarget(device, (float) exportSize.Width, (float) exportSize.Height, 96f);
                using (var session = backgroud.CreateDrawingSession())
                {
                    session.Clear(Colors.White);
                }

                for (int s = 0; s <= total; s += loopGap)
                {
                    if (Canceled) return;
                    var frame = new CanvasRenderTarget(device, (float) exportSize.Width, (float) exportSize.Height,
                        96f);
                    using (var session = frame.CreateDrawingSession())
                    {
                        session.DrawText($"frame:{s / 100}", new System.Numerics.Vector2(100, 100), Colors.Red);
                    }
                    tmpFrameImgs.Add(frame);
                    var clip = MediaClip.CreateFromSurface(frame, span);
                    layerTmp.Overlays.Add(CreateMediaOverlay(clip, exportSize, s - start));
                    if (s - start >= 2000 || total - s < loopGap)
                    {
                        var progress = (int) (s / (float) total * 100 * 0.5);
                        UpdateProgress(progress);
                        var composition = new MediaComposition();
                        composition.Clips.Add(MediaClip.CreateFromSurface(backgroud,
                            TimeSpan.FromMilliseconds(s - start + loopGap)));
                        composition.OverlayLayers.Add(layerTmp);
                        var mediaPartFile = cacheFolder.CreateFileAsync(
                                $"part_{mediafileList.Count}.mp4", CreationCollisionOption.ReplaceExisting).GetAwaiter()
                            .GetResult();
                        composition.RenderToFileAsync(mediaPartFile, MediaTrimmingPreference.Fast,
                            mediaEncodingProfile).GetAwaiter().GetResult();
                        mediafileList.Add(mediaPartFile);
                        layerTmp = new MediaOverlayLayer();
                        foreach (var item in tmpFrameImgs)
                        {
                            item.Dispose();
                        }
                        tmpFrameImgs.Clear();
                        start = s + loopGap;
                    }
                }
                var mediaComposition = new MediaComposition();
                foreach (var mediaPartFile in mediafileList)
                {
                    if (Canceled) return;
                    var mediaPartClip = MediaClip.CreateFromFileAsync(mediaPartFile).GetAwaiter().GetResult();
                    mediaComposition.Clips.Add(mediaPartClip);
                }
                lock (BackgroundAudioTracks)
                {
                    foreach (var bgm in BackgroundAudioTracks)
                    {
                        mediaComposition.BackgroundAudioTracks.Add(bgm);
                    }
                }
                var exportFile = folder.CreateFileAsync($"{fileName}.mp4", CreationCollisionOption.ReplaceExisting)
                    .GetAwaiter().GetResult();
                if (Canceled) return;
                var saveOperation = mediaComposition.RenderToFileAsync(exportFile, MediaTrimmingPreference.Fast,
                    mediaEncodingProfile);
                saveOperation.Progress = (info, progress) =>
                {
                    UpdateProgress((int) (50 + progress * 0.5));
                    if (Canceled)
                    {
                        saveOperation.Cancel();
                    }
                };

                saveOperation.Completed = (info, status) =>
                {
                    if (!Canceled)
                    {
                        var results = info.GetResults();
                        if (results != TranscodeFailureReason.None || status != AsyncStatus.Completed)
                        {
                            UpdateExportStatus(false);
                        }
                        else
                        {
                            UpdateExportStatus(true);
                        }
                    }
                    ClearCacheAsync();
                };
            }, cancellationToken);
        }

        private static MediaOverlay CreateMediaOverlay(MediaClip clip, Size size, double start)
        {
            return new MediaOverlay(clip)
            {
                Position = new Rect
                {
                    Width = size.Width,
                    Height = size.Height
                },
                Opacity = 1f,
                Delay = TimeSpan.FromMilliseconds(start)
            };
        }

        public void Start()
        {
            if (Canceled)
                return;
            _exportTask?.Start();
        }

        public void Cancel()
        {
            if (Canceled)
                return;
            if (_cancellationTokenSource.Token.CanBeCanceled)
            {
                _cancellationTokenSource.Cancel(true);
            }
        }
    }
}
