using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

/* TODO:
 * Add timeout for async operations (cancel, etc.)
 * 
 */

namespace ProjectDownloader.Core {
    /// <summary>
    /// Encapsulates a download job with pause/resume, cancellation and progress reporting support.
    /// </summary>
    public class DownloadTask : INotifyPropertyChanged, IDisposable {
        public static int DefaultBufferSize = 1024 * 8; // 8 KB
        public static string IncompleteDlExt = ".part"; // extension of downloading file

        private Stopwatch downloadWatch; // measures the total elapsed time of the download
        private System.Timers.Timer elapsedTimer; // raises PropertyChanged event for Elaped property every second while download is in progress
        private IProgress<DownloadProgress> progressReporter;
        private CancellationTokenSource cancelSource;
        private bool isDisposed;

        // BACKING FIELDS
        private DownloadTaskStatus _status;
        private DownloadProgress _progress;
        private string _saveTo; // where the file is saved when completed
        private string _saveToIncomlete; // temporary download file
        private string _url, _fileName, _extension;
        private FileSize _size;
        private bool _isInitialized, _resumeSupported, _deleteWhenCanceled;
        private int _bufferSize;

        /// <summary>
        /// Instantiates a new instance of the DownloadTask class
        /// </summary>
        /// <param name="url">The url to the file to download.</param>
        /// <exception cref="System.ArgumentException">An invalid HTTP url was passed into the constructor.</exception>
        public DownloadTask(string url) {
            Uri u = new Uri(url);
            if (!u.IsAbsoluteUri && !(u.Scheme == "http")) { // validate url
                throw new ArgumentException("Only downloads over HTTP are supported.", "url");
            }

            BufferSize = DefaultBufferSize;
            downloadWatch = new Stopwatch();
            _url = url;
            _status = DownloadTaskStatus.NotStarted;
            _deleteWhenCanceled = true;

            // notify change in elapsed time every second (timer started when download starts)
            elapsedTimer = new System.Timers.Timer(1000);
            elapsedTimer.Elapsed += (s, args) => {
                RaisePropertyChanged("Elapsed");
            };
        }


        /// <summary>
        /// Initializes the download; getting the file's size, resume support etc.
        /// </summary>
        /// <exception cref="System.NotSupportedException">
        /// Invalid URI scheme.
        /// </exception>
        /// <exception cref="System.Security.SecurityException">
        /// Permission is not granted to connect to remote server.
        /// </exception>
        /// <exception cref="System.UriFormatException">The download URL is invalid.</exception>
        /// <exception cref="System.Net.WebException">An error occurred while processing the request.</exception>
        public DownloadTask Initialize() {
            Status = DownloadTaskStatus.Initializing;
            int bytesToSkip = 1; // used to check resume support
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(_url);
            req.AddRange(bytesToSkip);

            using (HttpWebResponse res = (HttpWebResponse)req.GetResponse()) {
                ResumeSupported = res.StatusCode == HttpStatusCode.PartialContent;

                if (string.IsNullOrEmpty(FileName)) { // file name has not been set
                    // try to get filename from server
                    if (res.Headers["Content-Disposition"] != null) {
                        var match = System.Text.RegularExpressions.Regex.Match(res.Headers["Content-Disposition"], 
                            "filename ?= ?(\"?)([^\"]+)");
                        FileName = match.Groups[2].ToString();
                    }

                    if (string.IsNullOrEmpty(FileName)) { // otherwise, get it from the url
                        string urlString = res.ResponseUri.GetComponents(UriComponents.Path, UriFormat.Unescaped);
                        string name = Path.GetFileName(urlString);

                        if (name.Length < 1) { // uri path component not present
                            name = res.ResponseUri.GetComponents(UriComponents.Host, UriFormat.Unescaped); // use host name
                        }

                        FileName = name;
                    }
                }

                if (res.ContentType.Contains("text/html") && string.IsNullOrEmpty(Extension)) { // is it a webpage without an extension?
                    FileName += ".html";
                }

                if (ResumeSupported) { // can i get part of the file
                    this.Size = new FileSize(res.ContentLength + bytesToSkip); // add back skipped bytes to get total size
                }
                else { // i'll get the entire thing otherwise
                    this.Size = new FileSize(res.ContentLength);
                }
            }

            Progress = new DownloadProgress(0, new FileSize(0), Size, new DownloadSpeed(), new TimeSpan(-1), TimeSpan.Zero);
            Status = DownloadTaskStatus.Ready;
            IsInitialized = true;

            return this;
        }

