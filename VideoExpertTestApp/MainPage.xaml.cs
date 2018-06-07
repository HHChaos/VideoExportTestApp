using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using VideoExpertTestApp.Annotations;

// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了“空白页”项模板

namespace VideoExpertTestApp
{
    public class VideoQuality
    {
        public VideoEncodingQuality Quality { get; set; }
        public string Label { get; set; }
    }


    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        public MainPage()
        {
            this.InitializeComponent();
        }
        private double _progress;

        public double Progress
        {
            get { return _progress; }
            set
            {
                _progress = value;
                OnPropertyChanged(nameof(Progress));
            }
        }
        private double _width=1280;

        public double Width
        {
            get { return _width; }
            set
            {
                _width = value;
                OnPropertyChanged(nameof(Width));
            }
        }
        private double _height=720;

        public double Height
        {
            get { return _height; }
            set
            {
                _height = value;
                OnPropertyChanged(nameof(Height));
            }
        }
        private StorageFolder _exportFolder;
        public StorageFolder ExportFolder
        {
            get { return _exportFolder; }
            set
            {
                if (_exportFolder != value)
                {
                    _exportFolder = value;
                    OnPropertyChanged(nameof(ExportFolder));
                }
            }
        }
        private string _exportFileName;
        public string ExportFileName
        {
            get { return _exportFileName; }
            set
            {
                if (_exportFileName != value)
                {
                    _exportFileName = value;
                    OnPropertyChanged(nameof(ExportFileName));
                }
            }
        }
        private async void FolderPiker_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                ExportFolder = folder;
            }
        }
        private VideoExportTask _task;
        private async void ExportButton_OnClick(object sender, RoutedEventArgs e)
        {
            var folder=await Windows.ApplicationModel.Package.Current.InstalledLocation.GetFolderAsync(@"Assets");
            var musicFile = await folder.GetFileAsync(@"萧煌奇 - 末班车.mp3");
            var backgroundTrack = await BackgroundAudioTrack.CreateFromFileAsync(musicFile);

            _task = new VideoExportTask(ExportFolder, ExportFileName, new Size(_width, _height));
            _task.BackgroundAudioTracks.Add(backgroundTrack);
            _task.ExportProgressChanged += Task_ExportProgressChanged;
            _task.Start();
        }

        private void Task_ExportProgressChanged(object sender, ExportProgressEventArgs e)
        {
            this.Progress=e.Progress;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            _task?.Cancel();
        }
    }
}
