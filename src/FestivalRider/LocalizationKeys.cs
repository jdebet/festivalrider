namespace FestivalRider;

public static class LocalizationKeys
{
    public static class Nav
    {
        public const string Bands = "nav.bands";
        public const string BandsV2 = "nav.bandsV2";
        public const string RunningOrder = "nav.runningOrder";
        public const string RunningOrderV2 = "nav.runningOrderV2";
        public const string Settings = "nav.settings";
        public const string Shows = "nav.shows";
        public const string Export = "nav.export";
        public const string Save = "nav.save";
        public static class Show
        {
            public const string Label = "nav.show.label";
            public const string Untitled = "nav.show.untitled";
        }
    }

    public static class Banner
    {
        public const string Multitab = "banner.multitab";
    }

    public static class Update
    {
        public const string Body = "update.body";
        public const string Reload = "update.reload";
        public const string Dismiss = "update.dismiss";
    }

    public static class Page
    {
        public static class Bands
        {
            public const string Title = "page.bands.title";
            public const string Heading = "page.bands.heading";
            public const string AddBand = "page.bands.addBand";
            public const string Empty = "page.bands.empty";
            public const string NewBandDefault = "page.bands.newBandDefault";
            public static class Card
            {
                public const string Edit = "page.bands.card.edit";
                public const string Print = "page.bands.card.print";
                public const string Delete = "page.bands.card.delete";
                public const string Contacts = "page.bands.card.contacts";
                public const string Travellers = "page.bands.card.travellers";
            }
        }

        public static class BandsV2
        {
            public const string Title = "page.bandsV2.title";
            public const string FilterPlaceholder = "page.bandsV2.filterPlaceholder";
            public static class Sort
            {
                public const string Label = "page.bandsV2.sort.label";
                public const string NameAsc = "page.bandsV2.sort.nameAsc";
                public const string NameDesc = "page.bandsV2.sort.nameDesc";
                public const string TravelParty = "page.bandsV2.sort.travelParty";
                public const string CreatedAt = "page.bandsV2.sort.createdAt";
                public const string UpdatedAt = "page.bandsV2.sort.updatedAt";
            }
        }

        public static class Editor
        {
            public const string BandNotFound = "page.editor.bandNotFound";
            public const string BackToBands = "page.editor.backToBands";
            public const string Save = "page.editor.save";
            public const string Print = "page.editor.print";
            public const string ExportCsv = "page.editor.exportCsv";
            public const string ImportCsv = "page.editor.importCsv";
            public const string Back = "page.editor.back";
            public const string TechHeading = "page.editor.tech.heading";
            public const string HospitalityHeading = "page.editor.hospitality.heading";
        }

        public static class Export
        {
            public const string Title = "page.export.title";
            public const string Heading = "page.export.heading";
            public const string NoRunningOrders = "page.export.noRunningOrders";
            public const string NoBands = "page.export.noBands";
        }

        public static class Save
        {
            public const string Title = "page.save.title";
            public const string Heading = "page.save.heading";
            public static class Status
            {
                public const string SchemaVersion = "page.save.status.schemaVersion";
                public const string Bands = "page.save.status.bands";
                public const string RunningOrders = "page.save.status.runningOrders";
            }
            public static class MasterBundle
            {
                public const string Description = "page.save.masterBundle.description";
                public const string ExportBtn = "page.save.masterBundle.exportBtn";
                public const string ImportBtn = "page.save.masterBundle.importBtn";
            }
        }

        public static class DangerZone
        {
            public const string ForceSave = "page.dangerZone.forceSave";
            public const string ClearAll = "page.dangerZone.clearAll";
        }