        /// <summary>
        /// Starts downloading the file.
        /// </summary>
        /// <param name="saveLocation">Path to where the file will be saved.</param>
        /// <param name="onProgressChanged">Interface to track download progress.</param>
        /// <returns>A System.Threading.Task that represents the download operation.</returns>
        /// <exception cref="System.NotSupportedException">
        /// Invalid URI scheme.
        /// </exception>
        /// <exception cref="System.Security.SecurityException">
        /// Permission is not granted to connect to remote server.
        /// </exception>
        /// <exception cref="System.UriFormatException">The download URL is invalid.</exception>
        /// <exception cref="System.Net.WebException">An error occurred while processing the request.</exception>
        /// <exception cref="System.ArgumentException">
        /// path is an empty string (""), contains only white space, or contains one
        /// or more invalid characters.
        /// </exception>
        /// <exception cref="System.IO.IOException">
        /// An I/O error, such as specifying FileMode.CreateNew when the file specified
        /// by path already exists, occurred. -or-The stream has been closed.
        /// </exception>
        /// <exception cref="System.Security.SecurityException">
        /// The caller does not have the required permission.
        /// </exception>
        /// <exception cref="System.IO.DirectoryNotFoundException">
        /// The specified path is invalid, such as being on an unmapped drive.
        /// </exception>
        /// <exception cref="System.UnauthorizedAccessException">
        /// The access requested is not permitted by the operating system for the specified
        /// path, such as when access is Write or ReadWrite and the file or directory
        /// is set for read-only access.
        /// </exception>
        /// <exception cref="System.IO.PathTooLongException">
        /// The specified path, file name, or both exceed the system-defined maximum
        /// length. For example, on Windows-based platforms, paths must be less than
        /// 248 characters, and file names must be less than 260 characters.
        /// </exception>
        public Task DownloadAsync(string saveLocation, IProgress<DownloadProgress> onProgressChanged = null) {
            if (!IsInitialized) {
                throw new InvalidOperationException("The object has not been initialized. You must call DownloadTask.Initialize() first.");
            }

            if (isDisposed) {
                throw new ObjectDisposedException("DownloadTask", "Cannot start because this instance has been disposed.");
            }

            if (Status == DownloadTaskStatus.Failed || Status == DownloadTaskStatus.Canceled) {
                downloadWatch.Reset();
            }

            Status = DownloadTaskStatus.Starting;

            downloadWatch.Start();
            elapsedTimer.Start();

            FullPath = saveLocation; // filename after download
            FileName = Path.GetFileName(FullPath);

            _saveToIncomlete = FullPath + IncompleteDlExt; // save here temporarily (until download completes)            
            progressReporter = onProgressChanged; // save so that it can be used for resumption
            cancelSource = new CancellationTokenSource(); // disposed after paused, canceled, completed. a new one is use when resuming

            return Task.Factory.StartNew(() => {
                HttpWebResponse response = null;
                FileStream fStream = null;

                try {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(_url);
                    request.UseDefaultCredentials = true;
                    request.Proxy = WebRequest.GetSystemWebProxy();

                    byte[] buffer = new byte[BufferSize];
                    FileMode mode;
                    long bytesDownloaded = 0;

                    if (File.Exists(_saveToIncomlete) && ResumeSupported) { // dl has been started before; try to resume
                        FileInfo fInfo = new FileInfo(_saveToIncomlete);

                        if (fInfo.Length < this.Size.Value) { // incomplete file must be smaller than total download size
                            // add range header to resume where left off
                            request.AddRange(fInfo.Length);
                            bytesDownloaded = fInfo.Length;
                            mode = FileMode.Append;
                        }
                        else { // otherwise, it's another file with the same name or it got currupted
                            File.Delete(_saveToIncomlete);
                            mode = FileMode.Create;
                        }
                    }
                    else { // resume not supported; start from beginning
                        mode = FileMode.Create;
                    }

                    // begin download
                    fStream = new FileStream(_saveToIncomlete, mode, FileAccess.Write);
                    response = (HttpWebResponse)request.GetResponse();

                    using (Stream dlStream = response.GetResponseStream()) { // download stream
                        // for measuring speed
                        const int reportInterval = 200; // ms
                        int tmpBytes = 0;

                        Stopwatch watch = new Stopwatch();
                        Func<DownloadProgress> ProgressSnapshot = () => {
                            double percent;
                            TimeSpan eta;
                            FileSize totalSize;
                            FileSize curSize = new FileSize(bytesDownloaded);
                            DownloadSpeed speed;

                            if (Status == DownloadTaskStatus.Completed) {
                                speed = new DownloadSpeed(Size.Value, Elapsed);
                            }
                            else {
                                speed = new DownloadSpeed(tmpBytes, watch.Elapsed); // current speed
                            }

                            if (Size.Value > 0) { // got file size from server
                                totalSize = new FileSize(this.Size.Value);
                                percent = (double)bytesDownloaded / Size.Value * 100;
                                eta = TimeSpan.FromSeconds((this.Size.Value - bytesDownloaded) / (bytesDownloaded / Elapsed.TotalSeconds)); // eta from average speed
                            }
                            else { // size is not known (happens with webpages)
                                totalSize = new FileSize(bytesDownloaded); // use number of bytes downloaded so far as size
                                percent = -1;
                                eta = TimeSpan.FromSeconds(-1);
                            }

                            return new DownloadProgress(percent, curSize, totalSize, speed, eta, Elapsed);
                        };

                        while (true) {
                            Status = DownloadTaskStatus.Downloading;

                            watch.Start();
                            int bytesRead = dlStream.Read(buffer, 0, buffer.Length);
                            watch.Stop();

                            if (watch.ElapsedMilliseconds >= reportInterval) {
                                lock (Progress) {
                                    Progress.Update(ProgressSnapshot());
                                }
                                if (onProgressChanged != null) {
                                    onProgressChanged.Report(Progress);
                                }

                                tmpBytes = 0;
                                watch.Reset();
                            }

                            bytesDownloaded += bytesRead; // total downloaded so far
                            tmpBytes += bytesRead; // total since last progress report

                            if (bytesRead < 1) { // dl completed or failed
                                if (fStream.Length < Size.Value) { // downloaded less than required
                                    Status = DownloadTaskStatus.Failed;
                                }
                                else {
                                    Status = DownloadTaskStatus.Completed;
                                }
                                lock (Progress) {
                                    Progress.Update(ProgressSnapshot());
                                }
                                if (onProgressChanged != null) {
                                    onProgressChanged.Report(Progress);
                                }

                                break;
                            }

                            // write block to file
                            fStream.Write(buffer, 0, bytesRead);
                            fStream.Flush();

                            try { // check for cancellation (pause or cancel)
                                cancelSource.Token.ThrowIfCancellationRequested();
                            }
                            catch (OperationCanceledException) {
                                if (Status == DownloadTaskStatus.Pausing) {
                                    Status = DownloadTaskStatus.Paused;
                                }
                                else {
                                    Status = DownloadTaskStatus.Canceled;
                                }

                                throw;
                            }
                        }
                    }
                }
                catch (Exception ex) {
                    if (!(ex is OperationCanceledException)) { // something went wrong
                        Status = DownloadTaskStatus.Failed;
                    }
                    
                    throw;
                }
                finally {
                    downloadWatch.Stop();
                    elapsedTimer.Stop();

                    if (response != null) {
                        response.Dispose();
                    }

                    if (fStream != null) {
                        fStream.Dispose();
                    }

                    if (cancelSource != null) {
                        cancelSource.Dispose();
                        cancelSource = null;
                    }

                    if (Status == DownloadTaskStatus.Completed) {
                        File.Move(_saveToIncomlete, _saveTo); // rename temporary file
                    }
                    else if (Status == DownloadTaskStatus.Canceled) {
                        if (DeleteWhenCanceled) {
                            File.Delete(_saveToIncomlete);
                        }
                    }

                }
            }, TaskCreationOptions.LongRunning);
        }

