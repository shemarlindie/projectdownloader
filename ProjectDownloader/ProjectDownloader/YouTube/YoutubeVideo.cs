using ProjectDownloader.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

/* TODO:
 * Add 3D download support
 * Find out why it gets weird formats sometimes (static, etc.)
 * YouTube video grabber (download selected videos from a page)
 * YouTube playlist download
*/

namespace ProjectDownloader.YouTube {
    /// <summary>
    /// Contains information about a YouTube video; Title, Download URLs, Length, etc.
    /// </summary>
    public class YouTubeVideo {
        private readonly string thumbnailUrlFormatString = "http://i1.ytimg.com/vi/{0}/mqdefault.jpg";
        private const string stdDataBlockStart = "url_encoded_fmt_stream_map";
        private const string adpDataBlockStart = "adaptive_fmts";
        private string videoData;

        /// <summary>
        /// Instantiates a new instance of YouTubeVideo class.
        /// </summary>
        /// <param name="watchUrl">A link to a YouTube video.</param>
        /// <exception cref="System.Net.WebException"></exception>
        /// <exception cref="ProjectDownloader.YouTube.ParsingErrorException">The value of a required key was not found.</exception>
        /// <exception cref="ProjectDownloader.YouTube.DownloadUnavailableException">The video is protected against downloads. -or- 
        /// The download data could not be found.</exception>
        public YouTubeVideo(string watchUrl) {
            WatchUrl = watchUrl;
            string html = DownloadVideoPage(watchUrl);
            html = Uri.UnescapeDataString(html);

            videoData = JsonParser.GetKeyValue(html, "args"); // get js block with video information        

            // check if data block has the required data block
            if (videoData == null || !videoData.Contains(stdDataBlockStart)/*&& !videoData.Contains(adpDataBlockStart)*/) {
                throw new DownloadUnavailableException(watchUrl);
            }

            Title = Util.SterilizeFileName(Regex.Unescape(JsonParser.GetKeyValue(videoData, "title"))); // get video title
            Id = JsonParser.GetKeyValue(videoData, "video_id"); // get video id

            Channel = Regex.Match(html, // get channel that uploaded video
                "<div class=\"yt-user-info\">\\s*<a.*?>(.*?)</a>").Groups[1].ToString();

            string lenStr = JsonParser.GetKeyValue(videoData, "length_seconds"); // get length of video
            try {
                Length = TimeSpan.FromSeconds(int.Parse(lenStr));
            }
            catch (Exception ex) {
                throw new ParsingErrorException("length_seconds", ex);
            }

            ThumbnailUrl = string.Format(thumbnailUrlFormatString, Id); // get video thumbnail url
            AvailableDownloads = GetAvailableDownloads(videoData);
        }

        /// <summary>
        /// Downloads the page at the specified URL.
        /// </summary>
        /// <param name="url">The URL of the page to download.</param>
        /// <returns>The downloaded page as a string.</returns>
        /// <exception cref="System.Net.WebException">
        /// The URI formed by combining System.Net.WebClient.BaseAddress and address is invalid. 
        /// -or- An error occurred while downloading the resource.
        /// </exception>
        private string DownloadVideoPage(string url) {
            string html;

            using (WebClient wc = new WebClient()) {
                html = wc.DownloadString(url);
            }

            return html;
        }
        
        /// <summary>
        /// Gets the value of a URI query string key.
        /// </summary>
        /// <param name="queryString">The URI query string.</param>
        /// <param name="key">The key whose value should be retrieved.</param>
        /// <returns>The value of the provided key.</returns>
        /// <exception cref="ProjectDownloader.YouTube.ParsingErrorException">The value of a required key was not found.</exception>
        private string GetUriQueryValue(string queryString, string key) {
            string pattern = key + "=(\"?)([^\\\\&;]+)\\1"; // matches a uri query key/value pair
            string value = Regex.Match(queryString, pattern).Groups[2].ToString();

            if (string.IsNullOrEmpty(value)) {
                throw new ParsingErrorException(key);
            }

            return value;
        }

