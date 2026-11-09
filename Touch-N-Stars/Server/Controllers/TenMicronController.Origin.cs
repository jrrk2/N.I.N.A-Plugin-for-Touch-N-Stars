#if PINS
using EmbedIO;
using NINA.Astrometry;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.Origin;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace TouchNStars.Server.Controllers;

/// <summary>
/// The model builder screens for a Celestron Origin in PI.N.S., with no 10micron plugin: the same endpoints and answers, backed by PI.N.S.'s own pointing
/// model (<see cref="OriginModelBuilder"/>). The Origin has a model built from plate solves rather than a mount's alignment stars, so the 10micron mount
/// settings (GPS, slew rates, horizon limits and so on) are not there, and the "polar" figures of the model are the base's tilt: the direction of the
/// mount's vertical axis, as an equatorial mount's polar axis.
/// </summary>
public partial class TenMicronController
{
    /// <summary>The 10micron plugin is absent and PI.N.S. has an Origin mount: answer from the Origin.</summary>
    private static bool UseOrigin => !IsPluginLoaded() && OriginTelescope.Current != null;

    private static OriginTelescope Origin => OriginTelescope.Current;

    private static OriginModelBuilder Builder => Origin.ModelBuilder;

    // options the screens set that the Origin has no use for: kept so that they read back as set
    private static readonly Dictionary<string, object> OriginOtherOptions = new()
    {
        ["ShowRemovedPoints"] = true,
        ["MinimizeMeridianFlips"] = false,
        ["LogCommands"] = false,
        ["MaxConcurrency"] = 1,
        ["AllowBlindSolves"] = true,
        ["OptimizeDome"] = false,
        ["WestToEast"] = false,
        ["PlateSolveSubframe"] = 100.0,
        ["AlternateDirection"] = false,
        ["DisableRefractionCorrection"] = false,
        ["DecJitter"] = 0.0,
        ["DisableDAT"] = false,
        ["SiderealRaDelta"] = 1.5,
        ["SiderealStartProvider"] = "",
        ["SiderealEndProvider"] = "",
        ["SiderealStartOffset"] = 0,
        ["SiderealEndOffset"] = 0,
    };

    private object OriginFailure(Exception ex, string what)
    {
        Logger.Error($"Origin model builder {what} failed: {ex.Message}");
        return Error(ex.Message, ex is InvalidOperationException or ArgumentException or System.IO.FileNotFoundException ? 400 : 500);
    }

    private object OriginNotSupported() => Error("The Celestron Origin has no such setting", 400);

    private static Dictionary<string, object> Ok(string message = null)
    {
        var result = new Dictionary<string, object> { { "Success", true } };
        if (message != null) result["Message"] = message;
        return result;
    }

    private static object OriginStatus()
    {
        var mount = Origin;
        var connected = mount.Connected;
        var status = mount.AtPark ? "Parked" : mount.Slewing ? "Slewing" : mount.TrackingEnabled ? "Tracking" : "Stopped";
        return new Dictionary<string, object>
        {
            { "Success", true },
            { "PluginLoaded", true },
            { "Connected", connected },
            { "BuildInProgress", Builder.BuildInProgress },
            { "DualAxisTrackingEnabled", false },
            { "RefractionCorrectionEnabled", true },
            { "UnattendedFlipEnabled", false },
            { "TrackingRateArcsecPerSec", connected && mount.TrackingEnabled ? 15.041 : 0.0 },
            { "SlewSettleTimeSeconds", 0.0 },
            { "MeridianLimitDegrees", 0 },
            { "RefractionTemperature", 0.0 },
            { "RefractionPressure", 0.0 },
            { "MountStatus", connected ? status : "" },
            { "ProductName", "Celestron Origin" },
            { "FirmwareVersion", "" },
            { "FirmwareTimestamp", "" },
            { "IPAddress", mount.Host ?? "" },
            { "MACAddress", "" },
        };
    }

