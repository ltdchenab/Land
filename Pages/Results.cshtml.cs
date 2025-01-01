using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RestSharp;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Mvc;
using OpenCvSharp;

namespace HMDH.Pages
{
    public class ResultsModel : PageModel
{
    private readonly string _apiKey = "AIzaSyAMCuhMSeDuZRdvscIWAwYk9KCrbGJ6hwg";
    private readonly IWebHostEnvironment _environment;

    public ResultsModel(IWebHostEnvironment environment)
    {
        _environment = environment;
    }
    public async Task<IActionResult> OnPostAsync(string Address, int Radius)
    {
        if (!IsInternetAvailable())
        {
            // Redirect to the error page if there is no internet
            return RedirectToPage("/Error");
        }

        // Existing logic for fetching distressed properties
        (double lat, double lng) = await GetCoordinatesAsync(Address);
        DistressedProperties = await GetNearbyAddressesAsync(lat, lng, Radius);

        if (DistressedProperties.Count > 0)
        {
            SaveToCsv(DistressedProperties);
        }

        IsAnalysisComplete = DistressedProperties.Count > 0;
        return Page();
    }

    private bool IsInternetAvailable()
    {
        try
        {
            using (var client = new System.Net.WebClient())
            {
                using (client.OpenRead("http://clients3.google.com/generate_204"))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }
    }
    public bool IsAnalysisComplete { get; set; }
    public List<DistressedProperty> DistressedProperties { get; set; }

    private async Task<(double, double)> GetCoordinatesAsync(string address)
    {
        var client = new RestClient($"https://maps.googleapis.com/maps/api/geocode/json?address={Uri.EscapeDataString(address)}&key={_apiKey}");
        var response = await client.ExecuteAsync(new RestRequest());

        if (response.IsSuccessful)
        {
            var json = JObject.Parse(response.Content);
            var location = json["results"]?[0]?["geometry"]?["location"];
            if (location != null)
            {
                double lat = location["lat"].Value<double>();
                double lng = location["lng"].Value<double>();
                return (lat, lng);
            }
        }
        throw new System.Exception("Failed to fetch coordinates.");
    }

    private async Task<List<DistressedProperty>> GetNearbyAddressesAsync(double lat, double lng, int radius)
    {
        var client = new RestClient($"https://maps.googleapis.com/maps/api/place/nearbysearch/json?location={lat},{lng}&radius={radius}&key={_apiKey}");
        var response = await client.ExecuteAsync(new RestRequest());
        var properties = new List<DistressedProperty>();

        if (response.IsSuccessful)
        {
            var json = JObject.Parse(response.Content);
            var results = json["results"];
            if (results != null)
            {
                foreach (var place in results)
                {
                    var placeId = place["place_id"]?.ToString();
                    if (!string.IsNullOrEmpty(placeId))
                    {
                        // Get detailed address using Place Details API
                        string detailedAddress = await GetPlaceDetailsAsync(placeId);

                        // Check if the address already exists in the list (avoid duplicates)
                        if (!properties.Any(p => p.Address == detailedAddress))
                        {
                            var location = place["geometry"]?["location"];
                            if (!string.IsNullOrEmpty(detailedAddress) && location != null)
                            {
                                double propertyLat = location["lat"].Value<double>();
                                double propertyLng = location["lng"].Value<double>();

                                // Fetch and analyze street view image
                                string imageUrl = await GetStreetViewImageAsync(propertyLat, propertyLng);
                                List<string> analysisResults = AnalyzeImageForDistress(imageUrl);

                                properties.Add(new DistressedProperty
                                {
                                    Address = detailedAddress,
                                    DistressNotes = analysisResults,
                                    ImageUrl = imageUrl
                                });
                            }
                        }
                    }
                }
            }
        }
        return properties;
    }

    private async Task<string> GetPlaceDetailsAsync(string placeId)
    {
        var client = new RestClient($"https://maps.googleapis.com/maps/api/place/details/json?place_id={placeId}&fields=formatted_address&key={_apiKey}");
        var response = await client.ExecuteAsync(new RestRequest());

        if (response.IsSuccessful)
        {
            var json = JObject.Parse(response.Content);
            var formattedAddress = json["result"]?["formatted_address"]?.ToString();
            return formattedAddress ?? "Address details not available";
        }
        return "Address details not available";
    }

    private async Task<string> GetStreetViewImageAsync(double lat, double lng)
    {
        string imageUrl = $"https://maps.googleapis.com/maps/api/streetview?size=640x640&location={lat},{lng}&key={_apiKey}";
        return imageUrl;
    }

    private List<string> AnalyzeImageForDistress(string imageUrl)
    {
        // Download the image
        using (var client = new System.Net.WebClient())
        {
            byte[] imageBytes = client.DownloadData(imageUrl);
            using (var ms = new MemoryStream(imageBytes))
            {
                Mat image = Mat.FromStream(ms, ImreadModes.Color);

                // Perform distress analysis using OpenCV
                List<string> distressNotes = new List<string>();

                // Example analysis: detecting edges (you can implement more complex analysis)
                Mat edges = new Mat();
                Cv2.Canny(image, edges, 100, 200);

                // Check for specific distress markers (this is just a placeholder logic)
                if (DetectTallGrass(image))
                {
                    distressNotes.Add("Tall grass detected, needs to be cut");
                }
                if (DetectBrokenWindows(image))
                {
                    distressNotes.Add("Broken windows detected, needs repair");
                }
                if (DetectTrashAccumulation(image))
                {
                    distressNotes.Add("Trash accumulation detected, needs cleaning");
                }
                if (DetectFallenBranches(image))
                {
                    distressNotes.Add("Fallen branches detected, needs removal");
                }
                if (DetectDamagedRoof(image))
                {
                    distressNotes.Add("Damaged roof detected, needs repair");
                }

                return distressNotes.Any() ? distressNotes : new List<string> { "No visible distress markers found" };
            }
        }
    }

    private bool DetectTallGrass(Mat image)
    {
        // Implement tall grass detection logic using OpenCV
        // Placeholder: Use color detection for a simple example
        Mat hsvImage = new Mat();
        Cv2.CvtColor(image, hsvImage, ColorConversionCodes.BGR2HSV);
        Mat mask = new Mat();
        Cv2.InRange(hsvImage, new Scalar(35, 100, 100), new Scalar(85, 255, 255), mask); // Detect green colors
        double greenRatio = Cv2.CountNonZero(mask) / (double)(image.Rows * image.Cols);
        return greenRatio > 0.1; // Example threshold
    }

    private bool DetectBrokenWindows(Mat image)
    {
        // Implement broken windows detection logic using OpenCV
        // Placeholder: Detect large changes in edge density
        Mat grayImage = new Mat();
        Cv2.CvtColor(image, grayImage, ColorConversionCodes.BGR2GRAY);
        Mat edges = new Mat();
        Cv2.Canny(grayImage, edges, 100, 200);
        double edgeDensity = Cv2.CountNonZero(edges) / (double)(image.Rows * image.Cols);
        return edgeDensity > 0.05; // Example threshold
    }

    private bool DetectTrashAccumulation(Mat image)
    {
        // Implement trash accumulation detection logic using OpenCV
        // Placeholder: Detect clusters of small objects
        Mat grayImage = new Mat();
        Cv2.CvtColor(image, grayImage, ColorConversionCodes.BGR2GRAY);
        Mat binary = new Mat();
        Cv2.Threshold(grayImage, binary, 127, 255, ThresholdTypes.Binary);
        Point[][] contours;
        HierarchyIndex[] hierarchy;
        Cv2.FindContours(binary, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        int smallObjectCount = contours.Count(c => Cv2.ContourArea(c) < 1000); // Example threshold for small objects
        return smallObjectCount > 10; // Example threshold
    }

    private bool DetectFallenBranches(Mat image)
    {
        // Implement fallen branches detection logic using OpenCV
        // Placeholder: Detect elongated objects
        Mat grayImage = new Mat();
        Cv2.CvtColor(image, grayImage, ColorConversionCodes.BGR2GRAY);
        Mat binary = new Mat();
        Cv2.Threshold(grayImage, binary, 127, 255, ThresholdTypes.Binary);
        Point[][] contours;
        HierarchyIndex[] hierarchy;
        Cv2.FindContours(binary, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        int elongatedObjectCount = contours.Count(c => Cv2.ArcLength(c, true) > 100 && Cv2.ContourArea(c) / Cv2.ArcLength(c, true) > 1); // Example threshold for elongated objects
        return elongatedObjectCount > 5; // Example threshold
    }

    private bool DetectDamagedRoof(Mat image)
    {
        // Implement damaged roof detection logic using OpenCV
        // Placeholder: Detect large areas of specific colors (e.g., missing shingles)
        Mat hsvImage = new Mat();
        Cv2.CvtColor(image, hsvImage, ColorConversionCodes.BGR2HSV);
        Mat mask = new Mat();
        Cv2.InRange(hsvImage, new Scalar(0, 0, 0), new Scalar(180, 255, 50), mask); // Detect dark colors (e.g., missing shingles)
        double darkRatio = Cv2.CountNonZero(mask) / (double)(image.Rows * image.Cols);
        return darkRatio > 0.05; // Example threshold
    }

    private void SaveToCsv(List<DistressedProperty> properties)
    {
        var csvPath = Path.Combine(_environment.WebRootPath, "csv", "distressed_properties.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(csvPath));

        var csvBuilder = new StringBuilder();
        csvBuilder.AppendLine("Id,Address,DistressNotes,ImageUrl");

        for (int i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            string notes = string.Join("; ", property.DistressNotes);
            csvBuilder.AppendLine($"{i + 1},{property.Address},{notes},{property.ImageUrl}");
        }

        System.IO.File.WriteAllText(csvPath, csvBuilder.ToString());
    }

    public class DistressedProperty
    {
        public int Id { get; set; }
        public string Address { get; set; }
        public List<string>? DistressNotes { get; set; }
        public required string ImageUrl { get; set; }
    }
}
    }
