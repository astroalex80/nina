#region "copyright"

/*
    Copyright © 2016 - 2024 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Accord.Statistics.Kernels;
using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility.Http;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Equipment.MyWeatherData;
using NINA.Image.ImageData;
using NINA.Profile;
using NINA.WPF.Base.Exceptions;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.Mediator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NINA.WPF.Base.SkySurvey {

    public static class AstrobinUtil {
        



    }
    /// <summary>
    /// Represents a sky survey implementation that retrieves astronomical images and metadata from the AstroBin API.
    /// </summary>
    /// <remarks>This class provides methods to search for and retrieve astronomical images based on specified
    /// criteria such as object name, coordinates, and field of view. It interacts with the AstroBin API to fetch image
    /// metadata and download high-resolution images. The results are filtered and prioritized based on criteria such as
    /// image resolution, field of view, and user ratings.</remarks>
    public class AstroBinSkySurvey : ISkySurvey {

        private const string ApiKey = "408fb57346142a84a697ecfb0b3c688404852f93";
        private const string ApiSecret = "f571ee5d61a40f712005b69529594700b5ebca80";
        private const string ApiUrl = "https://www.astrobin.com/api/v1/image/";

        private async Task<SkySurveyImage> BuildSkySurveyImageAsync(AstroBinImageObject i, string name, CancellationToken ct, IProgress<int> progress) {
            var req = new HttpDownloadImageRequest(i.UrlHd);
            var bmp = await req.Request(ct, progress);
            if (bmp.DpiX != 96) bmp = ConvertBitmapTo96DPI(bmp);
            bmp.Freeze();

            var coords = new Coordinates(i.Ra, i.Dec, Epoch.J2000, Coordinates.RAType.Degrees);
            return new SkySurveyImage {
                Name = name,
                Source = nameof(AstroBinSkySurvey),
                Image = bmp,
                FoVHeight = AstroUtil.ArcsecToArcmin(i.PixScale * i.Height),
                FoVWidth = AstroUtil.ArcsecToArcmin(i.PixScale * i.Width),
                Rotation = i.Rotation,
                Coordinates = coords
            };
        }

        public async Task<BitmapSource> GetThumbnail(string url, CancellationToken ct) {
            var progress = new Progress<int>();
            var req = new HttpDownloadImageRequest(url);
            var bmp = await req.Request(ct, progress);
            if (bmp.DpiX != 96) bmp = ConvertBitmapTo96DPI(bmp);
            bmp.Freeze();

            return bmp;
        }
        public async Task<List<AstroBinImageObject>> GetMetadata(string name, Coordinates coordinates, double fieldOfView, int width, int height,
            CancellationToken ct, IProgress<int> progress) {

            using var httpClient = new HttpClient();

            var byTitleTask = FetchAllByAsync(httpClient, ApiUrl, "title__icontains", name, ApiKey, ApiSecret, 20, ct);
            var byDescTask = FetchAllByAsync(httpClient, ApiUrl, "description__icontains", name, ApiKey, ApiSecret, 20, ct);

            await Task.WhenAll(byTitleTask, byDescTask);

            var allImages = byTitleTask.Result
                .Concat(byDescTask.Result)
                .GroupBy(i => i.Id)
                .Select(g => g.First())
                .ToList();

            //var fovRadius = AstroUtil.ArcminToDegree(fieldOfView) / 2.0;

            var fovRadius = fieldOfView / 2.0;

            var selectedImageObjects = allImages
                .Where(i => i.IsSolved && !i.Animated)
                .Where(i => i.Radius >= fovRadius)
                .Where(i => i.Radius < 15)
                //.Where(i => i.Ra >= coordinates.RADegrees - fovRadius && i.Ra <= coordinates.RADegrees + fovRadius)
                .OrderByDescending(i => i.Likes)
                .ThenByDescending(i => i.Radius)
                .ToList();

            if (selectedImageObjects.Count == 0) selectedImageObjects = allImages
                .Where(i => i.IsSolved && !i.Animated)
                .Where(i => i.Radius < 15)
                //.Where(i => i.Ra >= coordinates.RADegrees - fovRadius && i.Ra <= coordinates.RADegrees + fovRadius)
                .OrderByDescending(i => i.Likes)
                .ThenByDescending(i => i.Radius)
                .ToList();


            if (selectedImageObjects.Count == 0) {
                Notification.ShowInformation("No suitable image found on Astrobin");
            }

            var selection = selectedImageObjects.Take(6).ToList();

            foreach(var abObj in selection) {
                abObj.Thumbnail = await GetThumbnail(abObj.ThumbnailUrl, ct);
            }

            return selection;
            
        }


        //fieldOfView in degrees
        public async Task<SkySurveyImage> GetImage(string name, Coordinates coordinates, double fieldOfView, int width, int height, CancellationToken ct, IProgress<int> progress) {

            using var httpClient = new HttpClient();

            var byTitleTask = FetchAllByAsync(httpClient, ApiUrl, "title__icontains", name, ApiKey, ApiSecret, 20, ct);
            var byDescTask = FetchAllByAsync(httpClient, ApiUrl, "description__icontains", name, ApiKey, ApiSecret, 20, ct);

            await Task.WhenAll(byTitleTask, byDescTask);

            var allImages = byTitleTask.Result
            .Concat(byDescTask.Result)
            .GroupBy(i => i.Id)
            .Select(g => g.First())
            .ToList();

            //var fovRadius = AstroUtil.ArcminToDegree(fieldOfView) / 2.0;

            var fovRadius = fieldOfView / 2.0;

            var selectedImageObjects = allImages
                .Where(i => i.IsSolved && !i.Animated)
                .Where(i => i.Radius >= fovRadius)
                .Where(i => i.Radius < 10)
                //.Where(i => i.Ra >= coordinates.RADegrees - fovRadius && i.Ra <= coordinates.RADegrees + fovRadius)
                .OrderByDescending(i => i.Likes)
                .ThenByDescending(i => i.Radius)
                .ToList();

            if (selectedImageObjects.Count == 0) selectedImageObjects = allImages
                .Where(i => i.IsSolved && !i.Animated)
                .Where(i => i.Radius < 10)
                //.Where(i => i.Ra >= coordinates.RADegrees - fovRadius && i.Ra <= coordinates.RADegrees + fovRadius)
                .OrderByDescending(i => i.Likes)
                .ThenByDescending(i => i.Radius)
                .ToList();


            if (selectedImageObjects.Count == 0) {
               // Notification.ShowInformation("No suitable image found on Astrobin");
                throw new SkySurveyUnavailableException("No suitable image found on Astrobin");
            }

            var selectedImg = selectedImageObjects[0];

            return await BuildSkySurveyImageAsync(selectedImg, name, ct, progress);

            //try {
            //    var request = new HttpDownloadImageRequest(selectedImg.UrlHd);

            //    image = await request.Request(ct, progress);
            //} catch (OperationCanceledException) {
            //    throw;
            //} catch (Exception ex) {
            //    throw new SkySurveyUnavailableException(ex.Message);
            //}

            //if (image.DpiX != 96) {
            //    image = ConvertBitmapTo96DPI(image);
            //}

            //Coordinates imgCoordinates = new Coordinates(selectedImg.Ra, selectedImg.Dec, Epoch.J2000, Coordinates.RAType.Degrees);

            //image.Freeze();
            //return new SkySurveyImage() {
            //    Name = name,
            //    Source = nameof(AstroBinSkySurvey),
            //    Image = image,
            //    FoVHeight = AstroUtil.ArcsecToArcmin(selectedImg.PixScale * selectedImg.Height),
            //    FoVWidth = AstroUtil.ArcsecToArcmin(selectedImg.PixScale * selectedImg.Width),
            //    Rotation = selectedImg.Rotation,
            //    Coordinates = imgCoordinates
            //};
        }

        private async Task<List<AstroBinImageObject>> FetchAllByAsync(
            HttpClient httpClient,
            string baseUrl,
            string field,
            string term,
            string apiKey,
            string apiSecret,
            int limit,
            CancellationToken ct) {
            var results = new List<AstroBinImageObject>();
            int offset = 0;
            int retries = 10;
            //int totalCount = int.MaxValue;
            int totalCount = 60;
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            //string encoded = Uri.EscapeDataString(term);
            string encoded = UrlEncoder.Create().Encode(term);
            //int i = 0;

            while (offset < totalCount) {
                string url = $"{baseUrl}?{field}={encoded}&api_key={apiKey}&api_secret={apiSecret}&format=json&limit={limit}&offset={offset}";
                string json = await httpClient.GetStringAsync(url, ct);

                var data = JsonSerializer.Deserialize<AstroBinResponse>(json, options);

                if (data?.Objects == null || data.Objects.Count == 0)
                    break;

                //if (data.Meta?.TotalCount is int tc && tc > 0)
                //    totalCount = tc;

                //ApplicationUpdate($"Checking {offset}/{totalCount} images");

                results.AddRange(data.Objects);
                offset += limit;
                //i++;
            }

            return results;
        }

        public BitmapSource ConvertBitmapTo96DPI(BitmapSource bitmapImage) {
            double dpi = 96;
            int width = bitmapImage.PixelWidth;
            int height = bitmapImage.PixelHeight;

            int stride = width * bitmapImage.Format.BitsPerPixel;
            byte[] pixelData = new byte[stride * height];
            bitmapImage.CopyPixels(pixelData, stride, 0);

            return BitmapSource.Create(width, height, dpi, dpi, bitmapImage.Format, null, pixelData, stride);
        }

        //private void ApplicationUpdate(string msg) {
        //    ApplicationStatus applicationStatus = new ApplicationStatus {
        //        Source = "Astrobin Sky Survey",
        //        Status = msg
        //    };
        //    ApplicationStatusMediator applicationStatusMediator = new();
        //    applicationStatusMediator.StatusUpdate(applicationStatus);
        //}

    }

    internal class AstroBinResponse {
        public AstroBinMeta Meta { get; set; }
        public List<AstroBinImageObject> Objects { get; set; }
    }

    internal class AstroBinMeta {
        [JsonPropertyName("Limit")] public int Limit { get; set; }

        [JsonPropertyName("Offset")] public int Offset { get; set; }

        [JsonPropertyName("Total_Count")] public int TotalCount { get; set; }
    }

    public class AstroBinImageObject {

        private const string AstrobinUrl = "https://www.astrobin.com/";
        private const string AstrobinUserUrl = "https://www.astrobin.com/users/";

        [JsonPropertyName("id")]
        public int Id { get; set; }

        private string _hash;

        [JsonPropertyName("hash")]
        public string Hash {
            get => _hash;
            set {
                _hash = value;
                ImageUrl = $"{AstrobinUrl}{value}";
            }
        }

        [JsonIgnore]
        public string ImageUrl { get; private set; }

        [JsonPropertyName("url_gallery")]
        public string ThumbnailUrl { get; set; }

        private string _user;

        [JsonPropertyName("user")]
        public string User {
            get => _user;
            set {
                _user = value;
                UserUrl = $"{AstrobinUserUrl}{value}/";
            }
        }

        [JsonIgnore]
        public string UserUrl { get; private set; }

        [JsonPropertyName("likes")]
        public int Likes { get; set; }

        [JsonPropertyName("url_real")]
        public string UrlReal { get; set; }

        [JsonPropertyName("url_hd")]
        public string UrlHd { get; set; }

        [JsonPropertyName("is_solved")]
        public bool IsSolved { get; set; }

        [JsonPropertyName("animated")]
        public bool Animated { get; set; }

        [JsonPropertyName("h")]
        public int Height { get; set; }

        [JsonPropertyName("w")]
        public int Width { get; set; }

        [JsonPropertyName("pixscale")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public double? PixScaleRaw { get; set; }

        [JsonIgnore]
        public double PixScale => PixScaleRaw ?? 0.0;

        [JsonPropertyName("radius")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public double? RadiusRaw { get; set; }

        [JsonIgnore]
        public double Radius => RadiusRaw ?? 0.0;


        [JsonPropertyName("orientation")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public double? RotationRaw { get; set; }

        [JsonIgnore]
        public double Rotation => RotationRaw ?? 0.0;


        [JsonPropertyName("ra")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public double? RaRaw { get; set; }

        [JsonIgnore]
        public double Ra => RaRaw ?? 0.0;


        [JsonPropertyName("dec")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public double? DecRaw { get; set; }

        [JsonIgnore]
        public double Dec => DecRaw ?? 0.0;

        [JsonIgnore]
        public BitmapSource Thumbnail { get; internal set; }
    }
}