        public static class RunningOrder
        {
            public const string Title = "page.runningOrder.title";
            public const string Heading = "page.runningOrder.heading";
            public const string AddBtn = "page.runningOrder.addBtn";
            public const string NoStages = "page.runningOrder.noStages";
            public const string NoBands = "page.runningOrder.noBands";
            public const string Empty = "page.runningOrder.empty";
            public const string DayLabel = "page.runningOrder.dayLabel";
            public const string DateLabel = "page.runningOrder.dateLabel";
            public const string ExportCsv = "page.runningOrder.exportCsv";
            public const string DeleteBtn = "page.runningOrder.deleteBtn";
            public const string NoSlots = "page.runningOrder.noSlots";
            public const string AddSlotBtn = "page.runningOrder.addSlotBtn";
            public const string Stage = "page.runningOrder.stage";
            public const string Band = "page.runningOrder.band";
            public const string PickStageDefault = "page.runningOrder.pickStageDefault";
            public const string PickBandDefault = "page.runningOrder.pickBandDefault";
            public const string Export = "page.runningOrder.export";
            public const string Print = "page.runningOrder.print";
            public const string Role = "page.runningOrder.role";
            public const string UnknownBand = "page.runningOrder.unknownBand";
            public const string UnknownStage = "page.runningOrder.unknownStage";
            public static class Col
            {
                public const string Stage = "page.runningOrder.col.stage";
                public const string Start = "page.runningOrder.col.start";
                public const string Band = "page.runningOrder.col.band";
                public const string SetMin = "page.runningOrder.col.setMin";
                public const string Changeover = "page.runningOrder.col.changeover";
                public const string Notes = "page.runningOrder.col.notes";
            }
            public static class DayTitle
            {
                public const string WithDate = "page.runningOrder.dayTitle";
                public const string NoDate = "page.runningOrder.dayTitle.noDate";
            }
        }

        public static class Shows
        {
            public const string Title = "page.shows.title";
            public const string Heading = "page.shows.heading";
            public const string ExportShow = "page.shows.exportShow";
        }

        public static class Settings
        {
            public const string Title = "page.settings.title";
            public const string Heading = "page.settings.heading";
            public static class Shows
            {
                public const string NoShows = "page.settings.shows.noShows";
                public const string Description = "page.settings.shows.description";
                public const string Active = "page.settings.shows.active";
                public const string SetActive = "page.settings.shows.setActive";
                public const string Delete = "page.settings.shows.delete";
                public const string NamePlaceholder = "page.settings.shows.namePlaceholder";
                public const string AddBtn = "page.settings.shows.addBtn";
            }
            public static class ShowDetails
            {
                public const string Name = "page.settings.showDetails.name";
                public const string Address = "page.settings.showDetails.address";
                public const string DateOfOpening = "page.settings.showDetails.dateOfOpening";
                public const string ShowDayCount = "page.settings.showDetails.showDayCount";
                public const string Stages = "page.settings.showDetails.stages";
                public const string NoStages = "page.settings.showDetails.noStages";
                public const string NewStagePlaceholder = "page.settings.showDetails.newStagePlaceholder";
                public const string AddStageBtn = "page.settings.showDetails.addStageBtn";
                public const string SaveBtn = "page.settings.showDetails.saveBtn";
            }
            public static class ShowCsv
            {
                public const string Description = "page.settings.showCsv.description";
                public const string ExportBtn = "page.settings.showCsv.exportBtn";
                public const string ImportBtn = "page.settings.showCsv.importBtn";
            }
            public static class BandsCsv
            {
                public const string Description = "page.settings.bandsCsv.description";
                public const string ImportBtn = "page.settings.bandsCsv.importBtn";
                public const string NoBands = "page.settings.bandsCsv.noBands";
                public const string ExportBtn = "page.settings.bandsCsv.exportBtn";
            }
            public static class Bundle
            {
                public const string Description = "page.settings.bundle.description";
                public const string Replace = "page.settings.bundle.replace";
                public const string Merge = "page.settings.bundle.merge";
                public const string ExportBtn = "page.settings.bundle.exportBtn";
                public const string ImportBtn = "page.settings.bundle.importBtn";
            }
            public static class Storage
            {
                public const string SchemaVersion = "page.settings.storage.schemaVersion";
                public const string Bands = "page.settings.storage.bands";
                public const string RunningOrders = "page.settings.storage.runningOrders";
                public const string ForceSave = "page.settings.storage.forceSave";
                public const string ClearAll = "page.settings.storage.clearAll";
            }
        }

