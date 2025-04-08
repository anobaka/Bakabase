using System.Xml;
using Bakabase.Modules.Enhancer.Components.Enhancers.Kodi;
using Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models;
using System.Xml.Serialization;
using Newtonsoft.Json;
using Formatting = Newtonsoft.Json.Formatting;

namespace Bakabase.Modules.ThirdParty.Tests.Kodi;

[TestClass]
public class KodiTests
{
    [TestMethod]
    public async Task TestXmlParser()
    {
        // todo: TBD

        var xmlFiles = Directory.GetFiles("Kodi/Xml");

        XmlDocument mergedXml = new XmlDocument();

        // Load the first XML file
        mergedXml.Load(xmlFiles[0]);

        // Get the root element of the first XML file
        XmlNode mergedRoot = mergedXml.DocumentElement;

        foreach (var xmlFile in xmlFiles)
        {
            var xml = await File.ReadAllTextAsync(xmlFile);
            // var doc = new XmlDocument();
            // doc.LoadXml(xml.Trim());

            // var json = JsonConvert.SerializeXmlNode(doc, Formatting.Indented);

            // var serializer = new XmlSerializer(typeof(KodiEnhancerContext));
            // using var reader = new StringReader(mergedXml); // Replace xmlContent with your XML string.
            // var mergedData = (KodiEnhancerContext?)serializer.Deserialize(reader);

            // var data = JsonConvert.DeserializeObject<KodiEnhancerContext>(json);

            // Process other XML files
                XmlDocument tempXml = new XmlDocument();
                tempXml.LoadXml(xml.Trim());

                // Get the root element of the current XML file
                XmlNode tempRoot = tempXml.DocumentElement;

                // Append all child nodes of the current root to the merged root
                foreach (XmlNode child in tempRoot.ChildNodes)
                {
                    XmlNode importedNode = mergedXml.ImportNode(child, true);
                    mergedRoot.AppendChild(importedNode);
                }

            // Save or display the merged XML
            mergedXml.Save("Kodi/Xml/merged.xml");
        }
    }
}