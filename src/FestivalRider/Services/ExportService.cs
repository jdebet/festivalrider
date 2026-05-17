using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using FestivalRider.Models;
using Microsoft.Extensions.Logging;

namespace FestivalRider.Services;

public class ExportService : IExportService
{
    private readonly ILogger<ExportService> _logger;
    private readonly IBandService _bands;

    private static readonly CsvConfiguration Config = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        NewLine = "\n",
    };

    public ExportService(ILogger<ExportService> logger, IBandService bands)
    {
        _logger = logger;
        _bands = bands;
    }

    private sealed class Row
    {
        public string Section { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Index { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    // ---- low-level CSV ----

    private static string Write(IEnumerable<Row> rows)
    {
        using var sw = new StringWriter { NewLine = "\n" };
        using (var csv = new CsvWriter(sw, Config))
        {
            csv.WriteHeader<Row>();
            csv.NextRecord();
            foreach (var r in rows)
            {
                csv.WriteRecord(r);
                csv.NextRecord();
            }
        }
        return sw.ToString();
    }

    private static List<Row> Read(string csv)
    {
        using var sr = new StringReader(csv);
        using var rd = new CsvReader(sr, Config);
        return rd.GetRecords<Row>().ToList();
    }

    // ---- helpers ----

    private static string Inv(decimal d) => d.ToString(CultureInfo.InvariantCulture);
    private static string Inv(decimal? d) => d?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    private static string Inv(int i) => i.ToString(CultureInfo.InvariantCulture);
    private static string IsoDateTime(DateTimeOffset d) => d.ToString("o", CultureInfo.InvariantCulture);
    private static string IsoDate(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

    private static int ParseInt(string s, int fallback = 0) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static decimal ParseDec(string s, decimal fallback = 0m) =>
        decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static decimal? ParseDecNullable(string s) =>
        string.IsNullOrEmpty(s) ? null :
            decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static bool ParseBool(string s) =>
        bool.TryParse(s, out var v) && v;

    private static T ParseEnum<T>(string s, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(s, ignoreCase: true, out var v) ? v : fallback;

    private static Guid ParseGuid(string s, Guid fallback) =>
        Guid.TryParse(s, out var v) ? v : fallback;

    private static DateTimeOffset ParseDateTimeOffset(string s, DateTimeOffset fallback) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var v)
            ? v : fallback;

    private static DateOnly ParseDateOnly(string s, DateOnly fallback) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var v)
            ? v : fallback;

    public string SanitizeFilename(string name)
    {
        var safe = new string((name ?? "untitled").Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "untitled" : safe.Trim('-');
    }

    private sealed class RowIndex
    {
        public Dictionary<string, List<Row>> BySection { get; } = new();
        public RowIndex(IEnumerable<Row> rows)
        {
            foreach (var r in rows)
            {
                if (!BySection.TryGetValue(r.Section, out var list))
                    BySection[r.Section] = list = new List<Row>();
                list.Add(r);
            }
        }
        public string Scalar(string section, string key)
            => BySection.TryGetValue(section, out var list)
                ? list.FirstOrDefault(r => r.Key == key && string.IsNullOrEmpty(r.Index))?.Value ?? string.Empty
                : string.Empty;
        public IEnumerable<IGrouping<string, Row>> Indexed(string section)
            => BySection.TryGetValue(section, out var list)
                ? list.Where(r => !string.IsNullOrEmpty(r.Index))
                      .GroupBy(r => r.Index)
                      .OrderBy(g => ParseInt(g.Key))
                : Enumerable.Empty<IGrouping<string, Row>>();
        public IEnumerable<Row> Repeated(string section, string key)
            => BySection.TryGetValue(section, out var list)
                ? list.Where(r => r.Key == key)
                      .OrderBy(r => ParseInt(r.Index))
                : Enumerable.Empty<Row>();
    }

    private static string GroupVal(IGrouping<string, Row> g, string key)
        => g.FirstOrDefault(r => r.Key == key)?.Value ?? string.Empty;

    // ---- Band CSV ----

    public string ExportBandCsv(Band b)
    {
        var rows = new List<Row>();
        void S(string section, string key, string value, string index = "")
            => rows.Add(new Row { Section = section, Key = key, Value = value, Index = index });

        // Band
        S("Band", "Id", b.Id.ToString());
        S("Band", "Name", b.Name);
        S("Band", "Notes", b.Notes ?? string.Empty);
        S("Band", "CreatedAt", IsoDateTime(b.CreatedAt));
        S("Band", "UpdatedAt", IsoDateTime(b.UpdatedAt));

        // Contact
        for (var i = 0; i < b.Contacts.Count; i++)
        {
            var c = b.Contacts[i]; var idx = Inv(i);
            S("Contact", "Role", c.Role.ToString(), idx);
            S("Contact", "Name", c.Name, idx);
            S("Contact", "Email", c.Email ?? string.Empty, idx);
            S("Contact", "Phone", c.Phone ?? string.Empty, idx);
        }

        // TravelParty
        for (var i = 0; i < b.TravelParty.Members.Count; i++)
        {
            var p = b.TravelParty.Members[i]; var idx = Inv(i);
            S("TravelParty", "Type", p.Type.ToString(), idx);
            S("TravelParty", "Role", p.Role, idx);
            S("TravelParty", "Name", p.Name, idx);
        }

        var t = b.Rider.Tech;

        // Tech.Cable
        for (var i = 0; i < t.Cables.Count; i++)
        {
            var c = t.Cables[i]; var idx = Inv(i);
            S("Tech.Cable", "Source", c.Source.ToString(), idx);
            if (c.Source == CablePoint.Other)
                S("Tech.Cable", "SourceOther", c.SourceOther ?? string.Empty, idx);
            S("Tech.Cable", "Target", c.Target.ToString(), idx);
            if (c.Target == CablePoint.Other)
                S("Tech.Cable", "TargetOther", c.TargetOther ?? string.Empty, idx);
            S("Tech.Cable", "Type", c.Type.ToString(), idx);
            if (c.Type == CableType.Other)
                S("Tech.Cable", "TypeOther", c.TypeOther ?? string.Empty, idx);
            S("Tech.Cable", "CategoryOrSpec", c.CategoryOrSpec ?? string.Empty, idx);
            S("Tech.Cable", "MinLengthMeters", Inv(c.MinLengthMeters), idx);
            S("Tech.Cable", "MaxLengthMeters", Inv(c.MaxLengthMeters), idx);
            S("Tech.Cable", "Provider", c.Provider.ToString(), idx);
        }

        // Tech.Lighting
        var l = t.Lighting;
        S("Tech.Lighting", "OwnConsoleModel", l.OwnConsoleModel ?? string.Empty);
        S("Tech.Lighting", "BackdropWidthMeters", Inv(l.BackdropWidthMeters));
        S("Tech.Lighting", "BackdropHeightMeters", Inv(l.BackdropHeightMeters));

        // Tech.LightingMachine
        for (var i = 0; i < l.FloorMachines.Count; i++)
        {
            var m = l.FloorMachines[i]; var idx = Inv(i);
            S("Tech.LightingMachine", "Name", m.Name, idx);
            S("Tech.LightingMachine", "Location", m.Location ?? string.Empty, idx);
            S("Tech.LightingMachine", "Count", Inv(m.Count), idx);
        }

        // Tech.Power
        var pw = t.Power;
        S("Tech.Power", "Amperage", pw.Amperage.ToString());
        S("Tech.Power", "Phase", pw.Phase.ToString());
        S("Tech.Power", "AdapterNotes", pw.AdapterNotes ?? string.Empty);

        // Tech.Foh
        var f = t.Foh;
        S("Tech.Foh", "OwnConsoleModel", f.OwnConsoleModel ?? string.Empty);
        S("Tech.Foh", "OutputProtocol", f.OutputProtocol.ToString());
        if (f.OutputProtocol == OutputProtocol.Other)
            S("Tech.Foh", "OutputProtocolOther", f.OutputProtocolOther ?? string.Empty);
        S("Tech.Foh", "OutputLocation", f.OutputLocation.ToString());
        if (f.OutputLocation == OutputLocation.Other)
            S("Tech.Foh", "OutputLocationOther", f.OutputLocationOther ?? string.Empty);
        S("Tech.Foh", "OutputNotes", f.OutputNotes ?? string.Empty);
        S("Tech.Foh", "AdditionalHardware", f.AdditionalHardware ?? string.Empty);
        S("Tech.Foh", "StageToFohSendCount", Inv(f.StageToFohSendCount));
        S("Tech.Foh", "StageToFohRoundTripCount", Inv(f.StageToFohRoundTripCount));
        S("Tech.Foh", "FootprintWidthMeters", Inv(f.FootprintWidthMeters));
        S("Tech.Foh", "FootprintLengthMeters", Inv(f.FootprintLengthMeters));
        S("Tech.Foh", "Notes", f.Notes ?? string.Empty);

        // Tech.Monitors
        var mo = t.Monitors;
        S("Tech.Monitors", "SourceMode", mo.SourceMode.ToString());
        S("Tech.Monitors", "OwnConsoleModel", mo.OwnConsoleModel ?? string.Empty);
        S("Tech.Monitors", "OwnConsoleLocation", mo.OwnConsoleLocation.ToString());
        S("Tech.Monitors", "Notes", mo.Notes ?? string.Empty);

        // Tech.MonitorWedge
        for (var i = 0; i < mo.Wedges.Count; i++)
        {
            var w = mo.Wedges[i]; var idx = Inv(i);
            S("Tech.MonitorWedge", "Where", w.Where, idx);
            S("Tech.MonitorWedge", "DualLinked", w.DualLinked.ToString(), idx);
            S("Tech.MonitorWedge", "Stereo", w.Stereo.ToString(), idx);
            S("Tech.MonitorWedge", "DrumFill", w.DrumFill.ToString(), idx);
        }

        // Tech.InEar
        for (var i = 0; i < mo.InEars.Count; i++)
        {
            var e = mo.InEars[i]; var idx = Inv(i);
            S("Tech.InEar", "Where", e.Where, idx);
            S("Tech.InEar", "IsWireless", e.IsWireless.ToString(), idx);
            S("Tech.InEar", "Provider", e.Provider.ToString(), idx);
            S("Tech.InEar", "Model", e.Model ?? string.Empty, idx);
            S("Tech.InEar", "Frequency", e.Frequency ?? string.Empty, idx);
        }

        // Tech.Stage
        var st = t.Stage;
        S("Tech.Stage", "BringsOwnMics", st.BringsOwnMics.ToString());
        S("Tech.Stage", "Notes", st.Notes ?? string.Empty);

        // Tech.Riser
        for (var i = 0; i < st.Risers.Count; i++)
        {
            var r = st.Risers[i]; var idx = Inv(i);
            S("Tech.Riser", "Where", r.Where, idx);
            S("Tech.Riser", "WidthMeters", Inv(r.WidthMeters), idx);
            S("Tech.Riser", "LengthMeters", Inv(r.LengthMeters), idx);
            S("Tech.Riser", "HeightCm", Inv(r.HeightCm), idx);
        }

        // Tech.OtherRiser
        for (var i = 0; i < st.OtherRisers.Count; i++)
        {
            var o = st.OtherRisers[i]; var idx = Inv(i);
            S("Tech.OtherRiser", "Where", o.Where, idx);
            S("Tech.OtherRiser", "Type", o.Type.ToString(), idx);
            if (o.Type == OtherRiserType.Custom)
                S("Tech.OtherRiser", "Description", o.Description ?? string.Empty, idx);
        }

        // Tech.WirelessMic
        for (var i = 0; i < st.WirelessMics.Count; i++)
        {
            var w = st.WirelessMics[i]; var idx = Inv(i);
            S("Tech.WirelessMic", "Where", w.Where, idx);
            S("Tech.WirelessMic", "Count", Inv(w.Count), idx);
            S("Tech.WirelessMic", "Provider", w.Provider.ToString(), idx);
            S("Tech.WirelessMic", "Model", w.Model ?? string.Empty, idx);
            S("Tech.WirelessMic", "Frequency", w.Frequency ?? string.Empty, idx);
        }

        // Tech (root scalar)
        S("Tech", "Notes", t.Notes ?? string.Empty);

        // Hospitality
        var h = b.Rider.Hospitality;
        S("Hospitality", "DressingRoomNotes", h.DressingRoomNotes ?? string.Empty);
        S("Hospitality", "CateringNotes", h.CateringNotes ?? string.Empty);
        S("Hospitality", "DietaryRestrictions", h.DietaryRestrictions ?? string.Empty);
        S("Hospitality", "TowelCount", Inv(h.TowelCount));
        S("Hospitality", "ParkingSpaces", Inv(h.ParkingSpaces));
        S("Hospitality", "Accommodations", h.Accommodations ?? string.Empty);
        for (var i = 0; i < h.DrinksRequests.Count; i++)
            S("Hospitality", "Drink", h.DrinksRequests[i], Inv(i));

        return Write(rows);
    }

    public Band ImportBandCsv(string csv)
    {
        var idx = new RowIndex(Read(csv));
        var band = new Band();

        // Band
        band.Id = ParseGuid(idx.Scalar("Band", "Id"), Guid.NewGuid());
        band.Name = idx.Scalar("Band", "Name");
        band.Notes = NullIfEmpty(idx.Scalar("Band", "Notes"));
        band.CreatedAt = ParseDateTimeOffset(idx.Scalar("Band", "CreatedAt"), DateTimeOffset.UtcNow);
        band.UpdatedAt = ParseDateTimeOffset(idx.Scalar("Band", "UpdatedAt"), DateTimeOffset.UtcNow);

        // Contact
        foreach (var g in idx.Indexed("Contact"))
        {
            band.Contacts.Add(new Contact
            {
                Role = ParseEnum(GroupVal(g, "Role"), ContactRole.Other),
                Name = GroupVal(g, "Name"),
                Email = NullIfEmpty(GroupVal(g, "Email")),
                Phone = NullIfEmpty(GroupVal(g, "Phone")),
            });
        }

        // TravelParty
        foreach (var g in idx.Indexed("TravelParty"))
        {
            band.TravelParty.Members.Add(new Party
            {
                Type = ParseEnum(GroupVal(g, "Type"), PartyType.BandMember),
                Role = GroupVal(g, "Role"),
                Name = GroupVal(g, "Name"),
            });
        }

        var t = band.Rider.Tech;

        // Tech.Cable
        foreach (var g in idx.Indexed("Tech.Cable"))
        {
            t.Cables.Add(new Cable
            {
                Source = ParseEnum(GroupVal(g, "Source"), CablePoint.SoundFoh),
                SourceOther = NullIfEmpty(GroupVal(g, "SourceOther")),
                Target = ParseEnum(GroupVal(g, "Target"), CablePoint.SoundFoh),
                TargetOther = NullIfEmpty(GroupVal(g, "TargetOther")),
                Type = ParseEnum(GroupVal(g, "Type"), CableType.RJ45),
                TypeOther = NullIfEmpty(GroupVal(g, "TypeOther")),
                CategoryOrSpec = NullIfEmpty(GroupVal(g, "CategoryOrSpec")),
                MinLengthMeters = ParseDecNullable(GroupVal(g, "MinLengthMeters")),
                MaxLengthMeters = ParseDecNullable(GroupVal(g, "MaxLengthMeters")),
                Provider = ParseEnum(GroupVal(g, "Provider"), CableProvider.Venue),
            });
        }

        // Tech.Lighting
        t.Lighting.OwnConsoleModel = NullIfEmpty(idx.Scalar("Tech.Lighting", "OwnConsoleModel"));
        t.Lighting.BackdropWidthMeters = ParseDecNullable(idx.Scalar("Tech.Lighting", "BackdropWidthMeters"));
        t.Lighting.BackdropHeightMeters = ParseDecNullable(idx.Scalar("Tech.Lighting", "BackdropHeightMeters"));

        foreach (var g in idx.Indexed("Tech.LightingMachine"))
        {
            t.Lighting.FloorMachines.Add(new LightingMachine
            {
                Name = GroupVal(g, "Name"),
                Location = NullIfEmpty(GroupVal(g, "Location")),
                Count = ParseInt(GroupVal(g, "Count")),
            });
        }

        // Tech.Power
        t.Power.Amperage = ParseEnum(idx.Scalar("Tech.Power", "Amperage"), PowerAmperage._16_A);
        t.Power.Phase = ParseEnum(idx.Scalar("Tech.Power", "Phase"), PowerPhase.SinglePhase);
        t.Power.AdapterNotes = NullIfEmpty(idx.Scalar("Tech.Power", "AdapterNotes"));

        // Tech.Foh
        var f = t.Foh;
        f.OwnConsoleModel = NullIfEmpty(idx.Scalar("Tech.Foh", "OwnConsoleModel"));
        f.OutputProtocol = ParseEnum(idx.Scalar("Tech.Foh", "OutputProtocol"), OutputProtocol.Aes);
        f.OutputProtocolOther = NullIfEmpty(idx.Scalar("Tech.Foh", "OutputProtocolOther"));
        f.OutputLocation = ParseEnum(idx.Scalar("Tech.Foh", "OutputLocation"), OutputLocation.Foh);
        f.OutputLocationOther = NullIfEmpty(idx.Scalar("Tech.Foh", "OutputLocationOther"));
        f.OutputNotes = NullIfEmpty(idx.Scalar("Tech.Foh", "OutputNotes"));
        f.AdditionalHardware = NullIfEmpty(idx.Scalar("Tech.Foh", "AdditionalHardware"));
        f.StageToFohSendCount = ParseInt(idx.Scalar("Tech.Foh", "StageToFohSendCount"));
        var roundTripCountStr = idx.Scalar("Tech.Foh", "StageToFohRoundTripCount");
        if (string.IsNullOrEmpty(roundTripCountStr))
        {
            // Backward safety: fall back to old bool key
            roundTripCountStr = ParseBool(idx.Scalar("Tech.Foh", "StageToFohRoundTrip")) ? "1" : "0";
        }
        f.StageToFohRoundTripCount = ParseInt(roundTripCountStr);
        f.FootprintWidthMeters = ParseDecNullable(idx.Scalar("Tech.Foh", "FootprintWidthMeters"));
        f.FootprintLengthMeters = ParseDecNullable(idx.Scalar("Tech.Foh", "FootprintLengthMeters"));
        f.Notes = NullIfEmpty(idx.Scalar("Tech.Foh", "Notes"));

        // Tech.Monitors
        var mo = t.Monitors;
        mo.SourceMode = ParseEnum(idx.Scalar("Tech.Monitors", "SourceMode"), MonitorSourceMode.None);
        mo.OwnConsoleModel = NullIfEmpty(idx.Scalar("Tech.Monitors", "OwnConsoleModel"));
        mo.OwnConsoleLocation = ParseEnum(idx.Scalar("Tech.Monitors", "OwnConsoleLocation"), MonitorTechLocation.OnStage);
        mo.Notes = NullIfEmpty(idx.Scalar("Tech.Monitors", "Notes"));

        foreach (var g in idx.Indexed("Tech.MonitorWedge"))
        {
            mo.Wedges.Add(new MonitorWedge
            {
                Where = GroupVal(g, "Where"),
                DualLinked = ParseBool(GroupVal(g, "DualLinked")),
                Stereo = ParseBool(GroupVal(g, "Stereo")),
                DrumFill = ParseBool(GroupVal(g, "DrumFill")),
            });
        }

        foreach (var g in idx.Indexed("Tech.InEar"))
        {
            mo.InEars.Add(new InEarMonitor
            {
                Where = GroupVal(g, "Where"),
                IsWireless = ParseBool(GroupVal(g, "IsWireless")),
                Provider = ParseEnum(GroupVal(g, "Provider"), CableProvider.Venue),
                Model = NullIfEmpty(GroupVal(g, "Model")),
                Frequency = NullIfEmpty(GroupVal(g, "Frequency")),
            });
        }

        // Tech.Stage
        var st = t.Stage;
        st.BringsOwnMics = ParseBool(idx.Scalar("Tech.Stage", "BringsOwnMics"));
        st.Notes = NullIfEmpty(idx.Scalar("Tech.Stage", "Notes"));

        foreach (var g in idx.Indexed("Tech.Riser"))
        {
            st.Risers.Add(new Riser
            {
                Where = GroupVal(g, "Where"),
                WidthMeters = ParseDec(GroupVal(g, "WidthMeters")),
                LengthMeters = ParseDec(GroupVal(g, "LengthMeters")),
                HeightCm = ParseInt(GroupVal(g, "HeightCm")),
            });
        }

        foreach (var g in idx.Indexed("Tech.OtherRiser"))
        {
            st.OtherRisers.Add(new OtherRiser
            {
                Where = GroupVal(g, "Where"),
                Type = ParseEnum(GroupVal(g, "Type"), OtherRiserType.EgoRiser),
                Description = NullIfEmpty(GroupVal(g, "Description")),
            });
        }

        foreach (var g in idx.Indexed("Tech.WirelessMic"))
        {
            st.WirelessMics.Add(new WirelessMic
            {
                Where = GroupVal(g, "Where"),
                Count = ParseInt(GroupVal(g, "Count")),
                Provider = ParseEnum(GroupVal(g, "Provider"), CableProvider.Venue),
                Model = NullIfEmpty(GroupVal(g, "Model")),
                Frequency = NullIfEmpty(GroupVal(g, "Frequency")),
            });
        }

        // Tech root
        t.Notes = NullIfEmpty(idx.Scalar("Tech", "Notes"));

        // Hospitality
        var h = band.Rider.Hospitality;
        h.DressingRoomNotes = NullIfEmpty(idx.Scalar("Hospitality", "DressingRoomNotes"));
        h.CateringNotes = NullIfEmpty(idx.Scalar("Hospitality", "CateringNotes"));
        h.DietaryRestrictions = NullIfEmpty(idx.Scalar("Hospitality", "DietaryRestrictions"));
        h.TowelCount = ParseInt(idx.Scalar("Hospitality", "TowelCount"));
        h.ParkingSpaces = ParseInt(idx.Scalar("Hospitality", "ParkingSpaces"));
        h.Accommodations = NullIfEmpty(idx.Scalar("Hospitality", "Accommodations"));
        foreach (var r in idx.Repeated("Hospitality", "Drink"))
            h.DrinksRequests.Add(r.Value);

        return band;
    }

    // ---- Show CSV ----

    public string ExportShowCsv(ShowData show)
    {
        var rows = new List<Row>();
        void S(string section, string key, string value, string index = "")
            => rows.Add(new Row { Section = section, Key = key, Value = value, Index = index });

        S("Show", "Id", show.Id.ToString());
        S("Show", "Name", show.Name);
        S("Show", "Address", show.Address ?? string.Empty);
        S("Show", "DateOfOpening", IsoDate(show.DateOfOpening));
        S("Show", "ShowDayCount", Inv(show.ShowDayCount));

        for (var i = 0; i < show.Stages.Count; i++)
        {
            var s = show.Stages[i]; var idx = Inv(i);
            S("Show.Stage", "Id", Inv(s.Id), idx);
            S("Show.Stage", "Name", s.Name, idx);
        }

        return Write(rows);
    }

    public ShowData ImportShowCsv(string csv)
    {
        var idx = new RowIndex(Read(csv));
        var show = new ShowData
        {
            Name = idx.Scalar("Show", "Name"),
            Address = NullIfEmpty(idx.Scalar("Show", "Address")),
            DateOfOpening = ParseDateOnly(idx.Scalar("Show", "DateOfOpening"), DateOnly.FromDateTime(DateTime.UtcNow)),
            ShowDayCount = ParseInt(idx.Scalar("Show", "ShowDayCount"), 1),
        };
        var idScalar = idx.Scalar("Show", "Id");
        if (!string.IsNullOrEmpty(idScalar)) show.Id = ParseGuid(idScalar, show.Id);
        foreach (var g in idx.Indexed("Show.Stage"))
        {
            show.Stages.Add(new Stage
            {
                Id = ParseInt(GroupVal(g, "Id")),
                Name = GroupVal(g, "Name"),
            });
        }
        return show;
    }

    // ---- Running order CSV ----

    private sealed class SlotRow
    {
        public string Id { get; set; } = string.Empty;
        public string ShowId { get; set; } = string.Empty;
        public string BandName { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public string OnStageTime { get; set; } = string.Empty;
        public string OnStageDayOffset { get; set; } = "0";
        public string IsOnStagePinned { get; set; } = string.Empty;
        public string SetLengthMinutes { get; set; } = string.Empty;
        public string SoundcheckOrderIndex { get; set; } = "0";
        public string BackstageTime { get; set; } = string.Empty;
        public string BackstageDayOffset { get; set; } = "0";
        public string IsBackstageTimePinned { get; set; } = string.Empty;
        public string BackstageLeadMinutes { get; set; } = string.Empty;
        public string BackstageCurfewTime { get; set; } = string.Empty;
        public string BackstageCurfewDayOffset { get; set; } = "0";
        public string IsBackstageCurfewPinned { get; set; } = string.Empty;
        public string CateringStart { get; set; } = string.Empty;
        public string CateringStartDayOffset { get; set; } = "0";
        public string CateringEnd { get; set; } = string.Empty;
        public string CateringEndDayOffset { get; set; } = "0";
        public string Flags { get; set; } = string.Empty;
        public string OverrideFlags { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    private static readonly CsvConfiguration SlotConfig = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        NewLine = "\n",
    };

    private string ResolveStageName(Guid showId, int stageId) =>
        _bands.FindStage(showId, stageId)?.Name ?? "Unknown stage";

    public string ResolveBandName(Guid bandId) =>
        _bands.FindBand(bandId)?.Name ?? "Unknown band";

    public string ResolveBandName(Guid showId, Guid bandId) =>
        _bands.FindBand(showId, bandId)?.Name ?? "Unknown band";

    private sealed class NoStageSlotRowMap : ClassMap<SlotRow>
    {
        public NoStageSlotRowMap()
        {
            Map(m => m.Id);
            Map(m => m.ShowId);
            Map(m => m.BandName);
            Map(m => m.OnStageTime);
            Map(m => m.OnStageDayOffset);
            Map(m => m.IsOnStagePinned);
            Map(m => m.SetLengthMinutes);
            Map(m => m.SoundcheckOrderIndex);
            Map(m => m.BackstageTime);
            Map(m => m.BackstageDayOffset);
            Map(m => m.IsBackstageTimePinned);
            Map(m => m.BackstageLeadMinutes);
            Map(m => m.BackstageCurfewTime);
            Map(m => m.BackstageCurfewDayOffset);
            Map(m => m.IsBackstageCurfewPinned);
            Map(m => m.CateringStart);
            Map(m => m.CateringStartDayOffset);
            Map(m => m.CateringEnd);
            Map(m => m.CateringEndDayOffset);
            Map(m => m.Flags);
            Map(m => m.OverrideFlags);
            Map(m => m.Notes);
        }
    }

    private static DateTime BaseDate(ShowData show, RunningOrder order)
        => show.DateOfOpening == default
            ? default
            : show.DateOfOpening.AddDays(Math.Max(0, order.ShowDayNumber - 1)).ToDateTime(TimeOnly.MinValue);

    private static (string time, string offset) FormatTime(DateTime? value, DateTime baseDate)
    {
        if (value is null) return (string.Empty, "0");
        var v = value.Value;
        var offset = baseDate == default ? 0 : (v.Date - baseDate.Date).Days;
        return (v.ToString("HH:mm", CultureInfo.InvariantCulture), offset.ToString(CultureInfo.InvariantCulture));
    }

    private static DateTime? ParseTime(string time, string offset, DateTime baseDate)
    {
        if (string.IsNullOrEmpty(time)) return null;
        if (!TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return null;
        var off = ParseInt(offset);
        var date = baseDate == default ? DateTime.MinValue : baseDate.Date.AddDays(off);
        return new DateTime(date.Year, date.Month, date.Day, t.Hour, t.Minute, 0, DateTimeKind.Unspecified);
    }

    private static string FormatFlags<T>(T flags) where T : struct, Enum
    {
        if (Convert.ToInt64(flags) == 0) return string.Empty;
        var parts = new List<string>();
        foreach (var name in Enum.GetNames<T>())
        {
            var value = (T)Enum.Parse(typeof(T), name);
            if (Convert.ToInt64(value) == 0) continue;
            if (flags.HasFlag(value)) parts.Add(name);
        }
        return string.Join(",", parts);
    }

    private static T ParseFlags<T>(string s) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(s)) return default;
        long acc = 0;
        foreach (var part in s.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0) continue;
            if (Enum.TryParse<T>(trimmed, ignoreCase: true, out var v))
                acc |= Convert.ToInt64(v);
        }
        return (T)Enum.ToObject(typeof(T), acc);
    }

    private string WriteSlotRows(RunningOrder order, ShowData show, IEnumerable<RunningOrderSlot> slots)
    {
        var baseDate = BaseDate(show, order);
        // Stable ordering: by on-stage time then stage name so diffs stay readable.
        var ordered = slots
            .OrderBy(s => s.OnStageTime ?? DateTime.MaxValue)
            .ThenBy(s => ResolveStageName(order.ShowId, s.StageId), StringComparer.Ordinal);

        using var sw = new StringWriter { NewLine = "\n" };
        using (var csv = new CsvWriter(sw, SlotConfig))
        {
            if (show.Stages.Count == 0)
                csv.Context.RegisterClassMap<NoStageSlotRowMap>();
            csv.WriteHeader<SlotRow>();
            csv.NextRecord();
            foreach (var s in ordered)
            {
                var (onStage, onStageOffset) = FormatTime(s.OnStageTime, baseDate);
                var (backstage, backstageOffset) = FormatTime(s.BackstageTime, baseDate);
                var (curfew, curfewOffset) = FormatTime(s.BackstageCurfewTime, baseDate);
                var (cateringStart, cateringStartOffset) = FormatTime(s.CateringSlot?.Start, baseDate);
                var (cateringEnd, cateringEndOffset) = FormatTime(s.CateringSlot?.End, baseDate);
                csv.WriteRecord(new SlotRow
                {
                    Id = s.Id.ToString(),
                    ShowId = order.ShowId.ToString(),
                    BandName = ResolveBandName(s.BandId),
                    Stage = ResolveStageName(order.ShowId, s.StageId),
                    OnStageTime = onStage,
                    OnStageDayOffset = onStageOffset,
                    IsOnStagePinned = s.IsOnStagePinned ? "true" : "false",
                    SetLengthMinutes = s.SetLengthMinutes is int sl ? Inv(sl) : string.Empty,
                    SoundcheckOrderIndex = Inv(s.SoundcheckOrderIndex),
                    BackstageTime = backstage,
                    BackstageDayOffset = backstageOffset,
                    IsBackstageTimePinned = s.IsBackstageTimePinned ? "true" : "false",
                    BackstageLeadMinutes = s.BackstageLeadMinutes is int bl ? Inv(bl) : string.Empty,
                    BackstageCurfewTime = curfew,
                    BackstageCurfewDayOffset = curfewOffset,
                    IsBackstageCurfewPinned = s.IsBackstageCurfewPinned ? "true" : "false",
                    CateringStart = cateringStart,
                    CateringStartDayOffset = cateringStartOffset,
                    CateringEnd = cateringEnd,
                    CateringEndDayOffset = cateringEndOffset,
                    Flags = FormatFlags(s.Flags),
                    OverrideFlags = FormatFlags(s.OverrideFlags),
                    Notes = s.Notes ?? string.Empty,
                });
                csv.NextRecord();
            }
        }
        return sw.ToString();
    }

    public string ExportRunningOrderCsv(RunningOrder order)
    {
        var show = _bands.FindShow(order.ShowId) ?? new ShowData();
        return WriteSlotRows(order, show, order.Slots);
    }

    public string ExportRunningOrderByStageCsv(RunningOrder order, int stageId)
    {
        var show = _bands.FindShow(order.ShowId) ?? new ShowData();
        return WriteSlotRows(order, show, order.Slots.Where(s => s.StageId == stageId));
    }

    public string ExportRunningOrderByBandCsv(RunningOrder order, Guid bandId)
    {
        var show = _bands.FindShow(order.ShowId) ?? new ShowData();
        return WriteSlotRows(order, show, order.Slots.Where(s => s.BandId == bandId));
    }

    public RunningOrder ImportRunningOrderCsv(string csv, ShowData show, IReadOnlyList<Band> bands)
    {
        using var sr = new StringReader(csv);
        using var rd = new CsvReader(sr, SlotConfig);
        if (show.Stages.Count == 0)
            rd.Context.RegisterClassMap<NoStageSlotRowMap>();
        var rows = rd.GetRecords<SlotRow>().ToList();

        var stageByName = show.Stages
            .GroupBy(s => s.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
        var bandByName = bands
            .GroupBy(b => b.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

        var order = new RunningOrder { ShowId = show.Id };
        var baseDate = BaseDate(show, order);
        foreach (var r in rows)
        {
            // Prefer the ShowId embedded in the row if present and parseable; falls back to the
            // show passed in by the caller so legacy rows with empty ShowId still import.
            if (Guid.TryParse(r.ShowId, out var rowShowId) && rowShowId != Guid.Empty)
                order.ShowId = rowShowId;
            var stageId = show.Stages.Count == 0 ? 0 : (stageByName.TryGetValue(r.Stage, out var sid) ? sid : 0);
            var bandId = bandByName.TryGetValue(r.BandName, out var bid) ? bid : Guid.Empty;

            var onStage = ParseTime(r.OnStageTime, r.OnStageDayOffset, baseDate);
            var backstage = ParseTime(r.BackstageTime, r.BackstageDayOffset, baseDate);
            var curfew = ParseTime(r.BackstageCurfewTime, r.BackstageCurfewDayOffset, baseDate);
            var cateringStart = ParseTime(r.CateringStart, r.CateringStartDayOffset, baseDate);
            var cateringEnd = ParseTime(r.CateringEnd, r.CateringEndDayOffset, baseDate);
            TimeSlot? cateringSlot = cateringStart is null
                ? null
                : new TimeSlot { Start = cateringStart.Value, End = cateringEnd };

            var slot = new RunningOrderSlot
            {
                Id = Guid.TryParse(r.Id, out var sidg) && sidg != Guid.Empty ? sidg : Guid.NewGuid(),
                BandId = bandId,
                StageId = stageId,
                OnStageTime = onStage,
                IsOnStagePinned = ParseBool(r.IsOnStagePinned),
                SetLengthMinutes = string.IsNullOrEmpty(r.SetLengthMinutes) ? null : ParseInt(r.SetLengthMinutes),
                SoundcheckOrderIndex = ParseInt(r.SoundcheckOrderIndex),
                BackstageTime = backstage,
                IsBackstageTimePinned = ParseBool(r.IsBackstageTimePinned),
                BackstageLeadMinutes = string.IsNullOrEmpty(r.BackstageLeadMinutes) ? null : ParseInt(r.BackstageLeadMinutes),
                BackstageCurfewTime = curfew,
                IsBackstageCurfewPinned = ParseBool(r.IsBackstageCurfewPinned),
                CateringSlot = cateringSlot,
                Flags = ParseFlags<BandScheduleFlags>(r.Flags),
                OverrideFlags = ParseFlags<UserOverrideFlags>(r.OverrideFlags),
                Notes = NullIfEmpty(r.Notes ?? string.Empty),
            };
            order.Slots.Add(slot);
        }
        return order;
    }
}