        /// <summary>
        /// Pauses an in progress download.
        /// </summary>
        public void Pause() {
            if (Status == DownloadTaskStatus.Downloading) {
                Status = DownloadTaskStatus.Pausing;
                cancelSource.Cancel();
            }
        }

        /// <summary>
        /// Resumes a paused download.
        /// </summary>
        /// <returns>A System.Threading.Task that represents the download operation, null if the download is not paused.</returns>
        public Task ResumeAsync() {
            if (isDisposed) {
                throw new ObjectDisposedException("DownloadTask", "Cannot resume because this instance has bee disposed.");
            }

            if (Status == DownloadTaskStatus.Paused
                || Status == DownloadTaskStatus.Canceled
                || Status == DownloadTaskStatus.Failed) {
                return DownloadAsync(_saveTo, progressReporter);
            }

            return Task.Delay(0); // nothing to do so return a completed task
        }

        /// <summary>
        /// Cancels the download if it is paused or downloading.
        /// </summary>
        public void Cancel() {
            if (Status == DownloadTaskStatus.Downloading) {
                Status = DownloadTaskStatus.Cancelling;
                cancelSource.Cancel();
            }
            else if (Status == DownloadTaskStatus.Paused) {
                Status = DownloadTaskStatus.Canceled;
                if (DeleteWhenCanceled) {
                    File.Delete(_saveToIncomlete);
                }
            }
        }        





