using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;

namespace ProjectDownloader.YouTube {
    class ItagInfo {
        private static Dictionary<int, ItagInfo> ItagDictionary = new Dictionary<int, ItagInfo>(20);

        static ItagInfo() {
            // - Standard -
            ItagDictionary[5] = new ItagInfo(5, new Size(320, 240), "flv");
            ItagDictionary[17] = new ItagInfo(17, new Size(176, 144), "3gpp");
            ItagDictionary[18] = new ItagInfo(18, new Size(640, 360), "mp4");
            ItagDictionary[22] = new ItagInfo(22, new Size(1280, 720), "mp4");
            ItagDictionary[36] = new ItagInfo(36, new Size(320, 240), "3gpp");
            ItagDictionary[43] = new ItagInfo(43, new Size(640, 360), "webm");

            // - Adaptive -
            ItagDictionary[136] = new ItagInfo(136, new Size(1280, 720), "mp4");
            ItagDictionary[137] = new ItagInfo(137, new Size(1920, 1080), "mp4");
            ItagDictionary[135] = new ItagInfo(135, new Size(854, 480), "mp4");
            ItagDictionary[134] = new ItagInfo(134, new Size(640, 360), "mp4");
            ItagDictionary[133] = new ItagInfo(133, new Size(426, 240), "mp4");
            ItagDictionary[160] = new ItagInfo(160, new Size(256, 144), "mp4");
            ItagDictionary[247] = new ItagInfo(247, new Size(1280, 720), "webm");
            ItagDictionary[248] = new ItagInfo(248, new Size(1920, 1080), "webm");
            ItagDictionary[244] = new ItagInfo(244, new Size(854, 480), "webm");
            ItagDictionary[243] = new ItagInfo(243, new Size(640, 360), "webm");
            ItagDictionary[242] = new ItagInfo(242, new Size(426, 240), "webm");
            ItagDictionary[241] = new ItagInfo(241, new Size(256, 144), "webm");

            // - Audio -
            ItagDictionary[140] = new ItagInfo(140, new Size(0, 0), "mp4") { IsAudio = true };
            ItagDictionary[171] = new ItagInfo(171, new Size(0, 0), "webm") { IsAudio = true };
        }

        private ItagInfo(int id, Size res, string format, bool isKnown = true) {
            Id = id;
            Resolution = res;
            Format = format;
            IsKnown = isKnown;
        }



        public static ItagInfo FromItagNumber(int itagNo) {
            ItagInfo i;
            try {
                i = ItagDictionary[itagNo];
            }
            catch {
                i = new ItagInfo(itagNo, new Size(0, 0), "", false);
            }

            return i;
        }


        public bool IsAudio { get; private set; }

        public bool IsKnown { get; private set; }

        public int Id { get; private set; }

        public Size Resolution { get; private set; }

        public string Format { get; private set; }
    }
}
