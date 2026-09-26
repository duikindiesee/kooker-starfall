using System;
using UnityEngine;

namespace CityLife.World
{
    public enum MicroClimateZone
    {
        RefugeCavern,
        HighMesa,
        CoastalDelta,
        SpringOasis,
        CanyonValley
    }

    public enum ExposureRating
    {
        Sheltered,
        Temperate,
        Moderate,
        Maritime,
        Harsh
    }

    [Serializable]
    public struct MicroWeatherReport
    {
        public MicroClimateZone zone;
        public ExposureRating exposure;
        public string zoneName;
        public float localTemperatureC;
        public float windMultiplier;
        public float rainMultiplier;
        public string statusSummary;
    }

    /// <summary>
    /// Evaluates dynamic micro-climates across the canyon regions (Refuge Cavern,
    /// High Mesa, Coastal Delta, Freshwater Oasis, Canyon Floor).
    /// </summary>
    public static class CanyonMicroWeather
    {
        public static MicroWeatherReport Sample(Vector3 worldPos, float outdoorTemp = 22f, float outdoorRain = 0f, float outdoorWind = 5f)
        {
            var report = new MicroWeatherReport();

            if (worldPos.x < -100f && worldPos.z > 60f)
            {
                // 1. Refuge Cavern & Overhang (deep thermal buffer, zero precipitation)
                report.zone = MicroClimateZone.RefugeCavern;
                report.exposure = ExposureRating.Sheltered;
                report.zoneName = "Refuge Cavern Buffer";
                report.localTemperatureC = 19.0f;
                report.windMultiplier = 0.10f;
                report.rainMultiplier = 0.0f;
                report.statusSummary = "Cavern shade buffer · Stored hearth warmth · Zero precipitation";
            }
            else if (worldPos.y > 18f)
            {
                // 2. High Mesa Plateau & Ridge (scorching solar heating, piercing high wind)
                report.zone = MicroClimateZone.HighMesa;
                report.exposure = ExposureRating.Harsh;
                report.zoneName = "High Mesa Bluffs";
                report.localTemperatureC = outdoorTemp + 5.5f;
                report.windMultiplier = 1.85f;
                report.rainMultiplier = 1.0f;
                report.statusSummary = "Direct solar exposure · Strong updrafts · High exhaustion";
            }
            else if (worldPos.z > 50f)
            {
                // 3. Coastal Shallows & River Delta (maritime sea breeze, high humidity, tidal spray)
                report.zone = MicroClimateZone.CoastalDelta;
                report.exposure = ExposureRating.Maritime;
                report.zoneName = "Coastal Sea Breeze";
                report.localTemperatureC = outdoorTemp - 3.5f;
                report.windMultiplier = 1.15f;
                report.rainMultiplier = Mathf.Max(outdoorRain, 0.35f);
                report.statusSummary = "Cool maritime draft · Tidal moisture · Salt mist";
            }
            else if (worldPos.x > 90f && worldPos.x < 145f && worldPos.z > -85f && worldPos.z < -35f)
            {
                // 4. Freshwater Spring Oasis (tempered humidity, shaded canyon basin)
                report.zone = MicroClimateZone.SpringOasis;
                report.exposure = ExposureRating.Temperate;
                report.zoneName = "Spring Oasis Grove";
                report.localTemperatureC = 20.5f;
                report.windMultiplier = 0.35f;
                report.rainMultiplier = 0.20f;
                report.statusSummary = "Lush riparian humidity · Mild canyon shade · Low wind";
            }
            else
            {
                // 5. Sunlit Canyon Valley Floor
                report.zone = MicroClimateZone.CanyonValley;
                report.exposure = ExposureRating.Moderate;
                report.zoneName = "Canyon Terrace";
                report.localTemperatureC = outdoorTemp;
                report.windMultiplier = 0.70f;
                report.rainMultiplier = outdoorRain;
                report.statusSummary = "Sunlit stone terrace · Gentle valley draft · Moderate exposure";
            }

            return report;
        }

        public static string GetHudWeatherLine(Vector3 worldPos)
        {
            var rep = Sample(worldPos);
            string tideInfo = CoastalTide.GetTidePhaseName(CoastalTide.ActiveTime);
            return $"CLIMATE: [{rep.zoneName} · {rep.localTemperatureC:0.0}°C · {rep.exposure}] · TIDE: [{tideInfo}]";
        }

        public static bool VerifyMicroWeather(out string receipt)
        {
            var cavern = Sample(new Vector3(-140f, 6f, 90f));
            if (cavern.zone != MicroClimateZone.RefugeCavern || cavern.exposure != ExposureRating.Sheltered || cavern.rainMultiplier != 0f)
            {
                receipt = "Refuge Cavern micro-climate failed shelter assertion.";
                return false;
            }

            var mesa = Sample(new Vector3(50f, 25f, -20f));
            if (mesa.zone != MicroClimateZone.HighMesa || mesa.exposure != ExposureRating.Harsh || mesa.windMultiplier <= 1.0f)
            {
                receipt = "High Mesa micro-climate failed solar/wind amplification assertion.";
                return false;
            }

            var coast = Sample(new Vector3(0f, -2f, 100f));
            if (coast.zone != MicroClimateZone.CoastalDelta || coast.exposure != ExposureRating.Maritime)
            {
                receipt = "Coastal Delta micro-climate failed maritime cooling assertion.";
                return false;
            }

            var oasis = Sample(new Vector3(120f, 2f, -60f));
            if (oasis.zone != MicroClimateZone.SpringOasis || oasis.exposure != ExposureRating.Temperate)
            {
                receipt = "Freshwater Oasis micro-climate failed temperate oasis assertion.";
                return false;
            }

            receipt = "Micro-climates verified across Refuge Cavern, High Mesa, Coastal Delta, and Oasis zones.";
            return true;
        }
    }
}
