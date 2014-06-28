using ProjectDownloader.Core;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Windows;


/* TODO:
 * Select all text on url box focus
 * check out renaming .part file while it is use at end of d/l
 * 
 */ 


namespace ProjectDownloader {
    /// <summary>
    /// Interaction logic for ProjectDownloaderWnd.xaml
    /// </summary>
    public partial class ProjectDownloaderWnd : Window {
        private ObservableCollection<DownloadTask> _dlQueue = new ObservableCollection<DownloadTask>();

        public ProjectDownloaderWnd() {
            InitializeComponent();
            txtUrl.Focus();
        }

        public ObservableCollection<DownloadTask> DlQueue { get { return _dlQueue; } }



        private void btnAdd_Click(object sender, RoutedEventArgs e) {
            string url = txtUrl.Text;
            if (Util.IsYoutubeVideoUrl(url)) {
                new YoutubeDownloadWnd(url, this).ShowDialog();
            }
            else {
                try {
                    DownloadTask dl = new DownloadTask(url).Initialize();
                    DlQueue.Add(dl);

                    if (chkStartNow.IsChecked.HasValue && chkStartNow.IsChecked.Value) {
                        string path = getSaveLocation(dl);

                        if (path != null) {
                            dl.DownloadAsync(path);
                        }
                    }
                }
                catch (Exception ex) {
                    MessageBox.Show(ex.Message, "Oops! An error occured.");
                }
            }
        }

        private void btnPauseResume_Click(object sender, RoutedEventArgs e) {
            foreach (DownloadTask dl in lstDlQueue.SelectedItems) {
                if (dl.Status == DownloadTask.DownloadTaskStatus.Downloading) {
                    dl.Pause();
                }
                else {
                    dl.ResumeAsync();
                }
            }
        }

        private void btnClear_Click(object sender, RoutedEventArgs e) {
            foreach (DownloadTask dl in DlQueue) {
                dl.Cancel();
            }
            DlQueue.Clear();
        }

        private void btnRemove_Click(object sender, RoutedEventArgs e) {
            while (lstDlQueue.SelectedItems.Count > 0) {
                DownloadTask dl = lstDlQueue.SelectedItems[0] as DownloadTask;
                dl.Cancel();
                DlQueue.Remove(dl);
            }
        }

        private void btnStart_Click(object sender, RoutedEventArgs e) {
            foreach (DownloadTask dl in lstDlQueue.SelectedItems) {
                if (dl.Status == DownloadTask.DownloadTaskStatus.Ready) {
                    string path = getSaveLocation(dl);
                    if (path != null) {
                        dl.DownloadAsync(path);
                    }
                }
                else {
                    dl.ResumeAsync();
                }
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e) {
            foreach (DownloadTask dl in lstDlQueue.SelectedItems) {
                dl.Cancel();
            }
        }



        private string getSaveLocation(DownloadTask dl) {
            string path = null;
            var dialog = new Microsoft.Win32.SaveFileDialog();
            dialog.RestoreDirectory = true;
            dialog.Title = "Save as...";
            dialog.DefaultExt = dl.Extension;
            dialog.Filter = string.Format("{0} files|*{1}|All files|*.*",
                dl.Extension.Replace(".", "").ToUpper(), dl.Extension);
            dialog.FileName = dl.FileName;

            bool? ok = dialog.ShowDialog();

            if (ok.HasValue && ok.Value) {
                path = dialog.FileName;
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            }

            return path;
        }



        private void txtUrl_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) {
            if (txtUrl.Text.Length > 0) {
                txtUrl.SelectAll();
            }
        }

        private void txtUrl_GotMouseCapture(object sender, System.Windows.Input.MouseEventArgs e) {
            if (txtUrl.Text.Length > 0) {
                txtUrl.SelectAll();
            }
        }
    }
}