        /// <summary>
        /// Gets or sets the name of the downloaded file.
        /// </summary>
        public string FileName {
            get { return _fileName; }
            set { 
                if (Status == DownloadTaskStatus.NotStarted || Status == DownloadTaskStatus.Initializing
                    || Status == DownloadTaskStatus.Ready || Status == DownloadTaskStatus.Starting) {
                    Set(ref _fileName, Util.SterilizeFileName(value));
                    Extension = Path.GetExtension(FileName);
                }
                else {
                    throw new InvalidOperationException("Cannot change the file name. The download has already been started.");
                }
            }
        }

        /// <summary>
        /// Gets the path to where file will be saved once the download has completed successfully.
        /// </summary>
        public string FullPath {
            get { return _saveTo; }
            private set { Set(ref _saveTo, Util.SterilizePath(value)); }
        }

        /// <summary>
        /// Gets the extension of the file to be downloaded.
        /// </summary>
        public string Extension {
            get { return _extension; }
            set {
                if (Status == DownloadTaskStatus.NotStarted || Status == DownloadTaskStatus.Initializing
                    || Status == DownloadTaskStatus.Ready || Status == DownloadTaskStatus.Starting) {
                    Set(ref _extension, value);
                }
                else {
                    throw new InvalidOperationException("Cannot change the file extension. The download has already been started.");
                }
            }
        }

        /// <summary>
        /// Gets the download URL of the file.
        /// </summary>
        public string Url {
            get { return _url; }
            private set { Set(ref _url, value); }
        }

        /// <summary>
        /// Gets the total size of the file to be downloaded if available (in bytes). -1 if unavailable.
        /// </summary>
        public FileSize Size {
            get { return _size; }
            private set { Set(ref _size, value); }
        }

        /// <summary>
        /// Gets a value that determines whether the download has been initialized.
        /// </summary>
        public bool IsInitialized {
            get { return _isInitialized; }
            private set { Set(ref _isInitialized, value); }
        }

        /// <summary>
        /// Gets a value that determines whether the download supports pause/resumption.
        /// </summary>
        public bool ResumeSupported {
            get { return _resumeSupported; }
            private set { Set(ref _resumeSupported, value); }
        }

        /// <summary>
        /// Gets the amount of time the download spent downloading. (not including pauses)
        /// </summary>
        // when changing the name of this property remember to change the name in the timer event handler in this class's constructor
        public TimeSpan Elapsed { get { return downloadWatch.Elapsed; } }

        /// <summary>
        /// Gets or sets whether the temporary download file should be deleted when the download is canceled.
        /// </summary>
        public bool DeleteWhenCanceled {
            get { return _deleteWhenCanceled; }
            set { Set(ref _deleteWhenCanceled, value); }
        }

        /// <summary>
        /// Gets of sets the size of the memory buffer used for downloading.
        /// </summary>
        public int BufferSize {
            get { return _bufferSize; }
            set { Set(ref _bufferSize, value); }
        }

        /// <summary>
        /// Gets the state of the current download.
        /// </summary>
        public DownloadTaskStatus Status {
            get { return _status; }
            private set {
                Set(ref _status, value);
                RaiseStatusChanged();
            }
        }

        /// <summary>
        /// Gets progress information about the current download.
        /// </summary>
        public DownloadProgress Progress {
            get { return _progress; }
            private set { Set(ref _progress, value); }
        }




        // EVENTS

        /// <summary>
        /// Fires the StatusChanged event.
        /// </summary>
        private void RaiseStatusChanged() {
            if (StatusChanged != null) {
                StatusChanged(this, new StatusChangedEventArgs(Status));
            }
        }

