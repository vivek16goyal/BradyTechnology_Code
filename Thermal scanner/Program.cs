using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Configuration;

namespace GeneratorDataProcessor
{
    class Program
    {
        static void Main(string[] args)
        {
            // Set default folder paths on the desktop
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string inputFolder = Path.Combine(desktopPath, "InputFolder");
            string outputFolder = Path.Combine(desktopPath, "OutputFolder");
            string referenceFolder = Path.Combine(desktopPath, "ReferenceFolder");
            string referenceDataPath = Path.Combine(referenceFolder, "ReferenceData.xml");

            // Create directories if they do not exist
            Directory.CreateDirectory(inputFolder);
            Directory.CreateDirectory(outputFolder);
            Directory.CreateDirectory(referenceFolder);

            // Watch for new files in the input folder
            using var watcher = new FileSystemWatcher(inputFolder, "*.xml")
            {
                NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.LastWrite
            };

            watcher.Created += (sender, e) =>
            {
                Console.WriteLine($"Processing file: {e.Name}");
                ProcessFile(e.FullPath, outputFolder, referenceDataPath);
            };

            watcher.Changed += (sender, e) =>
            {
                Console.WriteLine($"Processing file: {e.Name}");
                ProcessFile(e.FullPath, outputFolder, referenceDataPath);
            };

            watcher.EnableRaisingEvents = true;

            Console.WriteLine("Watching for new XML files. Press Enter to exit.");
            Console.ReadLine();
        }

        static void ProcessFile(string inputFilePath, string outputFolder, string referenceDataPath)
        {
            try
            {
                // Load XML data
                var generationReport = XDocument.Load(inputFilePath);
                var referenceData = XDocument.Load(referenceDataPath);

                // Extract reference factors
                var valueFactorsElement = referenceData.Root.Element("Factors").Element("ValueFactor");
                var emissionsFactorsElement = referenceData.Root.Element("Factors").Element("EmissionsFactor");

                // Prepare output XML structure
                var outputDocument = new XDocument(new XElement("GenerationOutput"));
                var totalsElement = new XElement("Totals");
                var maxEmissionsElement = new XElement("MaxEmissionGenerators");
                var heatRatesElement = new XElement("ActualHeatRates");

                // Process each generator type
                foreach (var generatorType in generationReport.Root.Elements())
                {
                    foreach (var generator in generatorType.Elements())
                    {
                        var generatorName = generator.Element("Name").Value;
                        var generatorTypeLocalName = generatorType.Name.LocalName;
                        var location = generator.Element("Location")?.Value;

                        // Get value factor and emission factor
                        var valueFactor = GetFactor(valueFactorsElement, location);
                        var emissionFactor = GetFactor(emissionsFactorsElement, location);

                        // Calculate total generation value
                        var totalGenerationValue = generator.Element("Generation").Elements("Day")
                            .Sum(day => (double)day.Element("Energy") * (double)day.Element("Price") * valueFactor);

                        totalsElement.Add(new XElement("Generator",
                            new XElement("Name", generatorName),
                            new XElement("Total", totalGenerationValue)));

                        // Calculate daily emissions and find the highest
                        var dailyEmissions = generator.Element("Generation")?.Elements("Day")
                             .Select(day => new
                             {
                                 Date = day.Element("Date")?.Value,
                                 Emission = (double.TryParse(day.Element("Energy")?.Value, out double energy) ? energy : 0)
                                            * (double.TryParse(generator.Element("EmissionsRating")?.Value, out double emissionsRating) ? emissionsRating : 0)
                                            * emissionFactor
                             })
                             .OrderByDescending(e => e.Emission)
                             .FirstOrDefault();

                        if (dailyEmissions != null)
                        {
                            maxEmissionsElement.Add(new XElement("Day",
                                new XElement("Name", generatorName),
                                new XElement("Date", dailyEmissions.Date),
                                new XElement("Emission", dailyEmissions.Emission)));
                        }

                        // Calculate actual heat rate for coal generators
                        if (generatorTypeLocalName == "Coal")
                        {
                            var totalHeatInput = (double)generator.Element("TotalHeatInput");
                            var actualNetGeneration = (double)generator.Element("ActualNetGeneration");
                            var actualHeatRate = totalHeatInput / actualNetGeneration;

                            heatRatesElement.Add(new XElement("ActualHeatRate",
                                new XElement("Name", generatorName),
                                new XElement("HeatRate", actualHeatRate)));
                        }
                    }
                }

                outputDocument.Root.Add(totalsElement);
                outputDocument.Root.Add(maxEmissionsElement);
                outputDocument.Root.Add(heatRatesElement);

                // Save the output XML
                var outputFilePath = Path.Combine(outputFolder, Path.GetFileName(inputFilePath).Replace(".xml", "-Result.xml"));
                outputDocument.Save(outputFilePath);
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        static double GetFactor(XElement factorElement, string location)
        {
            return location switch
            {
                "Offshore" => (double)factorElement.Element("Low"),
                "Onshore" => (double)factorElement.Element("High"),
                "Gas" => (double)factorElement.Element("Medium"),
                "Coal" => (double)factorElement.Element("High"),
                _ => 1.0
            };
        }
    }
}
