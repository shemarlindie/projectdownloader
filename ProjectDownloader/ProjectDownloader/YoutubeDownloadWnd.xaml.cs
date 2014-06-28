using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using ProjectDownloader.YouTube;
using ProjectDownloader.Core;
using System.Windows.Media;
using System.Threading;
using System.Net;

/* TODO:
 * Show video info even if it cannot be downloaded
 * link to channel and video
 * 
 */ 


namespace ProjectDownloader {
    /// <summary>
    /// Interaction logic for YoutubeDownloadWindow.xaml
    /// </summary>
    public partial class YoutubeDownloadWnd : Window {
        ProjectDownloaderWnd downloadWindow;
        YouTubeVideo ytVideo;


        public YoutubeDownloadWnd() {
            InitializeComponent();
        }

        public YoutubeDownloadWnd(string watchUrl, ProjectDownloaderWnd owner)
            : this() {
            Owner = owner;
            downloadWindow = owner as ProjectDownloaderWnd;

            txtVideoUrl.Text = watchUrl;
            Loaded += btnRefresh_Click;
        }

        private void cbxFormat_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (cbxFormat.SelectedIndex != -1) {
                cbxResolution.Items.Clear();
                foreach (string res in GetFormatQualities(ytVideo, cbxFormat.SelectedValue as string)) {
                    cbxResolution.Items.Add(res);
                }
                cbxResolution.SelectedIndex = 0;
            }
        }