        public static class Print
        {
            public const string Loading = "page.print.loading";
            public const string Back = "page.print.back";
            public const string Action = "page.print.print";
            public static class NotFound
            {
                public const string Heading = "page.print.notFound.heading";
                public const string Message = "page.print.notFound.message";
            }
        }

        public static class Counter
        {
            public const string Title = "page.counter.title";
            public const string Heading = "page.counter.heading";
            public const string Count = "page.counter.count";
            public const string ClickMe = "page.counter.clickMe";
        }

        public static class Weather
        {
            public const string Title = "page.weather.title";
            public const string Heading = "page.weather.heading";
            public const string Description = "page.weather.description";
            public const string Loading = "page.weather.loading";
            public static class Col
            {
                public const string Date = "page.weather.col.date";
                public const string TempC = "page.weather.col.tempC";
                public const string TempF = "page.weather.col.tempF";
                public const string Summary = "page.weather.col.summary";
            }
        }
    }

    public static class Section
    {
        public const string BandTitle = "section.band.title";
        public const string ContactsTitle = "section.contacts.title";
        public const string TravelPartyTitle = "section.travelParty.title";
        public const string CablingTitle = "section.cabling.title";
        public const string LightingTitle = "section.lighting.title";
        public const string PowerTitle = "section.power.title";
        public const string FohTitle = "section.foh.title";
        public const string MonitorsTitle = "section.monitors.title";
        public const string StageTitle = "section.stage.title";
        public const string TechNotesTitle = "section.techNotes.title";
        public const string HospitalityTitle = "section.hospitality.title";
        public const string ShowsTitle = "section.shows.title";
        public const string ShowDetailsTitle = "section.showDetails.title";
        public const string ShowCsvTitle = "section.showCsv.title";
        public const string BandsCsvTitle = "section.bandsCsv.title";
        public const string BundleTitle = "section.bundle.title";
        public const string MasterBundleTitle = "section.masterBundle.title";
        public const string StorageTitle = "section.storage.title";
        public const string StatusTitle = "section.status.title";
        public const string DangerZoneTitle = "section.dangerZone.title";
    }

    public static class Field
    {
        public const string Name = "field.name";
        public const string Notes = "field.notes";
        public const string Role = "field.role";
        public const string Email = "field.email";
        public const string Phone = "field.phone";
        public const string Type = "field.type";
        public const string Description = "field.description";
        public const string Where = "field.where";
        public const string Count = "field.count";
        public const string Model = "field.model";
        public const string Frequency = "field.frequency";
        public const string Provider = "field.provider";
        public const string Spec = "field.spec";

