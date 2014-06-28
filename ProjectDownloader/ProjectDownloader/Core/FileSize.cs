using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProjectDownloader.Core {
    /// <summary>
    /// Encapsulates the size of a file in bytes which can be converted to a formatted string (showing file size unit) by calling ToString.
    /// </summary>
    public struct FileSize {
        /// <summary>
        /// Instantiates a new instance of the DownloadTask.FileSize class.
        /// </summary>
        /// <param name="bytes">Size in bytes.</param>
        public FileSize(long bytes)
            : this() {
            Value = bytes;
        }

        /// <summary>
        /// Size in bytes.
        /// </summary>
        public long Value { get; private set; }

        public static implicit operator long(FileSize fs) {
            return fs.Value;
        }

        /// <summary>
        /// A textual representaion of the size using units of measurement. (bytes, KB, MB, GB ... etc)
        /// </summary>
        /// <returns></returns>
        public override string ToString() {
            string sizeUnit = "bytes";
            double size = 0;

            if (Value < 1024) { // bytes
                size = Value;
            }
            else if (Value < 1024 * 1024) { // KB
                size = (double)Value / 1024;
                sizeUnit = "KB";
            }
            else if (Value < 1024 * 1024 * 1024) { // MB
                size = (double)Value / 1024 / 1024;
                sizeUnit = "MB";
            }
            else { // GB
                size = (double)Value / 1024 / 1024 / 1024;
                sizeUnit = "GB";
            }

            // validate
            if (double.IsNaN(size) || size < 0) {
                size = 0;
            }

            return string.Format("{0:N2} {1}", size, sizeUnit);
        }
    }
}
