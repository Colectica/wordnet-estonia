using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Colectica.Data.Wordnet
{
    public class WordnetParser
    {

        public static void AddToDictionary(string wordnetFile, Dictionary<string, WordnetEntry> pairs)
        {
            foreach( string line in File.ReadAllLines(wordnetFile, Encoding.UTF8))
            {
                var entry = ParseLine(line);
                if(entry != null)
                {
                    pairs.Add(entry.Id, entry);
                }
                
            }
        }

        static char[] space = new char[] { ' ' };
        private static WordnetEntry ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) { return null; }
            if (!line.StartsWith("0")) { return null; }

            var parts = line.Split(space, StringSplitOptions.RemoveEmptyEntries);

            WordnetEntry result = new WordnetEntry();
            
            string partOfSpeech = parts[2];
            if(partOfSpeech == "s") { partOfSpeech = "a"; }
            if (partOfSpeech == "r") { partOfSpeech = "b"; }

            result.Id = partOfSpeech + parts[0].TrimStart('0');

            int wordCount = int.Parse(parts[3], System.Globalization.NumberStyles.HexNumber);
            for(int i = 0; i < wordCount; i++)
            {
                result.Names.Add(parts[4 + (i * 2)]);
            }

            int defStart = line.IndexOf('|');

            if(defStart != -1)
            {
                result.Description = line.Substring(defStart + 1).Trim();
            }
            return result;
        }
    }

    public class WordnetEntry
    {
        public string Id { get; set; }
        public List<string> Names { get; } = new List<string>();
        public string Description { get; set; }
    }
}