    private static object OriginAlignmentModel()
    {
        if (!Origin.Connected || Builder.Summary() is not { } model)
            return new Dictionary<string, object> { { "Success", true }, { "ModelLoaded", false } };
        var toward = model.TiltTowardAzimuth * Math.PI / 180;
        return new Dictionary<string, object>
        {
            { "Success", true },
            { "ModelLoaded", true },
            { "AlignmentStarCount", model.Points },
            { "RMSError", Math.Round(model.RmsArcsec, 2) },
            // the mount's vertical axis, as the 10micron's polar axis: where it points, and how far from the zenith
            { "RightAscensionAltitude", 90 - model.TiltDegrees },
            { "RightAscensionAzimuth", model.TiltTowardAzimuth },
            { "PolarAlignErrorDegrees", model.TiltDegrees },
            { "PAErrorAltitudeDegrees", model.TiltDegrees * Math.Cos(toward) },
            { "PAErrorAzimuthDegrees", model.TiltDegrees * Math.Sin(toward) },
            { "RightAscensionPolarPositionAngleDegrees", model.TiltTowardAzimuth },
            { "OrthogonalityErrorDegrees", model.ConeDegrees },
            { "AzimuthAdjustmentTurns", 0.0 },
            { "AltitudeAdjustmentTurns", 0.0 },
            { "ModelTerms", model.Terms },
            { "AlignmentStars", model.Stars.Select(s => new Dictionary<string, object>
                {
                    { "Altitude", Math.Round(s.Altitude, 2) },
                    { "Azimuth", Math.Round(s.Azimuth, 2) },
                    { "ErrorArcsec", Math.Round(s.ErrorArcsec, 2) },
                    { "ErrorPointRadius", Math.Round(s.ErrorArcsec, 2) },
                }).ToList() },
        };
    }

    private object OriginModelNames()
    {
        try
        {
            return new Dictionary<string, object> { { "Success", true }, { "ModelNames", Origin.Connected ? Builder.ModelNames() : Array.Empty<string>() } };
        }
        catch (Exception ex) { return OriginFailure(ex, "listing models"); }
    }

    private static object OriginBuilderStatus()
    {
        var builder = Builder;
        return new Dictionary<string, object>
        {
            { "Success", true },
            { "PluginLoaded", true },
            { "Connected", Origin.Connected },
            { "BuildInProgress", builder.BuildInProgress },
            { "GoldenSpiralStarCount", builder.Options.GoldenSpiralStarCount },
            { "ModelPoints", builder.Points.Select(p => new Dictionary<string, object>
                {
                    { "ModelIndex", p.Index },
                    { "Azimuth", Math.Round(p.Azimuth, 2) },
                    { "Altitude", Math.Round(p.Altitude, 2) },
                    { "DomeAzimuth", Math.Round(p.Azimuth, 2) },
                    { "DomeAltitude", Math.Round(p.Altitude, 2) },
                    { "ModelPointState", (int)p.State },
                    { "StateLabel", p.State.ToString() },
                }).ToList() },
        };
    }

    private static object OriginBuilderOptions()
    {
        var o = Builder.Options;
        var result = new Dictionary<string, object>
        {
            { "Success", true },
            { "MinPointAltitude", o.MinPointAltitude },
            { "MaxPointAltitude", o.MaxPointAltitude },
            { "MinPointAzimuth", o.MinPointAzimuth },
            { "MaxPointAzimuth", o.MaxPointAzimuth },
            { "MaxPointRMS", o.MaxPointRMS },
            { "BuilderNumRetries", o.BuilderNumRetries },
            { "MaxFailedPoints", o.MaxFailedPoints },
            { "RemoveHighRMSAfterBuild", o.RemoveHighRMSPointsAfterBuild },
            { "GoldenSpiralStarCount", o.GoldenSpiralStarCount },
        };
        lock (OriginOtherOptions)
        {
            foreach (var (key, value) in OriginOtherOptions) result[key] = value;
        }
        return result;
    }