        /// <summary>
        /// Gets the format data for the video.
        /// </summary>
        /// <param name="vidDataBlock">The block that contains video format data.</param>
        /// <returns>A list of the format data blocks.</returns>
        private List<string> ParseVideoDataBlock(string vidDataBlock) {
            List<string> formatList = new List<string>();
            string vidDataPattern = "((.+?=).+?)(,\\2)"; // matches info about a video (resolution, format, etc..)
            string vidDataStart = null;

            // get video data for each format
            while (true) {
                Match m = Regex.Match(vidDataBlock, vidDataPattern);
                string dataMatch = m.Groups[1].ToString();
                if (dataMatch == "") { // no more format blocks left
                    break;
                }

                // get start of data for each format data block
                if (vidDataStart == null) {
                    vidDataStart = m.Groups[2].ToString();
                    vidDataBlock += "," + vidDataStart; // add at end so that the last video data segment will match the pattern
                }

                formatList.Add(dataMatch); // add current data to list                   
                vidDataBlock = vidDataBlock.Replace(dataMatch + ",", ""); // remove current data from string
            }

            return formatList;
        }

        /// <summary>
        /// Checks if a key value is null or empty and throw an exception if it is.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <exception cref="ProjectDownloader.YouTube.ParsingErrorException">The value of a required key was not found.</exception>
        private void ValidateParse(string key, string value) {
            if (string.IsNullOrEmpty(value)) {
                throw new ParsingErrorException(key);
            }
        }

        /// <summary>
        /// Get the available downloads for a video.
        /// </summary>
        /// <param name="dataBlock">The video's data block.</param>
        /// <returns>A list of downloads.</returns>
        /// <exception cref="ProjectDownloader.YouTube.ParsingErrorException">
        /// An error occured while extracting information from the data block.
        /// </exception>
        private List<DownloadInfo> GetAvailableDownloads(string dataBlock) {
            List<DownloadInfo> dls = new List<DownloadInfo>();
            string urlPattern = "url=([^\\\\,]+)";

            // STANDARD
            string stdVidDataBlock = JsonParser.GetKeyValue(dataBlock, stdDataBlockStart); // video information for standard formats
            if (stdVidDataBlock == null) { // data block is empty
                throw new DownloadUnavailableException(WatchUrl);
            }

            string itagResolutionData = JsonParser.GetKeyValue(dataBlock, "fmt_list");  // itag numbers with resolution for standard formats

            // get available resolutions
            MatchCollection itagResMatches = Regex.Matches(itagResolutionData, "([0-9]+)\\\\/([0-9]+)x([0-9]+)[^,]+"); // matches itag numbers with their resolution
            var itagResolutions = new Dictionary<int, Size>(itagResMatches.Count);

            try {
                foreach (Match m in itagResMatches) {
                    int itagNo = int.Parse(m.Groups[1].ToString());
                    Size res = new Size(int.Parse(m.Groups[2].ToString()), int.Parse(m.Groups[3].ToString()));
                    itagResolutions[itagNo] = res;
                }
            }
            catch (FormatException ex) {
                throw new ParsingErrorException("fmt_list", ex);
            }

            List<string> stdVidDataList = ParseVideoDataBlock(stdVidDataBlock);

            try {
                foreach (string vd in stdVidDataList) {
                    string tmpItagStr = GetUriQueryValue(vd, "itag");
                    int itagNo = int.Parse(tmpItagStr);
                    string format = GetUriQueryValue(vd, "type").Replace("video/", "").Replace("x-", "");
                    string url = Regex.Match(vd, urlPattern).Groups[1].ToString();

                    dls.Add(new DownloadInfo(url, format, itagResolutions[itagNo], itagNo));
                }
            }
            catch (FormatException ex) {
                throw new ParsingErrorException("url", ex);
            }


            // ADAPTIVE (video and audio is separated)
            //string adpVidDataBlock = GetJsonKeyValue(dataBlock, adpDataBlockStart);
            //List<string> adpVidDataList = ParseVideoDataBlock(adpVidDataBlock);

            //foreach (string vd in adpVidDataList) {
            //    string tmpItagStr = GetUriQueryValue(vd, "itag");
            //    Size sz = new Size();
            //    int itagNo = int.Parse(tmpItagStr);
            //    string format = GetUriQueryValue(vd, "type");
            //    bool isAudio = format.Contains("audio");
            //    if (isAudio) {
            //        format = format.Replace("audio/", "");
            //        format += " (audio)";
            //    }
            //    else {
            //        format = format.Replace("video/", "");

            //        // get video resolution
            //        Match szMatch = Regex.Match(vd, "size=([0-9]+)x([0-9]+)");
            //        sz = new Size(int.Parse(szMatch.Groups[1].ToString()), int.Parse(szMatch.Groups[2].ToString()));

            //    }
            //    string url = Regex.Match(vd, urlPattern).Groups[1].ToString();

            //    dls.Add(new DownloadInfo(url, format, sz, itagNo, isAudio));
            //}

            return dls;
        }



