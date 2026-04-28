using System.Text.RegularExpressions;
using System.Xml;

namespace Bakabase.Modules.ThirdParty.Tests.Kodi;

[TestClass]
public class KodiTests
{
    [TestMethod]
    public async Task TestXmlParser()
    {
        // todo: TBD

        var xmlFiles = Directory.GetFiles("Kodi/Xml")
            .Where(f => !Path.GetFileName(f).Equals("merged.xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        XmlDocument mergedXml = new XmlDocument();
        mergedXml.AppendChild(mergedXml.CreateElement("kodi"));
        XmlNode mergedRoot = mergedXml.DocumentElement!;

        foreach (var xmlFile in xmlFiles)
        {
            var xml = await File.ReadAllTextAsync(xmlFile);

            // Strip the optional XML declaration (it's only allowed as the first node),
            // then wrap in a synthetic root so files like multi-episode.nfo (multiple
            // sibling root elements, which is the official Kodi format) parse in one shot.
            var body = Regex.Replace(xml.Trim(), @"^<\?xml[^?]*\?>\s*", "");

            XmlDocument tempXml = new XmlDocument();
            tempXml.LoadXml($"<root>{body}</root>");

            XmlNode tempRoot = tempXml.DocumentElement!;

            foreach (XmlNode child in tempRoot.ChildNodes)
            {
                XmlNode importedNode = mergedXml.ImportNode(child, true);
                mergedRoot.AppendChild(importedNode);
            }
        }

        mergedXml.Save("Kodi/Xml/merged.xml");
    }
}