        public static class Contact
        {
            public const string AddBtn = "field.contact.addBtn";
        }
        public static class TravelParty
        {
            public const string AddBtn = "field.travelParty.addBtn";
        }
        public static class Cable
        {
            public const string Source = "field.cable.source";
            public const string SourceOther = "field.cable.sourceOther";
            public const string Target = "field.cable.target";
            public const string TargetOther = "field.cable.targetOther";
            public const string Type = "field.cable.type";
            public const string TypeOther = "field.cable.typeOther";
            public const string MinLength = "field.cable.minLength";
            public const string MaxLength = "field.cable.maxLength";
            public const string AddBtn = "field.cable.addBtn";
        }
        public static class Lighting
        {
            public const string OwnConsole = "field.lighting.ownConsole";
            public const string BackdropWidth = "field.lighting.backdropWidth";
            public const string BackdropHeight = "field.lighting.backdropHeight";
            public const string FloorMachines = "field.lighting.floorMachines";
            public const string ModelOrType = "field.lighting.modelOrType";
            public const string Location = "field.lighting.location";
            public const string AddMachine = "field.lighting.addMachine";
        }
        public static class Power
        {
            public const string Amperage = "field.power.amperage";
            public const string Phase = "field.power.phase";
            public const string AdapterNotes = "field.power.adapterNotes";
        }
        public static class Foh
        {
            public const string OwnConsole = "field.foh.ownConsole";
            public const string OutputProtocol = "field.foh.outputProtocol";
            public const string OutputProtocolOther = "field.foh.outputProtocolOther";
            public const string OutputLocation = "field.foh.outputLocation";
            public const string OutputLocationOther = "field.foh.outputLocationOther";
            public const string OutputNotes = "field.foh.outputNotes";
            public const string AdditionalHardware = "field.foh.additionalHardware";
            public const string StageToFohSends = "field.foh.stageToFohSends";
            public const string RoundTripCount = "field.foh.roundTripCount";
            public const string FootprintWidth = "field.foh.footprintWidth";
            public const string FootprintLength = "field.foh.footprintLength";
        }
        public static class Monitors
        {
            public const string SourceMode = "field.monitors.sourceMode";
            public const string OwnConsole = "field.monitors.ownConsole";
            public const string ConsoleLocation = "field.monitors.consoleLocation";
            public const string Wedges = "field.monitors.wedges";
            public const string Dual = "field.monitors.dual";
            public const string Stereo = "field.monitors.stereo";
            public const string Drumfill = "field.monitors.drumfill";
            public const string AddWedge = "field.monitors.addWedge";
            public const string InEars = "field.monitors.inEars";
            public const string Wireless = "field.monitors.wireless";
            public const string AddIem = "field.monitors.addIem";
        }
        public static class Stage
        {
            public const string Risers = "field.stage.risers";
            public const string RiserWidth = "field.stage.riserWidth";
            public const string RiserLength = "field.stage.riserLength";
            public const string RiserHeight = "field.stage.riserHeight";
            public const string AddRiser = "field.stage.addRiser";
            public const string OtherRisers = "field.stage.otherRisers";
            public const string AddOtherRiser = "field.stage.addOtherRiser";
            public const string WirelessMics = "field.stage.wirelessMics";
            public const string AddWirelessMic = "field.stage.addWirelessMic";
            public const string BringsOwnMics = "field.stage.bringsOwnMics";
        }
        public static class Hospitality
        {
            public const string DressingRoom = "field.hospitality.dressingRoom";
            public const string Catering = "field.hospitality.catering";
            public const string DietaryRestrictions = "field.hospitality.dietaryRestrictions";
            public const string TowelCount = "field.hospitality.towelCount";
            public const string ParkingSpaces = "field.hospitality.parkingSpaces";
            public const string Accommodations = "field.hospitality.accommodations";
            public const string Drinks = "field.hospitality.drinks";
            public const string AddDrink = "field.hospitality.addDrink";
        }
    }

    public static class Toast
    {
        public static class Storage
        {
            public const string Unreadable = "toast.storage.unreadable";
            public const string MigrationFailed = "toast.storage.migrationFailed";
            public const string BackupReset = "toast.storage.backupReset";
            public const string Migrated = "toast.storage.migrated";
            public const string Empty = "toast.storage.empty";
            public const string Restored = "toast.storage.restored";
            public const string SaveFailed = "toast.storage.saveFailed";
        }
        public static class Editor
        {
            public const string Imported = "toast.editor.imported";
            public const string ImportFailed = "toast.editor.importFailed";
        }
        public static class Bands
        {
            public const string Created = "toast.bands.created";
            public const string Updated = "toast.bands.updated";
            public const string Deleted = "toast.bands.deleted";
        }
        public static class RunningOrder
        {
            public const string PickStage = "toast.runningOrder.pickStage";
            public const string PickBand = "toast.runningOrder.pickBand";
            public const string Deleted = "toast.runningOrder.deleted";
        }
        public static class Shows
        {
            public const string BundleExported = "toast.shows.bundleExported";
            public const string MasterBundleExported = "toast.shows.masterBundleExported";
        }
        public static class Settings
        {
            public const string ShowDetailsSaved = "toast.settings.showDetailsSaved";
            public const string StageAdded = "toast.settings.stageAdded";
            public const string StageDeleted = "toast.settings.stageDeleted";
            public const string ShowAdded = "toast.settings.showAdded";
            public const string ShowDeleted = "toast.settings.showDeleted";
            public const string ForceSaved = "toast.settings.forceSaved";
            public const string Cleared = "toast.settings.cleared";
            public const string ShowImported = "toast.settings.showImported";
            public const string BandImported = "toast.settings.bandImported";
            public const string BandReplaced = "toast.settings.bandReplaced";
            public const string ImportFailed = "toast.settings.importFailed";
            public const string BundleExported = "toast.settings.bundleExported";
            public const string BundleSizeExceeded = "toast.settings.bundleSizeExceeded";
            public const string BundleReadFailed = "toast.settings.bundleReadFailed";
            public const string BundleRejected = "toast.settings.bundleRejected";
            public const string BundleImported = "toast.settings.bundleImported";
            public const string BundleMergeApplied = "toast.settings.bundleMergeApplied";
        }
    }