        /// <summary>
        /// Get the size of a video.
        /// </summary>
        /// <param name="dInfo">Video download information.</param>
        /// <returns>Size of the video.</returns>
        /// <exception cref="System.Net.WebException">Could not connect to the internet or the server.</exception>
        public static Task<FileSize> GetVideoSizeAsync(DownloadInfo dInfo) {
            return Task.Run(() => {
                long size;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(dInfo.DownloadUrl);
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse()) {
                    size = res.ContentLength;
                }

                return new FileSize(size);
            });
        } 



        /// <summary>
        /// Gets the YouTube ID of the video.
        /// </summary>
        public string Id { get; private set; }

        /// <summary>
        /// Gets the video title.
        /// </summary>
        public string Title { get; private set; }

        /// <summary>
        /// Gets the channel that uploaded the video.
        /// </summary>
        public string Channel { get; private set; }

        /// <summary>
        /// Gets the video URL.
        /// </summary>
        public string WatchUrl { get; private set; }

        /// <summary>
        /// Gets the available downloads for this video.
        /// </summary>
        public List<DownloadInfo> AvailableDownloads { get; private set; }
        
        /// <summary>
        /// Gets the length of the video.
        /// </summary>
        public TimeSpan Length { get; private set; }

        /// <summary>
        /// Gets the the thumbnail URL for the video.
        /// </summary>
        public string ThumbnailUrl { get; private set; }



        /// <summary>
        /// Holds information about a video download. Resolution, Download URL, etc.
        /// </summary>
        public class DownloadInfo {
            /// <summary>
            /// Instantiates a new instance of the YouTubeVideo.DownloadInfo class.
            /// </summary>
            /// <param name="dlUrl">The video's download URL.</param>
            /// <param name="format">The encoded format of the video.</param>
            /// <param name="res">The resolution of the video.</param>
            /// <param name="itagNo">The YouTube itag that identifies the video's format and resolution.</param>
            /// <param name="isAudio">A value that determines whether the download URL points to an audio file instead of a video.</param>
            public DownloadInfo(string dlUrl, string format, Size res, int itagNo, bool isAudio = false) {
                DownloadUrl = dlUrl;
                Format = format;
                Resolution = res;
                Itag = itagNo;
                IsAudio = isAudio;
            }

            /// <summary>
            /// Gets the download URL of the video.
            /// </summary>
            public string DownloadUrl { get; private set; }

            /// <summary>
            /// Gets the encoded format of the video.
            /// </summary>
            public string Format { get; private set; }

            /// <summary>
            /// Gets the resolution of the video.
            /// </summary>
            public Size Resolution { get; private set; }

            /// <summary>
            /// Gets the YouTube itag that identifies to the video's format and resolution.
            /// </summary>
            public int Itag { get; private set; }

            /// <summary>
            /// Gets a value that determine whether the download URL points to an audio file instead of a video.
            /// </summary>
            public bool IsAudio { get; private set; }
        }
    }

}