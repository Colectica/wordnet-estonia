using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.IO.Compression;
using Algenta.Colectica.Model.Ddi;
using Algenta.Colectica.Model;
using Algenta.Colectica.Model.Ddi.Serialization;
using Algenta.Colectica.Model.Ddi.Utility;
using Algenta.Colectica.Model.Utility;
using System.Xml;
using System.Xml.Linq;

namespace Colectica.Data.Wordnet
{
    class Program
    {
        private static string currentDir;
        private static string distDir;

        static void Main(string[] args)
        {
            currentDir = Directory.GetCurrentDirectory();
            distDir = Path.Combine(currentDir, "dist");

            GatherDistData();

            EstonianWordnetParser parser = new EstonianWordnetParser();
            var synSets = parser.Parse(Path.Combine(distDir, "kb73-utf8.txt"));
            parser = null;

            // remember assigned uuids
            Dictionary<string, Guid> idMapping = new Dictionary<string, Guid>();
            if (File.Exists(Path.Combine(currentDir,"idmapping.txt")))
            {
                idMapping = File.ReadAllLines(Path.Combine(currentDir, "idmapping.txt"), Encoding.UTF8).ToDictionary(x => x.Split(':')[0], v => new Guid(v.Split(':')[1]));
            }
            


            VersionableBase.DefaultAgencyId = "ee.stat";
            long version = 1;

            var concepts = new Dictionary<string, Concept>();
            var idMappingLines = new List<string>();
            Dictionary<string, string> literalToId = new Dictionary<string, string>();

            foreach(var synSet in synSets)
            {
                var estonianId = synSet.Token.Trim('@');

                var concept = new Concept() { Version = version };
                Guid uuid;
                if (idMapping.TryGetValue(estonianId, out uuid))
                {
                    concept.Identifier = uuid;
                }
                idMappingLines.Add(estonianId + ":" + concept.Identifier);
                concepts.Add(estonianId, concept);
                concept.UserIds.Add(new UserId("estonianWordnet", estonianId));

                var partOfSpeech = synSet.Children.Where(x => x.Token == PART_OF_SPEECH).First().ValueUnquoted;

                var literals = synSet.Children.Where(x => x.Token == VARIANTS).First().Children.Where(x => x.Token == LITERAL).ToList();
                
                for(int i = 0; i < literals.Count; ++i)
                {
                    var literal = literals[i];
                    var sense = literal.Children.Where(x => x.Token == SENSE).First().Value;
                    var name = literal.ValueUnquoted;

                    literalToId.Add(partOfSpeech + ":" + name + ":" + sense, estonianId);

                    
                    if (i == 0)
                    {
                        concept.ItemName["et-EE"] = name;
                    }

                    if (concept.Label.IsEmpty)
                    {
                        concept.Label["et-EE"] = name;
                    }
                    else
                    {
                        concept.Label["et-EE"] = concept.Label["et-EE"] + ", " + name;
                    }
                    

                    var definition = literal.Children.Where(x => x.Token == DEFINITION).Select(y => y.ValueUnquoted).FirstOrDefault();


                    var examples = literal.Children.Where(x => x.Token == EXAMPLES)
                        .SelectMany(x => x.Children.Where(y => y.Token == EXAMPLE))
                        .Select(z => z.ValueUnquoted).ToList();

                    StringBuilder sb = new StringBuilder();
                    string currentDescription = null;
                    if(concept.Description.TryGetValue("et-EE", out currentDescription))
                    {
                        sb.Append(currentDescription);
                    }

                    string fourSpaces = "";
                    if (literals.Count > 1)
                    {
                        sb.Append("+ ");
                        sb.AppendLine(name);
                        fourSpaces = "    ";
                    }
                    if (!string.IsNullOrWhiteSpace(definition))
                    {
                        sb.Append(fourSpaces);
                        sb.Append("- ");
                        sb.AppendLine(definition);
                    } 
                    foreach(var example in examples)
                    {
                        sb.Append(fourSpaces);
                        sb.Append("- ");
                        sb.AppendLine(example);
                    }

                    concept.Description["et-EE"] = sb.ToString();

                }

                var wordnetSynonyms = synSet.Children.Where(x => x.Token == EQ_LINKS)
                    .SelectMany(x => x.Children.Where(y => y.ValueUnquoted == "eq_synonym" || y.ValueUnquoted == "eq_near_synonym"))
                    .SelectMany(a => a.Children.Where(b => b.Token == TARGET_ILI)).ToList();

                foreach( var ili in wordnetSynonyms)
                {
                    if(ili.Children[1].Token != WORDNET_OFFSET) { continue; }

                    string partOfSpeechRef = ili.Children[0].ValueUnquoted;
                    string wordnetOffset = ili.Children[1].ValueUnquoted;
                    concept.UserIds.Add(new UserId("wordnet15", partOfSpeechRef + wordnetOffset));
                    concept.UserAttributes.Add(new UserAttribute()
                    {
                        Key = "x:wordnet15PartOfSpeech",
                        Value = partOfSpeechRef
                    });
                    concept.UserAttributes.Add(new UserAttribute()
                    {
                        Key = "x:wordnet15WordnetOffset",
                        Value = wordnetOffset
                    });
                }


            }

            synSets = null;

            Dictionary<string, WordnetEntry> pairs = new Dictionary<string, WordnetEntry>();
            WordnetParser.AddToDictionary(Path.Combine(distDir, "DICT", "ADJ.DAT"), pairs);
            WordnetParser.AddToDictionary(Path.Combine(distDir, "DICT", "ADV.DAT"), pairs);
            WordnetParser.AddToDictionary(Path.Combine(distDir, "DICT", "VERB.DAT"), pairs);
            WordnetParser.AddToDictionary(Path.Combine(distDir, "DICT", "NOUN.DAT"), pairs);
            
            foreach (var concept in concepts.Values)
            {
                List<string> wordnetIds = concept.UserIds.Where(y => y.Type == "wordnet15").Select(x => x.Identifier).ToList();

                WordnetEntry entry = null;
                foreach(var wordnetId in wordnetIds)
                {
                    if (pairs.TryGetValue(wordnetId, out entry))
                    {
                        concept.ItemName["en-US"] = entry.Names.First();
                        concept.Label["en-US"] = string.Join(", ", entry.Names);
                        if (!string.IsNullOrWhiteSpace(entry.Description))
                        {
                            concept.Description["en-US"] = entry.Description;
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("Unknown wordnet id");
                    }
                }
            }


            // has_hyperonym for parent
            var relations = File.ReadAllLines(Path.Combine(distDir, "kb73-utf8.rix"), Encoding.UTF8);
            foreach(var relation in relations)
            {
                var parts = relation.Split(':');

                var subjectId = parts[0];
                var predicate = parts[2];
                var objectPartOfSpeech = parts[3];
                var objectLiteral = parts[4];
                var objectSense = parts[5];

                if(predicate != "has_hyperonym") { continue; }

                string objectId;
                if (literalToId.TryGetValue($"{objectPartOfSpeech}:{objectLiteral}:{objectSense}", out objectId))
                {
                    var subjectConcept = concepts[subjectId];
                    var objectConcept = concepts[objectId];
                    subjectConcept.SubclassOf.Add(objectConcept);
                }
                else { throw new InvalidOperationException("Unknown literal"); }

            }


            FragmentInstance instance = new FragmentInstance();
            instance.Items.Merge(concepts.Values);
            WriteFragment(Path.Combine(currentDir, "estonian-wordnet.ddi32.xml"),instance);

            // compress the DDI since it is large
            using (FileStream fs = new FileStream(Path.Combine(currentDir, "estonian-wordnet.ddi32.xml.zip"), FileMode.Create))
            using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(Path.Combine(currentDir, "estonian-wordnet.ddi32.xml"), "estonian-wordnet.ddi32.xml");
            }

            File.WriteAllLines(Path.Combine(currentDir, "idmapping.txt"), idMappingLines, Encoding.UTF8);

        }