    public static class Confirm
    {
        public static class DeleteBand
        {
            public const string Title = "confirm.deleteBand.title";
            public const string Message = "confirm.deleteBand.message";
            public const string Label = "confirm.deleteBand.label";
        }
        public static class DeleteRunningOrder
        {
            public const string Title = "confirm.deleteRunningOrder.title";
            public const string Label = "confirm.deleteRunningOrder.label";
            public const string Message = "confirm.deleteRunningOrder.message";
            public const string MessageFallback = "confirm.deleteRunningOrder.messageFallback";
        }
        public static class ClearData
        {
            public const string Title = "confirm.clearData.title";
            public const string Message = "confirm.clearData.message";
            public const string Label = "confirm.clearData.label";
        }
        public static class DeleteStage
        {
            public const string Title = "confirm.deleteStage.title";
            public const string Label = "confirm.deleteStage.label";
            public const string Message = "confirm.deleteStage.message";
        }
        public static class DeleteShow
        {
            public const string Title = "confirm.deleteShow.title";
            public const string Label = "confirm.deleteShow.label";
            public const string Message = "confirm.deleteShow.message";
            public const string MessageWithRo = "confirm.deleteShow.messageWithRo";
        }
        public static class BundleReplace
        {
            public const string Title = "confirm.bundleReplace.title";
            public const string Label = "confirm.bundleReplace.label";
            public const string Message = "confirm.bundleReplace.message";
        }
        public static class BundleMerge
        {
            public const string Title = "confirm.bundleMerge.title";
            public const string Label = "confirm.bundleMerge.label";
            public const string Message = "confirm.bundleMerge.message";
        }
    }

    public static class Bundle
    {
        public static class Error
        {
            public const string MissingManifest = "bundle.error.missingManifest";
            public const string InvalidManifestJson = "bundle.error.invalidManifestJson";
            public const string EmptyManifest = "bundle.error.emptyManifest";
            public const string UnknownFormat = "bundle.error.unknownFormat";
            public const string TooNew = "bundle.error.tooNew";
            public const string TooOld = "bundle.error.tooOld";
            public const string NoMigrator = "bundle.error.noMigrator";
            public const string ManifestParseFailed = "bundle.error.manifestParseFailed";
            public const string MigrationFailed = "bundle.error.migrationFailed";
            public const string MigratedManifestInvalid = "bundle.error.migratedManifestInvalid";
            public const string MigratedVersionMismatch = "bundle.error.migratedVersionMismatch";
            public const string PathTraversal = "bundle.error.pathTraversal";
            public const string NoShows = "bundle.error.noShows";
            public const string MissingShow = "bundle.error.missingShow";
            public const string MissingBand = "bundle.error.missingBand";
            public const string MissingRunningOrder = "bundle.error.missingRunningOrder";
            public const string NotZip = "bundle.error.notZip";
            public const string ImportFailed = "bundle.error.importFailed";
        }
        public static class Warning
        {
            public const string Unlisted = "bundle.warning.unlisted";
            public const string BandSkipped = "bundle.warning.bandSkipped";
            public const string RoNoShow = "bundle.warning.roNoShow";
            public const string RoNoLocalShow = "bundle.warning.roNoLocalShow";
            public const string RoMissingStages = "bundle.warning.roMissingStages";
            public const string RoReplaced = "bundle.warning.roReplaced";
        }
    }