        /// <summary>
        /// Fires the PropertyChanged event.
        /// </summary>
        /// <param name="propertyName"></param>
        private void RaisePropertyChanged(string propertyName) {
            if (PropertyChanged != null) {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        /// <summary>
        /// Fires when the download status has changed.
        /// </summary>
        public event StatusChangedEventHandler StatusChanged;

        /// <summary>
        /// Fires when a property has changed.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Delegate that represents event handlers for the DownloadTask.StatusChanged event
        /// </summary>
        /// <param name="sender">Source object.</param>
        /// <param name="e">Event arguments.</param>
        public delegate void StatusChangedEventHandler(object sender, StatusChangedEventArgs e);

        /// <summary>
        /// Type of event argument for the DownloadTask.StatusChanged event.
        /// </summary>
        public class StatusChangedEventArgs : EventArgs {
            /// <summary>
            /// Instantiates a new instance of the DownloadTask.StatusChangedEventArgs class.
            /// </summary>
            /// <param name="status">Download state.</param>
            public StatusChangedEventArgs(DownloadTaskStatus status) {
                Status = status;
            }

            /// <summary>
            /// Current state of the download.
            /// </summary>
            public DownloadTaskStatus Status { get; private set; }
        }

        /// <summary>
        /// Sets the backing field of a property and raises the PropertyChanged event.
        /// </summary>
        /// <typeparam name="T">Property/field type.</typeparam>
        /// <param name="field">Backing field.</param>
        /// <param name="value">New value.</param>
        /// <param name="propertyName">Name of the property that changed.</param>
        /// <returns>True if the property changed; false otherwise.</returns>
        private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
            if (EqualityComparer<T>.Default.Equals(field, value)) {
                return false;
            }
            field = value;
            RaisePropertyChanged(propertyName);

            return true;
        }




        /// <summary>
        /// Download states.
        /// </summary>
        public enum DownloadTaskStatus {
            NotStarted,
            Initializing,
            Ready,
            Starting,
            Downloading,
            Pausing,
            Paused,
            Cancelling,
            Canceled,
            Failed,
            Completing,
            Completed
        }

        /// <summary>
        /// Encapsulates information about the progress made with a download.
        /// </summary>
        public class DownloadProgress : INotifyPropertyChanged {
            private double _percentComplete;
            private FileSize _bytesDownloaded, _totalBytes;
            private TimeSpan _eta, _elapsed;
            private DownloadSpeed _speed;

            /// <summary>
            /// Instantiates a new instance of the DownloadTask.DownloadProgress class.
            /// </summary>
            /// <param name="percent">Percent completed.</param>
            /// <param name="bytes">Number of bytes downloaded so far.</param>
            /// <param name="totalBytes">Total number of bytes to be downloaded, i.e. the file size.</param>
            /// <param name="speed">Download speed.</param>
            /// <param name="eta">Estimated time of arrival (completion).</param>
            public DownloadProgress(double percent, FileSize bytes, FileSize totalBytes, DownloadSpeed speed, TimeSpan eta, TimeSpan elapsed) {
                _percentComplete = percent;
                _bytesDownloaded = bytes;
                _totalBytes = totalBytes;
                _speed = speed;
                _eta = eta;
                _elapsed = elapsed;
            }

            /// <summary>
            /// Gets the percentage of the download completed. (If TotalBytes is known)
            /// </summary>
            public double PercentComplete {
                get { return _percentComplete; }
                private set { Set(ref _percentComplete, value); }
            }

            /// <summary>
            /// Gets the number of bytes downloaded so far.
            /// </summary>
            public FileSize BytesDownloaded {
                get { return _bytesDownloaded; }
                private set { Set(ref _bytesDownloaded, value); }
            }

            /// <summary>
            /// Gets the total number of bytes to be downloaded, i.e. the file size. 
            /// (Same as BytesDownloaded if file size info could not be retrieved from the server)
            /// </summary>
            public FileSize TotalBytes {
                get { return _totalBytes; }
                private set { Set(ref _totalBytes, value); }
            }

            /// <summary>
            /// Gets the download speed.
            /// </summary>
            public DownloadSpeed Speed {
                get { return _speed; }
                private set { Set(ref _speed, value); }
            }

            /// <summary>
            /// Gets estimated time of arrival (completion).
            /// </summary>
            public TimeSpan Eta {
                get { return _eta; }
                private set { Set(ref _eta, value); }
            }

            /// <summary>
            /// Gets the elapsed download time.
            /// </summary>
            public TimeSpan Elapsed {
                get { return _elapsed; }
                private set { Set(ref _elapsed, value); }
            }

            /// <summary>
            /// Updates the current instance from an existing instance.
            /// </summary>
            /// <param name="p"></param>
            public void Update(DownloadProgress p) {
                PercentComplete = p.PercentComplete;
                BytesDownloaded = p.BytesDownloaded;
                TotalBytes = p.TotalBytes;
                Speed = p.Speed;
                Eta = p.Eta;
                Elapsed = p.Elapsed;
            }

            /// <summary>
            /// Fires when a property has changed.
            /// </summary>
            public event PropertyChangedEventHandler PropertyChanged;

            /// <summary>
            /// Fires the PropertyChanged event.
            /// </summary>
            /// <param name="propertyName"></param>
            private void RaisePropertyChanged(string propertyName) {
                if (PropertyChanged != null) {
                    PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
                }
            }

            /// <summary>
            /// Sets the backing field of a property and raises the PropertyChanged event.
            /// </summary>
            /// <typeparam name="T">Property/field type.</typeparam>
            /// <param name="field">Backing field.</param>
            /// <param name="value">New value.</param>
            /// <param name="propertyName">Name of the property that changed.</param>
            /// <returns>True if the property changed; false otherwise.</returns>
            private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
                if (EqualityComparer<T>.Default.Equals(field, value)) {
                    return false;
                }
                field = value;
                RaisePropertyChanged(propertyName);

                return true;
            }
        }        

