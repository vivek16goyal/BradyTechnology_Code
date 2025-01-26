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
            string referenceDataPath = Path.Combine(desktopPath, "ReferenceFolder", "ReferenceData.xml");
            // Create directories if they do not exist
            Directory.CreateDirectory(inputFolder);
            Directory.CreateDirectory(outputFolder);
            Directory.CreateDirectory(Path.Combine(desktopPath, "ReferenceFolder"));

            // Watch for new files in the input folder
            FileSystemWatcher watcher = new FileSystemWatcher(inputFolder, "*.xml")
            {
                NotifyFilter = NotifyFilters.FileName
            };

            watcher.Created += (sender, e) =>
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
                var valueFactors = referenceData.Root.Element("Factors").Element("ValueFactor");
                var emissionsFactors = referenceData.Root.Element("Factors").Element("EmissionsFactor");

                // Prepare output XML structure
                var outputDoc = new XDocument(new XElement("GenerationOutput"));
                var totals = new XElement("Totals");
                var maxEmissions = new XElement("MaxEmissionGenerators");
                var heatRates = new XElement("ActualHeatRates");

                // Process each generator type
                foreach (var generatorType in generationReport.Root.Elements())
                {
                    foreach (var generator in generatorType.Elements())
                    {
                        string name = generator.Element("Name").Value;
                        string type = generatorType.Name.LocalName;
                        string location = generator.Element("Location")?.Value;

                        // Get value factor and emission factor
                        double valueFactor = GetFactor(valueFactors, location);
                        double emissionFactor = GetFactor(emissionsFactors, location);

                        // Calculate total generation value
                        double totalGenerationValue = generator.Element("Generation").Elements("Day")
                            .Sum(day => (double)day.Element("Energy") * (double)day.Element("Price") * valueFactor);

                        totals.Add(new XElement("Generator",
                            new XElement("Name", name),
                            new XElement("Total", totalGenerationValue)));

                        // Calculate daily emissions and find the highest
                        var dailyEmissions = generator.Element("Generation").Elements("Day")
                            .Select(day => new
                            {
                                Date = day.Element("Date").Value,
                                Emission = (double)day.Element("Energy") * (double)generator.Element("EmissionsRating") * emissionFactor
                            })
                            .OrderByDescending(e => e.Emission)
                            .FirstOrDefault();

                        if (dailyEmissions != null)
                        {
                            maxEmissions.Add(new XElement("Day",
                                new XElement("Name", name),
                                new XElement("Date", dailyEmissions.Date),
                                new XElement("Emission", dailyEmissions.Emission)));
                        }

                        // Calculate actual heat rate for coal generators
                        if (type == "Coal")
                        {
                            double totalHeatInput = (double)generator.Element("TotalHeatInput");
                            double actualNetGeneration = (double)generator.Element("ActualNetGeneration");
                            double actualHeatRate = totalHeatInput / actualNetGeneration;

                            heatRates.Add(new XElement("ActualHeatRate",
                                new XElement("Name", name),
                                new XElement("HeatRate", actualHeatRate)));
                        }
                    }
                }

                outputDoc.Root.Add(totals);
                outputDoc.Root.Add(maxEmissions);
                outputDoc.Root.Add(heatRates);

                // Save the output XML
                string outputFilePath = Path.Combine(outputFolder, Path.GetFileName(inputFilePath).Replace(".xml", "-Result.xml"));
                outputDoc.Save(outputFilePath);

                Console.WriteLine($"File processed successfully: {outputFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing file: {ex.Message}");
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
