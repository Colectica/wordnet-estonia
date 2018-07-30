using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Colectica.Data.Wordnet
{
    public class EstonianWordnetParser
    {
        char[] space = new char[] { ' ' };
        Queue<Entry> lines;

        public List<Entry> Parse(string wordnetFile)
        {
            lines = new Queue<Entry>(File.ReadAllLines(wordnetFile, Encoding.UTF8).Select(x => ParseLine(x.Trim())));

            List<Entry> results = new List<Entry>();

            while(lines.Count > 0)
            {
                var currentEntry = lines.Dequeue();
                if(currentEntry == null) { continue; }

                results.Add(currentEntry);
                ParseChildren(new List<Entry>() { currentEntry });
            }
            return results;
        }

        public void ParseChildren(List<Entry> current)
        {
            var nextEntry = lines.Peek();
            if (nextEntry == null)
            {
                lines.Dequeue();
                return;
            }
            while(nextEntry.Level < current.Last().Level)
            {
                current.Remove(current.Last());
                if(current.Count == 0) { return; }
            }

            nextEntry = lines.Dequeue();

            var lastEntry = current.Last();
            if (nextEntry.Level == lastEntry.Level) // sibling child
            {
                current.Remove(lastEntry);

                var parent = current.Last();
                parent.Children.Add(nextEntry);
                current.Add(nextEntry);                
                ParseChildren(current);
            }
            else if(nextEntry.Level > lastEntry.Level)
            {
                lastEntry.Children.Add(nextEntry);
                current.Add(nextEntry);
                ParseChildren(current);
            }
        }


        private Entry ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) { return null; }

            int tokenStart = line.IndexOf(' ');

            string s = line.Substring(0, tokenStart);

            Entry result = new Entry();
            result.Level = int.Parse(s);

            int valueStart = line.IndexOf(' ', tokenStart+1);

            if(valueStart != -1)
            {
                result.Token = line.Substring(tokenStart + 1, valueStart - tokenStart - 1);
                result.Value = line.Substring(valueStart + 1);
            }
            else
            {
                result.Token = line.Substring(tokenStart + 1);
            }



            return result;
        }
    }

    public class Entry
    {
        public int Level { get; set; }
        public string Token { get; set; }
        public string Value { get; set; }
        public string ValueUnquoted
        {
            get
            {
                return Value.Trim().Trim('"');
            }
        }
        public List<Entry> Children = new List<Entry>();

        public override string ToString()
        {
            return $"{Level} {Token} {Value}";
        }
    }
}