        private void cbxResolution_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (cbxResolution.SelectedIndex != -1) {
                RetrieveVideoSize(ytVideo);
            }
        }

        private async void btnDownload_Click(object sender, RoutedEventArgs e) {
            string saveTo = null;
            YouTubeVideo.DownloadInfo info = GetDownloadInfo(ytVideo,
                cbxFormat.SelectedValue as string, cbxResolution.SelectedValue as string);

            if (info == null) {
                ClearUIText();
                txtblTitle.Text = "Oops! Download information is unavailable. Refresh and try again...";
                return;
            }

            UIEnabled(false);
            DownloadTask dl = null;

            await Task.Run(() => {
                dl = new DownloadTask(info.DownloadUrl) {
                    FileName = ytVideo.Title + "." + info.Format
                }.Initialize();
            });

            downloadWindow.DlQueue.Add(dl);

            if (downloadWindow.chkStartNow.IsChecked.HasValue
                && downloadWindow.chkStartNow.IsChecked.Value) {
                saveTo = getSaveLocation(ytVideo);
                if (saveTo != null) {
                    dl.DownloadAsync(saveTo);
                    this.Close();
                }
                UIEnabled(true);
            }
            else {
                this.Close();
            }
        }

        private async void btnRefresh_Click(object sender, RoutedEventArgs e) {
            try {
                await RetrieveVideoDataAsync(txtVideoUrl.Text);
            }
            catch (DownloadUnavailableException) {
                txtblTitle.Text = "This video cannot be downloaded.";
                UIEnabled(true, false);
            }
            catch (Exception ex) {
                MessageBox.Show(ex.Message, "Oops! Something went wrong.");
            }
        }



        private async Task RetrieveVideoDataAsync(string watchUrl) {
            UIEnabled(false);
            ClearUIText();
            txtblTitle.Text = "Retrieving video data...";

            pbGettingData.Visibility = System.Windows.Visibility.Visible;
            MemoryStream imgStream = null;

            Task<YouTubeVideo> task = Task.Run(() => {
                YouTubeVideo video = new YouTubeVideo(watchUrl);
                
                byte[] imgData = null;
                using (var wc = new WebClient()) {
                    try {
                        imgData = wc.DownloadData(video.ThumbnailUrl);
                    }
                    catch { }
                }

                if (imgData != null) {
                    imgStream = new MemoryStream(imgData);
                }

                return video;
            });

            task.ConfigureAwait(true).GetAwaiter().OnCompleted(() => {
                if (!task.IsFaulted) {
                    ytVideo = task.Result;

                    BitmapImage thumbnail = null;
                    if (imgStream != null) {
                        thumbnail = new BitmapImage();
                        thumbnail.BeginInit();
                        thumbnail.StreamSource = imgStream;
                        thumbnail.EndInit();
                    }

                    UpdateUI(ytVideo, thumbnail);
                    UIEnabled(true);
                }

                pbGettingData.Visibility = System.Windows.Visibility.Hidden;
            });

            ytVideo = await task;
        }

        private async void RetrieveVideoSize(YouTubeVideo ytv) {
            cbxFormat.IsEnabled = false;
            cbxResolution.IsEnabled = false;
            lblVideoSize.Content = "Getting size...";
            try {
                YouTubeVideo.DownloadInfo dlInfo = GetDownloadInfo(ytv,
                    cbxFormat.SelectedValue as string, cbxResolution.SelectedValue as string);
                FileSize sz = await YouTubeVideo.GetVideoSizeAsync(dlInfo);
                lblVideoSize.Content = sz.ToString();
            }
            catch {
                lblVideoSize.Content = "Unable to get size.";
            }
            finally {
                cbxFormat.IsEnabled = true;
                cbxResolution.IsEnabled = true;
            }
        }

        private void UpdateUI(YouTubeVideo ytv, BitmapImage thumbnail) {
            cbxResolution.Items.Clear();
            cbxFormat.Items.Clear();

            if (thumbnail != null) {
                imgThumbnail.Source = thumbnail;
            }

            txtblTitle.Text = ytv.Title;
            if (!string.IsNullOrEmpty(ytv.Channel)) {
                txtblChannel.Text = ytv.Channel;
            }
            else {
                txtblChannel.Text = " - ";
            }

            string length;
            if (ytv.Length.Hours > 0) {
                length = ytv.Length.ToString("c");
            }
            else {
                length = ytv.Length.ToString("mm\\:ss");
            }
            lblLength.Content = length;

            foreach (var dlInfo in ytv.AvailableDownloads) {
                if (!cbxFormat.Items.Contains(dlInfo.Format)) {
                    cbxFormat.Items.Add(dlInfo.Format);
                }
            }

            cbxFormat.SelectedIndex = 0;
        }

        private IEnumerable<string> GetFormatQualities(YouTubeVideo ytv, string format) {
            List<string> formatResList = new List<string>();

            foreach (var dlInfo in ytv.AvailableDownloads) {
                if (dlInfo.Format == format) {
                    formatResList.Add(dlInfo.Resolution.Height.ToString());
                }
            }

            return formatResList;
        }

        private YouTubeVideo.DownloadInfo GetDownloadInfo(YouTubeVideo ytv, string format, string quality) {
            YouTubeVideo.DownloadInfo dl = null;
            foreach (var dlInfo in ytv.AvailableDownloads) {
                if (dlInfo.Resolution.Height.ToString() == quality
                    && dlInfo.Format == format) {
                    dl = dlInfo;
                }
            }

            return dl;
        }

        private string getSaveLocation(YouTubeVideo ytv) {
            string path = null;
            var dialog = new Microsoft.Win32.SaveFileDialog();
            dialog.RestoreDirectory = true;
            dialog.Title = "Save as...";
            dialog.DefaultExt = cbxFormat.SelectedValue as string;
            dialog.Filter = string.Format("{0} files|*.{1}|All files|*.*",
                dialog.DefaultExt.ToUpper(), dialog.DefaultExt);
            dialog.FileName = ytv.Title;

            bool? ok = dialog.ShowDialog();

            if (ok.HasValue && ok.Value) {
                path = dialog.FileName;
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            }

            return path;
        }

        private void UIEnabled(bool state, bool videoAvailable = true) {
            txtVideoUrl.IsEnabled = state;
            btnRefresh.IsEnabled = state;

            if (videoAvailable) {
                btnDownload.IsEnabled = state;
                btnCopyLink.IsEnabled = state;
                cbxFormat.IsEnabled = state;
                cbxResolution.IsEnabled = state;
            }
        }

        private void ClearUIText() {
            txtblTitle.Text = "";
            txtblChannel.Text = "";
            lblLength.Content = "";
            lblVideoSize.Content = "";
        }

        private void btnCopyLink_Click(object sender, RoutedEventArgs e) {
            YouTubeVideo.DownloadInfo dl = GetDownloadInfo(ytVideo, cbxFormat.SelectedValue as string,
                cbxResolution.SelectedValue as string);

            if (dl == null) {
                return;
            }

            string link = dl.DownloadUrl;
            Clipboard.SetText(link);
            MessageBox.Show("Copied download link to the clipboard. You can paste it anywhere.", "Copied link",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