    private async Task<object> OriginSetBuilderOption()
    {
        try
        {
            var body = await HttpContext.GetRequestBodyAsStringAsync().ConfigureAwait(false);
            using var json = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            if (!json.RootElement.TryGetProperty("key", out var k) || !json.RootElement.TryGetProperty("value", out var v))
                return Error("key and value are required", 400);
            var key = k.GetString();
            double Number() => v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetDouble() : double.Parse(v.GetString(), CultureInfo.InvariantCulture);
            bool Flag() => v.ValueKind == System.Text.Json.JsonValueKind.True || (v.ValueKind == System.Text.Json.JsonValueKind.String && bool.Parse(v.GetString()));
            var o = Builder.Options;
            switch (key)
            {
                case "MinPointAltitude": o.MinPointAltitude = (int)Math.Round(Math.Clamp(Number(), 0, 89)); break;
                case "MaxPointAltitude": o.MaxPointAltitude = (int)Math.Round(Math.Clamp(Number(), 1, 90)); break;
                case "MinPointAzimuth": o.MinPointAzimuth = Math.Clamp(Number(), 0, 360); break;
                case "MaxPointAzimuth": o.MaxPointAzimuth = Math.Clamp(Number(), 0, 360); break;
                case "MaxPointRMS": o.MaxPointRMS = Math.Max(0, Number()); break;
                case "BuilderNumRetries": o.BuilderNumRetries = (int)Math.Clamp(Number(), 0, 5); break;
                case "MaxFailedPoints": o.MaxFailedPoints = (int)Math.Max(0, Number()); break;
                case "RemoveHighRMSAfterBuild": o.RemoveHighRMSPointsAfterBuild = Flag(); break;
                case "GoldenSpiralStarCount": o.GoldenSpiralStarCount = (int)Math.Clamp(Number(), 1, 500); break;
                default:
                    lock (OriginOtherOptions)
                    {
                        if (!OriginOtherOptions.ContainsKey(key)) return Error($"Unknown option key: {key}", 400);
                        OriginOtherOptions[key] = v.ValueKind switch
                        {
                            System.Text.Json.JsonValueKind.True => true,
                            System.Text.Json.JsonValueKind.False => false,
                            System.Text.Json.JsonValueKind.Number => v.GetDouble(),
                            _ => v.GetString(),
                        };
                    }
                    break;
            }
            return Ok();
        }
        catch (Exception ex) { return OriginFailure(ex, "setting an option"); }
    }

    private static object OriginResetBuilderOptions()
    {
        Builder.Options.ResetDefaults();
        return OriginBuilderOptions();
    }

    private async Task<object> OriginGenerateGoldenSpiral()
    {
        try
        {
            var body = await HttpContext.GetRequestBodyAsStringAsync().ConfigureAwait(false);
            var count = Builder.Options.GoldenSpiralStarCount;
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var json = System.Text.Json.JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("starCount", out var sc) && sc.TryGetInt32(out var n)) count = n;
            }
            var points = Builder.GenerateGoldenSpiral(count);
            return new Dictionary<string, object> { { "Success", true }, { "Message", $"Generated {points.Count} points" }, { "PointCount", points.Count } };
        }
        catch (Exception ex) { return OriginFailure(ex, "generating points"); }
    }

    private object OriginSiderealPathNotSupported() =>
        Error("A sidereal path is for an equatorial mount's model; use the golden spiral for the Origin", 400);

    private object OriginClearPoints()
    {
        try { Builder.ClearPoints(); return Ok(); }
        catch (Exception ex) { return OriginFailure(ex, "clearing points"); }
    }

    private object OriginBuildModel()
    {
        try { Builder.StartBuild(); return Ok("Model build started"); }
        catch (Exception ex) { return OriginFailure(ex, "starting a build"); }
    }

    private static object OriginCancelBuild()
    {
        Builder.Cancel();
        return Ok();
    }

    private static object OriginStopBuild()
    {
        Builder.Stop();
        return Ok();
    }

    private async Task<object> OriginModelFile(string what, Action<string> act, string done)
    {
        try
        {
            var name = await ReadStringField("name").ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(name)) return Error("Model 'name' is required", 400);
            act(name);
            return Ok(string.Format(done, name.Trim()));
        }
        catch (Exception ex) { return OriginFailure(ex, what); }
    }

    private object OriginDeleteWorstStar()
    {
        try
        {
            var (index, error) = Builder.DeleteWorstPoint();
            return new Dictionary<string, object>
            {
                { "Success", true },
                { "Message", $"Deleted point {index} with error {error:0.##} arcsec" },
                { "DeletedStarIndex", index },
                { "DeletedErrorArcsec", Math.Round(error, 2) },
            };
        }
        catch (Exception ex) { return OriginFailure(ex, "deleting the worst point"); }
    }

    private async Task<object> OriginClearAlignment()
    {
        try
        {
            await Builder.ClearAlignment().ConfigureAwait(false);
            return Ok("Alignment cleared: unpark to plate solve again");
        }
        catch (Exception ex) { return OriginFailure(ex, "clearing the alignment"); }
    }

    private object OriginMountTime()
    {
        if (!Origin.Connected) return Error("Mount is not connected", 503);
        var now = DateTime.Now;
        var lst = AstroUtil.GetLocalSiderealTimeNow(Origin.SiteLongitude);
        var t = TimeSpan.FromHours(((lst % 24) + 24) % 24);
        return new Dictionary<string, object>
        {
            { "Success", true },
            { "LocalTime", now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) },
            { "LocalDate", now.ToString("MM/dd/yy", CultureInfo.InvariantCulture) },
            { "SiderealTime", $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}" },
        };
    }
}
#endif
