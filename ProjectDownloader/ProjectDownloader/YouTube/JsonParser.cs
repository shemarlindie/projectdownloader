using System.Text.RegularExpressions;

namespace ProjectDownloader.YouTube {
    /// <summary>
    /// Contains functionality to get the value associated with a JSON key.
    /// </summary>
    class JsonParser {
        private string jsonData;

        /// <summary>
        /// Instantiates a new instance of the JasonParser class.
        /// </summary>
        /// <param name="jsonData">The JSON data to be parsed.</param>
        public JsonParser(string jsonData) {
            this.jsonData = jsonData;
        }




        /// <summary>
        /// Gets the value associated with a key.
        /// </summary>
        /// <param name="key">The JSON key.</param>
        /// <returns>The value associated with the key; null if the key is not found or is empty.</returns>
        public string GetKeyValue(string key) {
            return GetKeyValue(this.jsonData, key);
        }

        /// <summary>
        /// Gets the type of data of associated with a key.
        /// </summary>
        /// <param name="key">The JSON key.</param>
        /// <returns>The type of data associated with to that key.</returns>
        public DataType GetDataType(string key) {
            return GetDataType(this.jsonData, key);
        }



        
        /// <summary>
        /// Gets the value associated with a key.
        /// </summary>
        /// <param name="jsonData">A block of JSON data.</param>
        /// <param name="key">The JSON key.</param>
        /// <returns>The value associated with the key; null if the key is not found or is empty.</returns>
        public static string GetKeyValue(string jsonData, string key) {
            DataType valueType = GetDataType(jsonData, key);
            string arrayPattern = "\"" + key + "\"\\s*:\\s*\\[((\\S|\\s)*?)(\\],|\\]\\s*\\]|\\]\\s*\\})";
            string objectPattern = "\"" + key + "\"\\s*:\\s*\\{((\\S|\\s)*?)(\\},|\\}\\s*\\}|\\}\\s*\\])";
            string stringPattern = "\"" + key + "\"\\s*:\\s*\"(.*?)(\",|\"\\s*\\}|\"\\s*\\])";
            string numberPattern = "\"" + key + "\"\\s*:\\s*([0-9]*)(,|\\s*\\}|\\s*\\])";
            string value;

            switch (valueType) {
                case DataType.Array:
                    value = Regex.Match(jsonData, arrayPattern).Groups[1].ToString();
                    break;
                case DataType.Object:
                    value = Regex.Match(jsonData, objectPattern).Groups[1].ToString();
                    break;
                case DataType.String:
                    value = Regex.Match(jsonData, stringPattern).Groups[1].ToString();
                    break;
                case DataType.Number:
                    value = Regex.Match(jsonData, numberPattern).Groups[1].ToString();
                    break;
                default:
                    value = null;
                    break;
            }

            return value;
        }

        /// <summary>
        /// Gets the type of data of associated with a key.
        /// </summary>
        /// <param name="jsonData">A block of JSON data.</param>
        /// <param name="key">The JSON key.</param>
        /// <returns>The type of data associated with to that key.</returns>
        public static DataType GetDataType(string jsonData, string key) {
            string start = Regex.Match(jsonData, "\"" + key + "\"\\s*:\\s*(\\{|\\[|\"|[0-9])").Groups[1].ToString();
            DataType t;

            switch (start) {
                case "{":
                    t = DataType.Object;
                    break;
                case "[":
                    t = DataType.Array;
                    break;
                case "\"":
                    t = DataType.String;
                    break;
                default:
                    t = DataType.Nothing;
                    break;
            }

            // check if value is a number
            int res;
            if (int.TryParse(start, out res)) {
                t = DataType.Number;
            }

            return t;
        }



        
        /// <summary>
        /// Data types that a JSON key can be associated with.
        /// </summary>
        public enum DataType {
            /// <summary>
            /// Numeric data.
            /// </summary>
            Number,

            /// <summary>
            /// Textual data.
            /// </summary>
            String,

            /// <summary>
            /// An object.
            /// </summary>
            Object,

            /// <summary>
            /// An array of values.
            /// </summary>
            Array,

            /// <summary>
            /// No value.
            /// </summary>
            Nothing
        }
    }
}