        /// <summary>
        /// Encapsulates the average speed of a download given the number of bytes download and the time it took to download those bytes.
        /// The Value is in bytes/second and can be converted to a formatted string (showing download speed unit) by calling ToString.
        /// </summary>
        public struct DownloadSpeed {
            private long _size;     // in bytes
            private double _time;   // in seconds

            /// <summary>
            /// Instantiates a new instance of the DownloadTask.DownloadSpeed class.
            /// </summary>
            /// <param name="bytes">Bytes downloaded.</param>
            /// <param name="milliseconds">Time taken to download bytes.</param>
            public DownloadSpeed(long bytes, TimeSpan duration)
                : this() {
                _size = bytes;
                _time = duration.TotalSeconds;
                Value = bytes / _time;
            }

            /// <summary>
            /// Instantiates a new instance of the DownloadTask.DownloadSpeed class.
            /// </summary>
            /// <param name="ds">An existing DownloadSpeed instance.</param>
            public DownloadSpeed(DownloadSpeed ds)
                : this() {
                Value = ds.Value;
            }

            /// <summary>
            /// Instantiates a new instance of the DownloadTask.DownloadSpeed class.
            /// </summary>
            /// <param name="bytesPerSecond">Download speed in bytes per second.</param>
            public DownloadSpeed(double bytesPerSecond)
                : this() {
                Value = bytesPerSecond;
            }

            /// <summary>
            /// Gets the speed in bytes/s.
            /// </summary>
            public double Value { get; private set; }

            public static implicit operator double(DownloadSpeed ds) {
                return ds.Value;
            }

            public static DownloadSpeed operator +(DownloadSpeed ds1, DownloadSpeed ds2) {
                return new DownloadSpeed(ds1.Value + ds2.Value);
            }

            public static DownloadSpeed operator /(DownloadSpeed ds, double n) {
                return new DownloadSpeed(ds.Value / n);
            }

            /// <summary>
            /// A textual representaion of the speed using units of measurement. (bytes/s, KB/s, MB/s, GB/s ... etc)
            /// </summary>
            /// <returns></returns>
            public override string ToString() {
                // default to bytes/s
                double speed = Value;
                string speedUnit;

                if (Value < Math.Pow(1024, 1)) {
                    speedUnit = "bytes/s";
                }
                else if (Value < Math.Pow(1024, 2)) { // KB/s
                    speed = (double)Value / Math.Pow(1024, 1);
                    speedUnit = "KB/s";
                }
                else if (Value < Math.Pow(1024, 3)) { // MB/s
                    speed = (double)Value / Math.Pow(1024, 2);
                    speedUnit = "MB/s";
                }
                else { // GB/s
                    speed = (double)Value / Math.Pow(1024, 3);
                    speedUnit = "GB/s";
                }

                return string.Format("{0:N2} {1}", speed, speedUnit);
            }
        }




        /// <summary>
        /// Releases all resources used by this instance.
        /// </summary>
        public void Dispose() {
            isDisposed = true; // prevent certain operations while disposing and afterwards
            Dispose(true);
        }

        protected virtual void Dispose(bool disposeManaged) {
            if (disposeManaged) {
                if (elapsedTimer != null) {
                    elapsedTimer.Dispose();
                }

                if (cancelSource != null) {
                    cancelSource.Dispose();
                }
            }

            // ... no native resources to dispose ...
        }
    }
}