        private static void WriteFragment(string fileName, FragmentInstance instance)
        {
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.NewLineChars = "\n";

            using (XmlWriter writer = XmlWriter.Create(fileName, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("ddi", "FragmentInstance", Ddi32Serializer.NamespaceInstance.NamespaceName);
                writer.WriteAttributeString("xmlns", "r", null, Ddi32Serializer.NamespaceReusable.NamespaceName);

                Ddi32Serializer serializer = new Ddi32Serializer();
                foreach (IVersionable v in instance.Items)
                {
                    XElement fragment = v.GetDdi32FragmentRepresentation(serializer);
                    fragment.WriteTo(writer);
                }

                writer.WriteEndElement();//ddi:FragmentInstance
                writer.WriteEndDocument();
            }
        }

        private static string VARIANTS = "VARIANTS";
        private static string LITERAL = "LITERAL";
        private static string DEFINITION = "DEFINITION";
        private static string EXAMPLES = "EXAMPLES";
        private static string EXAMPLE = "EXAMPLE";
        private static string EQ_LINKS = "EQ_LINKS";
        private static string EQ_RELATION = "EQ_RELATION";
        private static string TARGET_ILI = "TARGET_ILI";
        private static string WORDNET_OFFSET = "WORDNET_OFFSET";
        private static string SENSE = "SENSE";
        private static string PART_OF_SPEECH = "PART_OF_SPEECH";

        



        private static void GatherDistData()
        {            
            if (!Directory.Exists(distDir))
            {
#if DEBUG
                Directory.CreateDirectory(distDir);
                File.Copy(@"..\..\dist\wn15.zip", "wn15.zip");
                File.Copy(@"..\..\dist\estonianWordnetkb73.zip", "estonianWordnetkb73.zip");
                ZipFile.ExtractToDirectory("wn15.zip", distDir);
                ZipFile.ExtractToDirectory("estonianWordnetkb73.zip", distDir);
                File.Copy(@"..\..\dist\idmapping.txt", "idmapping.txt");
#endif
            }
        }
    }




}
