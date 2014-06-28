using System;
using System.IO;
using System.Windows.Data;

namespace ProjectDownloader {
    public static class Util {
        /// <summary>
        /// Checks if a URL is a YouTube URL.
        /// </summary>
        /// <param name="url">URL to check.</param>
        /// <returns>A value that tell whether the URL is a YouTube URL.</returns>
        public static bool IsYoutubeVideoUrl(string url) {
            return url.Contains("youtube.com/watch?")
                || url.Contains("youtu.be/");
        }


        /// <summary>
        /// Removes all invalid characters from a file name.
        /// </summary>
        /// <param name="path">File name to be sterilized.</param>
        /// <returns>The sterilized file name.</returns>
        public static string SterilizeFileName(string fileName) {
            foreach (char c in Path.GetInvalidFileNameChars()) {
                if (fileName.Contains(c.ToString())) {
                    fileName = fileName.Replace(c.ToString(), "");
                }
            }

            return fileName;
        }


        /// <summary>
        /// Removes all invalid characters from a path.
        /// </summary>
        /// <param name="fileName">Path to be sterilized.</param>
        /// <returns>The sterilized path.</returns>
        public static string SterilizePath(string path) {
            foreach (char c in Path.GetInvalidPathChars()) {
                if (path.Contains(c.ToString())) {
                    path = path.Replace(c.ToString(), "");
                }
            }

            return path;
        }

    }




    public class TimeSpanConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
            const string dayFormat = "{0:%d} day(s) {0:%h} hour(s) {0:%m} min {0:%s} seconds";
            const string hourFormat = "{0:%h} hour(s) {0:%m} min {0:%s} seconds";
            const string minFormat = "{0:%m} min {0:%s} seconds";
            const string secFormat = "{0:%s} seconds";

            TimeSpan ts = (TimeSpan)value;
            if (ts == TimeSpan.Zero || ts.Ticks < 0) {
                return " - ";
            }

            if (ts.Days > 0) {
                return string.Format(dayFormat, ts);
            }

            if (ts.Hours > 0) {
                return string.Format(hourFormat, ts);
            }

            if (ts.Minutes > 0) {
                return string.Format(minFormat, ts);
            }

            else {
                return string.Format(secFormat, ts);
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}
