using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProjectDownloader.YouTube {
    class ParsingErrorException : Exception {
        public ParsingErrorException() : base() { }

        public ParsingErrorException(string key) : base(string.Format("Unable to get the value this key: {0}", key)) { }

        public ParsingErrorException(string key, Exception innerException) 
            : base(string.Format("Unable to get the value this key: {0}", key), innerException) { }
    }
}