    public static class Print
    {
        public const string Day = "print.day";
        public static class Band
        {
            public const string Title = "print.band.title";
            public const string TitleWithShow = "print.band.titleWithShow";
            public const string RoundTrip = "print.band.roundTrip";
            public const string ContactsHeading = "print.band.contactsHeading";
            public const string TravelPartyHeading = "print.band.travelPartyHeading";
            public const string TechHeading = "print.band.techHeading";
            public const string Cabling = "print.band.cabling";
            public const string Notes = "print.band.notes";
            public const string Wireless = "print.band.wireless";
            public const string Wired = "print.band.wired";
            public static class Col
            {
                public const string Role = "print.band.col.role";
                public const string Name = "print.band.col.name";
                public const string Email = "print.band.col.email";
                public const string Phone = "print.band.col.phone";
                public const string Type = "print.band.col.type";
                public const string Source = "print.band.col.source";
                public const string Target = "print.band.col.target";
                public const string Spec = "print.band.col.spec";
                public const string MinM = "print.band.col.minM";
                public const string MaxM = "print.band.col.maxM";
                public const string Provider = "print.band.col.provider";
            }
            public static class Field
            {
                public const string LightingConsole = "print.band.field.lightingConsole";
                public const string FloorMachines = "print.band.field.floorMachines";
                public const string Backdrop = "print.band.field.backdrop";
                public const string Power = "print.band.field.power";
                public const string PowerAdapterNotes = "print.band.field.powerAdapterNotes";
                public const string FohConsole = "print.band.field.fohConsole";
                public const string FohOutput = "print.band.field.fohOutput";
                public const string FohOutputNotes = "print.band.field.fohOutputNotes";
                public const string AdditionalHardware = "print.band.field.additionalHardware";
                public const string StageFohSends = "print.band.field.stageFohSends";
                public const string RoundTrip = "print.band.field.roundTrip";
                public const string FohFootprint = "print.band.field.fohFootprint";
                public const string FohNotes = "print.band.field.fohNotes";
                public const string MonitorSource = "print.band.field.monitorSource";
                public const string MonitorConsole = "print.band.field.monitorConsole";
                public const string MonitorLocation = "print.band.field.monitorLocation";
                public const string Wedges = "print.band.field.wedges";
                public const string InEars = "print.band.field.inEars";
                public const string MonitorNotes = "print.band.field.monitorNotes";
                public const string Risers = "print.band.field.risers";
                public const string OtherRisers = "print.band.field.otherRisers";
                public const string WirelessMics = "print.band.field.wirelessMics";
                public const string Mics = "print.band.field.mics";
                public const string BringsOwnMics = "print.band.field.bringsOwnMics";
                public const string StageNotes = "print.band.field.stageNotes";
                public const string TechNotes = "print.band.field.techNotes";
                public const string DressingRoom = "print.band.field.dressingRoom";
                public const string Catering = "print.band.field.catering";
                public const string Drinks = "print.band.field.drinks";
                public const string DietaryRestrictions = "print.band.field.dietaryRestrictions";
                public const string Towels = "print.band.field.towels";
                public const string ParkingSpaces = "print.band.field.parkingSpaces";
                public const string Accommodations = "print.band.field.accommodations";
            }
        }
        public static class Stage
        {
            public const string ScheduleHeading = "print.stage.scheduleHeading";
            public const string NoSlots = "print.stage.noSlots";
            public const string TechSummaryHeading = "print.stage.techSummaryHeading";
            public const string UnknownBand = "print.stage.unknownBand";
            public const string Min = "print.stage.min";
            public static class Col
            {
                public const string Start = "print.stage.col.start";
                public const string Set = "print.stage.col.set";
                public const string Changeover = "print.stage.col.changeover";
                public const string Band = "print.stage.col.band";
                public const string Notes = "print.stage.col.notes";
                public const string Power = "print.stage.col.power";
                public const string FohConsole = "print.stage.col.fohConsole";
                public const string Monitors = "print.stage.col.monitors";
                public const string Wedges = "print.stage.col.wedges";
                public const string Iems = "print.stage.col.iems";
                public const string Risers = "print.stage.col.risers";
            }
        }
        public static class Role
        {
            public const string NoSlots = "print.role.noSlots";
            public const string NoContact = "print.role.noContact";
            public const string RoundTrip = "print.role.roundTrip";
            public static class Col
            {
                public const string Name = "print.role.col.name";
                public const string Email = "print.role.col.email";
                public const string Phone = "print.role.col.phone";
            }
            public static class Field
            {
                public const string FohConsole = "print.role.field.fohConsole";
                public const string FohOutput = "print.role.field.fohOutput";
                public const string OutputNotes = "print.role.field.outputNotes";
                public const string AdditionalHardware = "print.role.field.additionalHardware";
                public const string StageFohSends = "print.role.field.stageFohSends";
                public const string FohFootprint = "print.role.field.fohFootprint";
                public const string FohNotes = "print.role.field.fohNotes";
                public const string Source = "print.role.field.source";
                public const string MonitorConsole = "print.role.field.monitorConsole";
                public const string ConsoleLocation = "print.role.field.consoleLocation";
                public const string Wedges = "print.role.field.wedges";
                public const string InEars = "print.role.field.inEars";
                public const string MonitorNotes = "print.role.field.monitorNotes";
                public const string Power = "print.role.field.power";
                public const string AdapterNotes = "print.role.field.adapterNotes";
                public const string Risers = "print.role.field.risers";
                public const string OtherRisers = "print.role.field.otherRisers";
                public const string WirelessMics = "print.role.field.wirelessMics";
                public const string BringsOwn = "print.role.field.bringsOwn";
                public const string StageNotes = "print.role.field.stageNotes";
                public const string TravelParty = "print.role.field.travelParty";
                public const string BandNotes = "print.role.field.bandNotes";
                public const string Mics = "print.role.field.mics";
            }
        }
    }

