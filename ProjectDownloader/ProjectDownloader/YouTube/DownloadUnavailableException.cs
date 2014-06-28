using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProjectDownloader.YouTube {
    class DownloadUnavailableException : Exception {
        public DownloadUnavailableException() : base() { }

        public DownloadUnavailableException(string videoUrl)
            : base(string.Format("The video at this URL is unavailable for download: {0}", videoUrl)) { }

        public DownloadUnavailableException(string videoUrl, Exception innerException) 
            : base(string.Format("The video at this URL is unavailable for download: {0}", videoUrl), innerException) { }

    }
}