    public static class Enum
    {
        public static class ContactRole
        {
            public const string TourManager = "enum.ContactRole.TourManager";
            public const string BandManager = "enum.ContactRole.BandManager";
            public const string FohEngineer = "enum.ContactRole.FOHEngineer";
            public const string MonitorEngineer = "enum.ContactRole.MonitorEngineer";
            public const string StageManager = "enum.ContactRole.StageManager";
            public const string BackingTech = "enum.ContactRole.BackingTech";
            public const string Other = "enum.ContactRole.Other";
        }
        public static class PartyType
        {
            public const string BandMember = "enum.PartyType.BandMember";
            public const string Tech = "enum.PartyType.Tech";
            public const string Production = "enum.PartyType.Production";
        }
        public static class CableType
        {
            public const string Rj45 = "enum.CableType.RJ45";
            public const string Bnc = "enum.CableType.BNC";
            public const string Fiber = "enum.CableType.Fiber";
            public const string Other = "enum.CableType.Other";
        }
        public static class CablePoint
        {
            public const string SoundFoh = "enum.CablePoint.SoundFoh";
            public const string LightFoh = "enum.CablePoint.LightFoh";
            public const string StageLeft = "enum.CablePoint.StageLeft";
            public const string StageRight = "enum.CablePoint.StageRight";
            public const string StageCenter = "enum.CablePoint.StageCenter";
            public const string AmpRack = "enum.CablePoint.AmpRack";
            public const string MonitorWorld = "enum.CablePoint.MonitorWorld";
            public const string Other = "enum.CablePoint.Other";
        }
        public static class CableProvider
        {
            public const string Venue = "enum.CableProvider.Venue";
            public const string Brought = "enum.CableProvider.Brought";
        }
        public static class MonitorSourceMode
        {
            public const string None = "enum.MonitorSourceMode.None";
            public const string OwnConsole = "enum.MonitorSourceMode.OwnConsole";
            public const string FromFoh = "enum.MonitorSourceMode.FromFoh";
        }
        public static class MonitorTechLocation
        {
            public const string OnStage = "enum.MonitorTechLocation.OnStage";
            public const string OwnFootprint = "enum.MonitorTechLocation.OwnFootprint";
        }
        public static class PowerPhase
        {
            public const string SinglePhase = "enum.PowerPhase.SinglePhase";
            public const string ThreePhase = "enum.PowerPhase.ThreePhase";
        }
        public static class PowerAmperage
        {
            public const string _16A = "enum.PowerAmperage._16_A";
            public const string _32A = "enum.PowerAmperage._32_A";
            public const string _63A = "enum.PowerAmperage._63_A";
        }
        public static class OutputProtocol
        {
            public const string Aes = "enum.OutputProtocol.Aes";
            public const string Analog = "enum.OutputProtocol.Analog";
            public const string Other = "enum.OutputProtocol.Other";
        }
        public static class OutputLocation
        {
            public const string Foh = "enum.OutputLocation.Foh";
            public const string Stage = "enum.OutputLocation.Stage";
            public const string Other = "enum.OutputLocation.Other";
        }
        public static class OtherRiserType
        {
            public const string EgoRiser = "enum.OtherRiserType.EgoRiser";
            public const string Custom = "enum.OtherRiserType.Custom";
        }
        public static class TimingEventType
        {
            public const string GetIn = "enum.TimingEventType.GET_IN";
            public const string LoadInVenue = "enum.TimingEventType.LOAD_IN_VENUE";
            public const string LoadInStage = "enum.TimingEventType.LOAD_IN_STAGE";
            public const string BackstageDrop = "enum.TimingEventType.BACKSTAGE_DROP";
            public const string Catering = "enum.TimingEventType.CATERING";
            public const string SetupOnStage = "enum.TimingEventType.SETUP_ON_STAGE";
            public const string Soundcheck = "enum.TimingEventType.SOUNDCHECK";
            public const string Changeover = "enum.TimingEventType.CHANGEOVER";
            public const string PreshowLinecheck = "enum.TimingEventType.PRESHOW_LINECHECK";
            public const string OnStage = "enum.TimingEventType.ON_STAGE";
            public const string LoadOutStaging = "enum.TimingEventType.LOAD_OUT_STAGING";
            public const string LoadOutVenue = "enum.TimingEventType.LOAD_OUT_VENUE";
            public const string BackstageWait = "enum.TimingEventType.BACKSTAGE_WAIT";
        }
        public static class ScheduleMode
        {
            public const string Traditional = "enum.ScheduleMode.Traditional";
            public const string Festival = "enum.ScheduleMode.Festival";
        }
        public static class ScheduleWarningType
        {
            public const string BreakTimeViolation = "enum.ScheduleWarningType.BreakTimeViolation";
            public const string SoundcheckBlockOverlap = "enum.ScheduleWarningType.SoundcheckBlockOverlap";
            public const string OnStageOverlap = "enum.ScheduleWarningType.OnStageOverlap";
            public const string BackwardLockConflict = "enum.ScheduleWarningType.BackwardLockConflict";
            public const string BarrierConflict = "enum.ScheduleWarningType.BarrierConflict";
            public const string CateringOutsideHours = "enum.ScheduleWarningType.CateringOutsideHours";
            public const string CurfewViolation = "enum.ScheduleWarningType.CurfewViolation";
            public const string SoundcheckShrunk = "enum.ScheduleWarningType.SoundcheckShrunk";
            public const string SoundcheckOrderOverlap = "enum.ScheduleWarningType.SoundcheckOrderOverlap";
            public const string UserOverrideOverlap = "enum.ScheduleWarningType.UserOverrideOverlap";
            public const string EarlySoundcheckAfterOnStage = "enum.ScheduleWarningType.EarlySoundcheckAfterOnStage";
            public const string ConstraintViolation = "enum.ScheduleWarningType.ConstraintViolation";
            public const string FirstShowTimeMissing = "enum.ScheduleWarningType.FirstShowTimeMissing";
            public const string VenueClosed = "enum.ScheduleWarningType.VenueClosed";
        }
        public static class BandScheduleFlags
        {
            public const string None = "enum.BandScheduleFlags.None";
            public const string HasPersonalBackstageCurfew = "enum.BandScheduleFlags.HasPersonalBackstageCurfew";
        }
        public static class UserOverrideFlags
        {
            public const string None = "enum.UserOverrideFlags.None";
            public const string AllowSoundcheckOverlap = "enum.UserOverrideFlags.AllowSoundcheckOverlap";
            public const string AllowOnStageOverlap = "enum.UserOverrideFlags.AllowOnStageOverlap";
        }
        public static class StageLinkConstraint
        {
            public const string All = "enum.StageLinkConstraint.All";
            public const string OnStageOnly = "enum.StageLinkConstraint.OnStageOnly";
        }
    }
}
